using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Daeanne.Shared.Models;

namespace Daeanne.Bridge;

/// <summary>
/// Inbound bridge: Azure Service Bus → local Kestrel Dispatcher.
/// Listens on daeanne-inbox queue and creates Email tasks in the Dispatcher.
///
/// Outbound email is handled exclusively by GraphMailWorker (Graph API).
/// This worker does NOT poll /outbox/email — it only processes inbound SB messages.
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

        await RunInboundAsync(sbClient, inboundQueue, dispatcherUrl, stoppingToken);
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
}
