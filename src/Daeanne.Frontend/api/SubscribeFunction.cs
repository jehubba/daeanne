using System.Text.Json;
using DaeanneFrontend.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DaeanneFrontend.Api;

/// <summary>
/// POST /api/subscribe — stores a browser PushSubscription for the authenticated user.
///
/// The browser sends the standard PushSubscription JSON:
/// {
///   "endpoint": "https://fcm.googleapis.com/...",
///   "keys": { "p256dh": "...", "auth": "..." }
/// }
/// </summary>
public class SubscribeFunction
{
    private readonly PushSubscriptionStore? _store;
    private readonly ILogger<SubscribeFunction> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SubscribeFunction(ILogger<SubscribeFunction> logger, PushSubscriptionStore? store = null)
    {
        _logger = logger;
        _store = store;
    }

    [Function("subscribe")]
    public async Task<IActionResult> PostSubscribe(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "subscribe")] HttpRequest req)
    {
        if (_store is null)
        {
            _logger.LogWarning("SubscribeFunction: storage not configured — subscription dropped.");
            return new StatusCodeResult(503);
        }

        PushSubscriptionBody? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<PushSubscriptionBody>(req.Body, JsonOpts);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new { error = "Invalid subscription JSON" });
        }

        if (body is null
            || string.IsNullOrWhiteSpace(body.Endpoint)
            || body.Keys is null
            || string.IsNullOrWhiteSpace(body.Keys.P256dh)
            || string.IsNullOrWhiteSpace(body.Keys.Auth))
        {
            return new BadRequestObjectResult(new { error = "endpoint and keys (p256dh, auth) are required" });
        }

        var stored = new StoredPushSubscription(body.Endpoint, body.Keys.P256dh, body.Keys.Auth);
        await _store.SaveAsync(stored);

        _logger.LogInformation("SubscribeFunction: subscription saved for endpoint hash.");
        return new OkObjectResult(new { message = "Subscription saved" });
    }

    private record PushSubscriptionBody(string? Endpoint, PushKeys? Keys);
    private record PushKeys(string? P256dh, string? Auth);
}
