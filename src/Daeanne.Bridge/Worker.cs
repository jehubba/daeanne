using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Daeanne.Shared.Models;

namespace Daeanne.Bridge;

/// <summary>
/// Bidirectional bridge between Azure Service Bus and the local Kestrel Dispatcher.
///
/// Inbound:  daeanne-inbox queue → POST /tasks (type=Email, prompt=formatted email)
/// Outbound: GET /outbox/email?status=Pending → PATCH Processing → publish to
///           daeanne-outbox → PATCH Sent/Failed
///
/// Runs in DISABLED mode when ConnectionStrings:ServiceBus is absent.
/// </summary>
public class BridgeWorker : BackgroundService
{
    private readonly ILogger<BridgeWorker> _logger;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _http;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public BridgeWorker(ILogger<BridgeWorker> logger, IConfiguration config, IHttpClientFactory http)
    {
        _logger = logger;
        _config = config;
        _http = http;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = _config.GetConnectionString("ServiceBus");
        var dispatcherUrl = _config["Bridge:DispatcherUrl"] ?? "http://127.0.0.1:47777";
        var inboundQueue = _config["Bridge:InboundQueue"] ?? "daeanne-inbox";
        var outboundQueue = _config["Bridge:OutboundQueue"] ?? "daeanne-outbox";
        var pollIntervalSeconds = int.TryParse(_config["Bridge:OutboundPollIntervalSeconds"], out var s) ? s : 10;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogWarning(
                "Daeanne.Bridge running in DISABLED mode — " +
                "ConnectionStrings:ServiceBus not configured.");
            await Task.Delay(Timeout.Infinite, stoppingToken);
            return;
        }

        _logger.LogInformation("Daeanne.Bridge starting. Dispatcher: {Url}", dispatcherUrl);

        await using var sbClient = new ServiceBusClient(connectionString);

        var inboundTask = RunInboundAsync(sbClient, inboundQueue, dispatcherUrl, stoppingToken);
        var outboundTask = RunOutboundAsync(sbClient, outboundQueue, dispatcherUrl,
            TimeSpan.FromSeconds(pollIntervalSeconds), stoppingToken);

