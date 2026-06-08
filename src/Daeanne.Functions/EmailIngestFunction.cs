using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Daeanne.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Daeanne.Functions;

/// <summary>
/// Receives inbound email events and enqueues them to the daeanne-inbox Service Bus queue
/// for Bridge pickup.
///
/// STATUS: Stub — ACS inbound email (EmailReceived event) is private preview and not
/// accessible. Future inbound options:
///   - Microsoft Graph webhook on Exchange Online inbox (preferred — M365)
///   - IMAP polling (any standard mailbox)
/// The outbound path (EmailSendFunction + Bridge) is fully operational.
/// </summary>
public class EmailIngestFunction(ServiceBusClient sbClient, ILogger<EmailIngestFunction> logger)
{
    private const string InboxQueue = "daeanne-inbox";

    [Function("EmailIngest")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "email/ingest")] HttpRequest req)
    {
        // ACS EventGrid sends a validation handshake on first subscription
        if (req.Headers.TryGetValue("aeg-event-type", out var eventType) &&
            eventType == "SubscriptionValidation")
        {
            return await HandleValidationAsync(req);
        }

        string body;
        using (var reader = new StreamReader(req.Body))
            body = await reader.ReadToEndAsync();

        logger.LogDebug("EmailIngest received payload: {Body}", body);

        BridgeEmailMessage? message;
        try
        {
            message = ParseAcsEmailEvent(body);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse ACS email event");
            return new BadRequestObjectResult("Invalid event payload");
        }

        if (message is null)
        {
            logger.LogWarning("EmailIngest: unrecognized event type, ignoring");
            return new OkResult();
        }

        await using var sender = sbClient.CreateSender(InboxQueue);
        var sbMessage = new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(message))
        {
            ContentType = "application/json",
            MessageId = message.AcsMessageId ?? Guid.NewGuid().ToString()
        };
        await sender.SendMessageAsync(sbMessage);

        logger.LogInformation("EmailIngest: queued message {MessageId} from {From}", sbMessage.MessageId, message.From);
        return new OkResult();
    }

    private static async Task<IActionResult> HandleValidationAsync(HttpRequest req)
    {
        string body;
        using (var reader = new StreamReader(req.Body))
            body = await reader.ReadToEndAsync();

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        // EventGrid sends an array for validation
        var evt = root.ValueKind == JsonValueKind.Array ? root[0] : root;
        var code = evt.GetProperty("data").GetProperty("validationCode").GetString();
        return new OkObjectResult(new { validationResponse = code });
    }

    /// <summary>
    /// Parses an ACS inbound-email EventGrid event into a BridgeEmailMessage.
    /// ACS event type: "Microsoft.Communication.IncomingCall" is voice;
    /// for email it's "Microsoft.Communication.EmailReceived".
    /// </summary>
    private static BridgeEmailMessage? ParseAcsEmailEvent(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var evt = root.ValueKind == JsonValueKind.Array ? root[0] : root;

        if (!evt.TryGetProperty("eventType", out var evtType))
            return null;

        if (evtType.GetString() != "Microsoft.Communication.EmailReceived")
            return null;

        var data = evt.GetProperty("data");
        var sender = data.TryGetProperty("sender", out var s) ? s.GetString() ?? "" : "";
        var recipient = data.TryGetProperty("recipient", out var r) ? r.GetString() ?? "" : "";
        var subject = data.TryGetProperty("subject", out var sub) ? sub.GetString() ?? "" : "";
        var msgId = data.TryGetProperty("messageId", out var mid) ? mid.GetString() : null;

        string bodyText = "";
        string? bodyHtml = null;
        if (data.TryGetProperty("body", out var bodyProp))
        {
            bodyText = bodyProp.TryGetProperty("plainText", out var pt) ? pt.GetString() ?? "" : "";
            bodyHtml = bodyProp.TryGetProperty("html", out var ht) ? ht.GetString() : null;
        }

        return new BridgeEmailMessage
        {
            From = sender,
            To = recipient,
            Subject = subject,
            BodyText = bodyText,
            BodyHtml = bodyHtml,
            AcsMessageId = msgId,
            Timestamp = DateTimeOffset.UtcNow
        };
    }
}
