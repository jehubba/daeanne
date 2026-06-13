using System.Net;
using System.Text.Json;
using DaeanneFrontend.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using WebPush;

namespace DaeanneFrontend.Api;

/// <summary>
/// POST /api/notify — internal endpoint called by Bridge on task completion.
/// Fans out a push notification to all stored subscriptions using VAPID.
///
/// Secured by the NOTIFY_INTERNAL_KEY environment variable (X-Internal-Key header).
/// Required environment variables:
///   VAPID_PUBLIC_KEY  — Base64url-encoded VAPID public key
///   VAPID_PRIVATE_KEY — Base64url-encoded VAPID private key (secret; never commit)
///   VAPID_SUBJECT     — mailto: or https: VAPID subject claim
///   NOTIFY_INTERNAL_KEY — shared secret for internal callers
///
/// Payload shape:
/// {
///   "type": "task_complete|escalation|alert|summary",
///   "title": "string",
///   "body":  "string",
///   "taskId": "string | null",
///   "url":   "/tasks/{id}"
/// }
/// </summary>
public class NotifyFunction
{
    private readonly PushSubscriptionStore? _store;
    private readonly ILogger<NotifyFunction> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public NotifyFunction(ILogger<NotifyFunction> logger, PushSubscriptionStore? store = null)
    {
        _logger = logger;
        _store = store;
    }

    /// <summary>GET /api/push/vapid-public-key — returns the VAPID public key for the client to use.</summary>
    [Function("vapidPublicKey")]
    public IActionResult GetVapidPublicKey(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "push/vapid-public-key")] HttpRequest req)
    {
        var publicKey = Environment.GetEnvironmentVariable("VAPID_PUBLIC_KEY");
        if (string.IsNullOrWhiteSpace(publicKey))
            return new NotFoundObjectResult(new { error = "Push notifications not configured" });

        return new OkObjectResult(new { publicKey });
    }

    [Function("notify")]
    public async Task<IActionResult> PostNotify(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "notify")] HttpRequest req)
    {
        // Verify internal key
        var expectedKey = Environment.GetEnvironmentVariable("NOTIFY_INTERNAL_KEY");
        if (!string.IsNullOrWhiteSpace(expectedKey))
        {
            req.Headers.TryGetValue("X-Internal-Key", out var providedKey);
            if (providedKey.FirstOrDefault() != expectedKey)
                return new StatusCodeResult((int)HttpStatusCode.Forbidden);
        }

        if (_store is null)
        {
            _logger.LogWarning("NotifyFunction: storage not configured.");
            return new StatusCodeResult(503);
        }

        var vapidPublicKey = Environment.GetEnvironmentVariable("VAPID_PUBLIC_KEY");
        var vapidPrivateKey = Environment.GetEnvironmentVariable("VAPID_PRIVATE_KEY");
        var vapidSubject = Environment.GetEnvironmentVariable("VAPID_SUBJECT");

        if (string.IsNullOrWhiteSpace(vapidPublicKey) || string.IsNullOrWhiteSpace(vapidPrivateKey))
        {
            _logger.LogWarning("NotifyFunction: VAPID keys not configured — notification dropped.");
            return new StatusCodeResult(503);
        }

        if (string.IsNullOrWhiteSpace(vapidSubject))
        {
            _logger.LogWarning("NotifyFunction: VAPID_SUBJECT not configured — notification dropped.");
            return new StatusCodeResult(503);
        }

        NotifyPayload? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<NotifyPayload>(req.Body, JsonOpts);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new { error = "Invalid payload JSON" });
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Title))
            return new BadRequestObjectResult(new { error = "title is required" });

        var subscriptions = await _store.GetAllAsync();
        if (subscriptions.Count == 0)
        {
            _logger.LogInformation("NotifyFunction: no subscriptions — nothing to send.");
            return new OkObjectResult(new { sent = 0 });
        }

        var vapidDetails = new VapidDetails(vapidSubject, vapidPublicKey, vapidPrivateKey);
        var webPushClient = new WebPushClient();
        var payloadJson = JsonSerializer.Serialize(payload, JsonOpts);

        int sent = 0, failed = 0;
        var stale = new List<string>();

        foreach (var sub in subscriptions)
        {
            var pushSubscription = new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
            try
            {
                await webPushClient.SendNotificationAsync(pushSubscription, payloadJson, vapidDetails);
                sent++;
            }
            catch (WebPushException ex) when (ex.StatusCode == HttpStatusCode.Gone
                                               || ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Subscription is no longer valid — remove it
                stale.Add(sub.Endpoint);
                failed++;
                _logger.LogInformation("NotifyFunction: removed stale subscription ({Status}).", ex.StatusCode);
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "NotifyFunction: push send failed for one subscription.");
            }
        }

        // Clean up stale subscriptions
        foreach (var endpoint in stale)
            await _store.DeleteAsync(endpoint);

        _logger.LogInformation("NotifyFunction: sent={Sent}, failed={Failed}.", sent, failed);
        return new OkObjectResult(new { sent, failed });
    }

    private record NotifyPayload(
        string? Type,
        string? Title,
        string? Body,
        string? TaskId,
        string? Url);
}