        await Task.WhenAll(inboundTask, outboundTask);
    }

    // ─── INBOUND: Service Bus → Dispatcher ───────────────────────────────────

    private async Task RunInboundAsync(
        ServiceBusClient sbClient,
        string queueName,
        string dispatcherUrl,
        CancellationToken ct)
    {
        var options = new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 2,
            AutoCompleteMessages = false
        };

        await using var processor = sbClient.CreateProcessor(queueName, options);

        processor.ProcessMessageAsync += async args =>
        {
            try
            {
                var msg = args.Message.Body.ToObjectFromJson<BridgeEmailMessage>(JsonOpts);
                if (msg is null)
                {
                    _logger.LogWarning("Inbound: could not deserialize message {Id}, dead-lettering.",
                        args.Message.MessageId);
                    await args.DeadLetterMessageAsync(args.Message, "DeserializationFailed", cancellationToken: ct);
                    return;
                }

                var prompt = BuildEmailPrompt(msg);
                var body = JsonSerializer.Serialize(new
                {
                    type = "Email",
                    prompt,
                    correlationId = msg.AcsMessageId ?? msg.MessageId.ToString()
                });

                var http = _http.CreateClient("dispatcher");
                var response = await http.PostAsync(
                    $"{dispatcherUrl}/tasks",
                    new StringContent(body, Encoding.UTF8, "application/json"),
                    ct);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Inbound: task created for email from {From} re: {Subject}",
                        msg.From, msg.Subject);
                    await args.CompleteMessageAsync(args.Message, ct);
                }
                else
                {
                    var err = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogError("Inbound: Dispatcher rejected task. Status {Code}: {Err}",
                        response.StatusCode, err);
                    await args.AbandonMessageAsync(args.Message, cancellationToken: ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Inbound: unhandled error processing message {Id}",
                    args.Message.MessageId);
                await args.AbandonMessageAsync(args.Message, cancellationToken: ct);
            }
        };

        processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(args.Exception, "Inbound Service Bus error. Source: {Source}",
                args.ErrorSource);
            return Task.CompletedTask;
        };

        await processor.StartProcessingAsync(ct);
        _logger.LogInformation("Inbound processor started on queue '{Queue}'.", queueName);

        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { /* stopping */ }

        await processor.StopProcessingAsync();
    }

    private static string BuildEmailPrompt(BridgeEmailMessage msg) =>
        $"""
        INBOUND EMAIL
        From: {msg.From}
        To: {msg.To}
        Subject: {msg.Subject}
        Received: {msg.Timestamp:u}

        ---
        {msg.BodyText}
        """;

    // ─── OUTBOUND: Dispatcher → Service Bus ──────────────────────────────────

    private async Task RunOutboundAsync(
        ServiceBusClient sbClient,
        string queueName,
        string dispatcherUrl,
        TimeSpan pollInterval,
        CancellationToken ct)
    {
        await using var sender = sbClient.CreateSender(queueName);
        _logger.LogInformation("Outbound loop started. Polling every {Interval}s for pending emails.",
            pollInterval.TotalSeconds);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingEmailsAsync(sender, dispatcherUrl, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Outbound: error in poll cycle.");
            }

            await Task.Delay(pollInterval, ct);
        }
    }

    private async Task ProcessPendingEmailsAsync(
        ServiceBusSender sender,
        string dispatcherUrl,
        CancellationToken ct)
    {
        var http = _http.CreateClient("dispatcher");

        // Fetch pending emails (also retry stale Processing emails)
        var pendingJson = await http.GetStringAsync(
            $"{dispatcherUrl}/outbox/email?status=Pending&take=20", ct);
        var processingJson = await http.GetStringAsync(
            $"{dispatcherUrl}/outbox/email?status=Processing&take=20", ct);

        var pending = JsonSerializer.Deserialize<OutboxEmail[]>(pendingJson, JsonOpts) ?? [];
        var processing = JsonSerializer.Deserialize<OutboxEmail[]>(processingJson, JsonOpts) ?? [];

        // Stale Processing = created > 2 minutes ago (likely from a previous crashed Bridge run)
        var stale = processing.Where(e => DateTime.UtcNow - e.CreatedAt > TimeSpan.FromMinutes(2));
        var toSend = pending.Concat(stale).ToArray();

        foreach (var email in toSend)
        {
            await TrySendOutboundEmailAsync(email, sender, dispatcherUrl, http, ct);
        }
    }

    private async Task TrySendOutboundEmailAsync(
        OutboxEmail email,
        ServiceBusSender sender,
        string dispatcherUrl,
        HttpClient http,
        CancellationToken ct)
    {
        // Claim the email
        var claimBody = JsonSerializer.Serialize(new { status = "Processing" });
        var claimResp = await http.PatchAsync(
            $"{dispatcherUrl}/outbox/email/{email.Id}/status",
            new StringContent(claimBody, Encoding.UTF8, "application/json"), ct);

        if (!claimResp.IsSuccessStatusCode)
        {
            _logger.LogDebug("Outbound: could not claim email {Id} — skipping.", email.Id);
            return;
        }

        var bridgeMsg = new BridgeEmailMessage
        {
            From = "daeanne@daeanne.local",
            To = email.To,
            Subject = email.Subject,
            BodyText = email.Body,
            OutboxEmailId = email.Id,
            MessageId = Guid.NewGuid()
        };

        try
        {
            var sbMessage = new ServiceBusMessage(
                BinaryData.FromObjectAsJson(bridgeMsg, JsonOpts))
            {
                ContentType = "application/json",
                MessageId = bridgeMsg.MessageId.ToString(),
                CorrelationId = email.CorrelationId
            };

            await sender.SendMessageAsync(sbMessage, ct);

            var sentBody = JsonSerializer.Serialize(new { status = "Sent" });
            await http.PatchAsync(
                $"{dispatcherUrl}/outbox/email/{email.Id}/status",
                new StringContent(sentBody, Encoding.UTF8, "application/json"), ct);

            _logger.LogInformation("Outbound: published email {Id} to '{To}' re: {Subject}",
                email.Id, email.To, email.Subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Outbound: failed to publish email {Id} to Service Bus.", email.Id);

            var failBody = JsonSerializer.Serialize(new { status = "Failed", error = ex.Message });
            await http.PatchAsync(
                $"{dispatcherUrl}/outbox/email/{email.Id}/status",
                new StringContent(failBody, Encoding.UTF8, "application/json"), ct);
        }
    }
}

