using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Daeanne.Shared.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Daeanne.Functions;

/// <summary>
/// Polls daeanne-srs@outlook.com via Microsoft Graph every minute for unread messages.
/// New messages → BridgeEmailMessage → daeanne-inbox Service Bus queue → Bridge → Dispatcher.
///
/// Auth: delegated OAuth with refresh token stored in GraphRefreshToken app setting.
/// Token refresh is handled automatically; updated refresh token is logged (manual update needed
/// if token expires — re-run device code flow to get a new one).
/// </summary>
public class MailPollFunction(
    IConfiguration config,
    IHttpClientFactory httpFactory,
    ServiceBusClient sbClient,
    ILogger<MailPollFunction> logger)
{
    private const string InboxQueue = "daeanne-inbox";
    private const string GraphBase = "https://graph.microsoft.com/v1.0";
    private const string TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";

    // DISABLED: Graph polling moved to Daeanne.Bridge / GraphMailWorker.cs.
    // Keeping code for reference. Remove [Function] attribute to prevent deployment.
    // [Function("MailPoll")]
    public async Task Run([TimerTrigger("0 */1 * * * *")] TimerInfo timer)
    {
        var clientId = config["GraphClientId"]
            ?? throw new InvalidOperationException("GraphClientId not configured");
        var refreshToken = config["GraphRefreshToken"]
            ?? throw new InvalidOperationException("GraphRefreshToken not configured");

        string accessToken;
        try
        {
            accessToken = await GetAccessTokenAsync(clientId, refreshToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MailPoll: failed to get access token — refresh token may need renewal");
            return;
        }

        var http = httpFactory.CreateClient("graph");
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        // Fetch unread messages, newest first, up to 25 per poll
        var url = $"{GraphBase}/me/mailFolders/inbox/messages" +
                  "?$filter=isRead eq false" +
                  "&$orderby=receivedDateTime asc" +
                  "&$top=25" +
                  "&$select=id,subject,from,toRecipients,body,receivedDateTime,internetMessageId";

        JsonElement messages;
        try
        {
            var resp = await http.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            messages = doc.RootElement.GetProperty("value").Clone();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MailPoll: Graph API call failed");
            return;
        }

        if (messages.GetArrayLength() == 0)
        {
            logger.LogDebug("MailPoll: no new messages");
            return;
        }

        logger.LogInformation("MailPoll: {Count} new message(s)", messages.GetArrayLength());

        await using var sender = sbClient.CreateSender(InboxQueue);
        var batch = await sender.CreateMessageBatchAsync();

        foreach (var msg in messages.EnumerateArray())
        {
            var graphId = msg.GetProperty("id").GetString()!;
            var subject = msg.TryGetProperty("subject", out var s) ? s.GetString() ?? "(no subject)" : "(no subject)";
            var from = msg.TryGetProperty("from", out var f)
                ? f.GetProperty("emailAddress").GetProperty("address").GetString() ?? ""
                : "";
            var bodyText = msg.TryGetProperty("body", out var b)
                ? b.GetProperty("content").GetString() ?? ""
                : "";
            var bodyHtml = msg.TryGetProperty("body", out var bh) &&
                           bh.GetProperty("contentType").GetString() == "html"
                ? bh.GetProperty("content").GetString()
                : null;
            var internetMsgId = msg.TryGetProperty("internetMessageId", out var mid)
                ? mid.GetString()
                : null;
            var received = msg.TryGetProperty("receivedDateTime", out var rd)
                ? DateTimeOffset.Parse(rd.GetString()!)
                : DateTimeOffset.UtcNow;
            var toAddress = config["GraphMailAddress"] ?? "daeanne-srs@outlook.com";

            var bridgeMsg = new BridgeEmailMessage
            {
                From = from,
                To = toAddress,
                Subject = subject,
                BodyText = StripHtml(bodyText),
                BodyHtml = bodyHtml,
                AcsMessageId = internetMsgId ?? graphId,
                Timestamp = received
            };

            var sbMessage = new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(bridgeMsg))
            {
                ContentType = "application/json",
                MessageId = graphId,
                Subject = subject
            };

            if (!batch.TryAddMessage(sbMessage))
            {
                await sender.SendMessagesAsync(batch);
                batch = await sender.CreateMessageBatchAsync();
                batch.TryAddMessage(sbMessage);
            }

            // Mark as read so we don't reprocess
            await MarkReadAsync(http, graphId);
        }

        if (batch.Count > 0)
            await sender.SendMessagesAsync(batch);

        logger.LogInformation("MailPoll: queued {Count} message(s) to {Queue}",
            messages.GetArrayLength(), InboxQueue);
    }

    private async Task<string> GetAccessTokenAsync(string clientId, string refreshToken)
    {
        var http = httpFactory.CreateClient();
        var resp = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"]    = "refresh_token",
            ["client_id"]     = clientId,
            ["refresh_token"] = refreshToken,
            ["scope"]         = "Mail.Read Mail.ReadWrite offline_access"
        }));

        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var err))
            throw new InvalidOperationException($"Token error: {err.GetString()}");

        // Log new refresh token if rotated (manual update required in app settings)
        if (root.TryGetProperty("refresh_token", out var newRt) &&
            newRt.GetString() != refreshToken)
        {
            logger.LogWarning("MailPoll: refresh token rotated — update GraphRefreshToken app setting: {Token}",
                newRt.GetString());
        }

        return root.GetProperty("access_token").GetString()!;
    }

    private async Task MarkReadAsync(System.Net.Http.HttpClient http, string messageId)
    {
        try
        {
            var patch = new StringContent("""{"isRead":true}""",
                System.Text.Encoding.UTF8, "application/json");
            await http.PatchAsync($"{GraphBase}/me/messages/{messageId}", patch);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MailPoll: could not mark message {Id} as read", messageId);
        }
    }

    private static string StripHtml(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return System.Text.RegularExpressions.Regex.Replace(input, "<[^>]+>", " ")
            .Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
            .Trim();
    }
}
