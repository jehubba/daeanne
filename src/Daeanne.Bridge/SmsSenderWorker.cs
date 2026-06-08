using System.Text.Json;
using Daeanne.Shared.Models;

namespace Daeanne.Bridge;

/// <summary>
/// Polls the Dispatcher SMS outbox and sends messages via ACS SMS API.
///
/// Activation: requires Sms:AcsConnectionString and Sms:FromNumber in config.
/// When not configured, logs a one-time warning and exits — the outbox accumulates
/// pending items until ACS SMS is set up, then they drain on next start.
///
/// ACS SMS REST: POST https://{endpoint}/sms?api-version=2021-03-07
/// Body: { "from": "+1...", "smsRecipients": [{ "to": "+1..." }], "message": "..." }
/// </summary>
public class SmsSenderWorker(
    ILogger<SmsSenderWorker> logger,
    IConfiguration config,
    IHttpClientFactory http) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connStr    = config["Sms:AcsConnectionString"];
        var fromNumber = config["Sms:FromNumber"];
        var dispatcherUrl = config["Bridge:DispatcherUrl"] ?? "http://127.0.0.1:47777";

        if (string.IsNullOrWhiteSpace(connStr) || string.IsNullOrWhiteSpace(fromNumber))
        {
            logger.LogWarning(
                "SmsSenderWorker: Sms:AcsConnectionString or Sms:FromNumber not configured. " +
                "SMS sending is disabled until these are set.");
            return;
        }

        // Parse ACS endpoint + access key from connection string
        // Format: endpoint=https://...;accesskey=...
        var endpoint   = ParseConnStrValue(connStr, "endpoint");
        var accessKey  = ParseConnStrValue(connStr, "accesskey");

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(accessKey))
        {
            logger.LogError("SmsSenderWorker: could not parse endpoint/accesskey from ACS connection string.");
            return;
        }

        var pollSeconds = config.GetValue<int>("Sms:PollIntervalSeconds", 10);
        logger.LogInformation("SmsSenderWorker: started, polling every {Poll}s → {From}", pollSeconds, fromNumber);

        while (!stoppingToken.IsCancellationRequested)
        {
            await SendPendingAsync(endpoint, accessKey, fromNumber, dispatcherUrl, stoppingToken);

            try { await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SendPendingAsync(
        string endpoint, string accessKey, string fromNumber,
        string dispatcherUrl, CancellationToken ct)
    {
        using var client = http.CreateClient();

        // Fetch pending SMS from Dispatcher outbox
        List<OutboxSms>? pending;
        try
        {
            var json = await client.GetStringAsync($"{dispatcherUrl}/outbox/sms?status=Pending&take=20", ct);
            pending = JsonSerializer.Deserialize<List<OutboxSms>>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SmsSenderWorker: could not fetch pending SMS from Dispatcher");
            return;
        }

        if (pending is null || pending.Count == 0) return;

        foreach (var sms in pending)
        {
            // Mark Sending
            await PatchStatusAsync(client, dispatcherUrl, sms.Id, "Sending", null, ct);

            try
            {
                await SendViaCsAsync(client, endpoint, accessKey, fromNumber, sms.To, sms.Body, ct);
                await PatchStatusAsync(client, dispatcherUrl, sms.Id, "Sent", null, ct);
                logger.LogInformation("SmsSenderWorker: sent SMS {Id} to {To}", sms.Id, sms.To);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SmsSenderWorker: failed to send SMS {Id}", sms.Id);
                await PatchStatusAsync(client, dispatcherUrl, sms.Id, "Failed", ex.Message, ct);
            }
        }
    }

    private static async Task SendViaCsAsync(
        HttpClient client, string endpoint, string accessKey,
        string from, string to, string body, CancellationToken ct)
    {
        // ACS SMS REST API
        // https://learn.microsoft.com/en-us/rest/api/communication/sms/send
        var url = $"{endpoint.TrimEnd('/')}/sms?api-version=2021-03-07";
        var payload = JsonSerializer.Serialize(new
        {
            from,
            smsRecipients = new[] { new { to } },
            message       = body
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

        // ACS HMAC-SHA256 auth — placeholder; real implementation requires signing the request.
        // See: https://learn.microsoft.com/en-us/azure/communication-services/concepts/authentication
        // For now, add the access key as a header stub so this compiles and the path is clear.
        req.Headers.Add("Authorization", $"Bearer {accessKey}");

        var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task PatchStatusAsync(
        HttpClient client, string dispatcherUrl,
        Guid id, string status, string? error, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { status, error });
        try
        {
            await client.PatchAsync(
                $"{dispatcherUrl}/outbox/sms/{id}/status",
                new StringContent(body, System.Text.Encoding.UTF8, "application/json"), ct);
        }
        catch (Exception ex)
        {
            // Log but don't rethrow — a status update failure shouldn't abort the batch
            Console.Error.WriteLine($"SmsSenderWorker: status patch failed for {id}: {ex.Message}");
        }
    }

    private static string? ParseConnStrValue(string connStr, string key)
    {
        foreach (var part in connStr.Split(';'))
        {
            var idx = part.IndexOf('=');
            if (idx < 0) continue;
            if (part[..idx].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                return part[(idx + 1)..].Trim();
        }
        return null;
    }
}
