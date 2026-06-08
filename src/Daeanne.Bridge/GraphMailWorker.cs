using System.Text;
using System.Text.Json;
using Daeanne.Shared.Models;

namespace Daeanne.Bridge;

/// <summary>
/// Polls daeanne-srs@outlook.com via Microsoft Graph every minute for unread messages.
/// New messages are posted directly to the local Dispatcher as Email tasks — no Service Bus hop needed.
///
/// Refresh token is loaded from Graph:RefreshToken config (user secrets), then persisted
/// to %APPDATA%\daeanne\graph-token.json so rotations survive restarts automatically.
/// </summary>
public class GraphMailWorker(
    ILogger<GraphMailWorker> logger,
    IConfiguration config,
    IHttpClientFactory http) : BackgroundService
{
    private const string GraphBase = "https://graph.microsoft.com/v1.0";
    private const string TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static readonly string TokenStatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "daeanne", "graph-token.json");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var clientId = config["Graph:ClientId"];
        var mailAddress = config["Graph:MailAddress"] ?? "daeanne-srs@outlook.com";
        var pollSeconds = int.TryParse(config["Graph:PollIntervalSeconds"], out var s) ? s : 60;
        var dispatcherUrl = config["Bridge:DispatcherUrl"] ?? "http://127.0.0.1:47777";

        if (string.IsNullOrWhiteSpace(clientId))
        {
            logger.LogWarning("GraphMailWorker: Graph:ClientId not configured — polling disabled.");
            return;
        }

        var tokenState = LoadTokenState();
        if (string.IsNullOrWhiteSpace(tokenState.RefreshToken))
            tokenState.RefreshToken = config["Graph:RefreshToken"];

        if (string.IsNullOrWhiteSpace(tokenState.RefreshToken))
        {
            logger.LogWarning("GraphMailWorker: No refresh token — run device code flow and set Graph:RefreshToken.");
            return;
        }

        logger.LogInformation("GraphMailWorker starting. Polling every {Interval}s for {Mail}",
            pollSeconds, mailAddress);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollInboxAsync(clientId, tokenState, mailAddress, dispatcherUrl, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "GraphMailWorker: unhandled error in poll cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
        }
    }

    private async Task PollInboxAsync(
        string clientId,
        TokenState tokenState,
        string mailAddress,
        string dispatcherUrl,
        CancellationToken ct)
    {
        string accessToken;
        try
        {
            accessToken = await RefreshAccessTokenAsync(clientId, tokenState, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GraphMailWorker: token refresh failed — may need re-authorization");
            return;
        }

        var graphHttp = http.CreateClient();
        graphHttp.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var url = $"{GraphBase}/me/mailFolders/inbox/messages" +
                  "?$filter=isRead eq false" +
                  "&$orderby=receivedDateTime asc" +
                  "&$top=25" +
                  "&$select=id,subject,from,body,receivedDateTime,internetMessageId";

        JsonElement messages;
        try
        {
            var resp = await graphHttp.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            messages = doc.RootElement.GetProperty("value").Clone();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GraphMailWorker: Graph API call failed");
            return;
        }

        if (messages.GetArrayLength() == 0) return;

        logger.LogInformation("GraphMailWorker: {Count} new message(s)", messages.GetArrayLength());

        var dispatchHttp = http.CreateClient("dispatcher");

        foreach (var msg in messages.EnumerateArray())
        {
            var graphId = msg.GetProperty("id").GetString()!;
            var subject = msg.TryGetProperty("subject", out var sv) ? sv.GetString() ?? "(no subject)" : "(no subject)";
            var from = msg.TryGetProperty("from", out var fv)
                ? fv.GetProperty("emailAddress").GetProperty("address").GetString() ?? ""
                : "";
            var bodyRaw = msg.TryGetProperty("body", out var bv) ? bv.GetProperty("content").GetString() ?? "" : "";
            var isHtml = msg.TryGetProperty("body", out var bv2) &&
                         bv2.GetProperty("contentType").GetString() == "html";
            var internetMsgId = msg.TryGetProperty("internetMessageId", out var mid) ? mid.GetString() : null;
            var received = msg.TryGetProperty("receivedDateTime", out var rd)
                ? DateTimeOffset.Parse(rd.GetString()!)
                : DateTimeOffset.UtcNow;

            var bridgeMsg = new BridgeEmailMessage
            {
                From = from,
                To = mailAddress,
                Subject = subject,
                BodyText = isHtml ? StripHtml(bodyRaw) : bodyRaw,
                BodyHtml = isHtml ? bodyRaw : null,
                AcsMessageId = internetMsgId ?? graphId,
                Timestamp = received
            };

            var prompt = BuildEmailPrompt(bridgeMsg);
            var taskBody = JsonSerializer.Serialize(new
            {
                type = "Email",
                prompt,
                correlationId = bridgeMsg.AcsMessageId
            });

            var taskResp = await dispatchHttp.PostAsync(
                $"{dispatcherUrl}/tasks",
                new StringContent(taskBody, Encoding.UTF8, "application/json"),
                ct);

            if (taskResp.IsSuccessStatusCode)
            {
                logger.LogInformation("GraphMailWorker: task created — from {From} re: {Subject}", from, subject);
                await MarkReadAsync(graphHttp, graphId, ct);
            }
            else
            {
                var err = await taskResp.Content.ReadAsStringAsync(ct);
                logger.LogError("GraphMailWorker: Dispatcher rejected task ({Code}): {Err}",
                    taskResp.StatusCode, err);
                // Don't mark read — will retry next poll
            }
        }
    }

    private async Task<string> RefreshAccessTokenAsync(string clientId, TokenState state, CancellationToken ct)
    {
        var tokenHttp = http.CreateClient();
        var resp = await tokenHttp.PostAsync(TokenEndpoint, new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"]    = "refresh_token",
                ["client_id"]     = clientId,
                ["refresh_token"] = state.RefreshToken!,
                ["scope"]         = "Mail.Read Mail.ReadWrite offline_access"
            }), ct);

        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var err))
            throw new InvalidOperationException($"Token error: {err.GetString()}");

        if (root.TryGetProperty("refresh_token", out var newRt))
        {
            var newToken = newRt.GetString()!;
            if (newToken != state.RefreshToken)
            {
                logger.LogInformation("GraphMailWorker: refresh token rotated — persisting to {Path}", TokenStatePath);
                state.RefreshToken = newToken;
                SaveTokenState(state);
            }
        }

        return root.GetProperty("access_token").GetString()!;
    }

    private async Task MarkReadAsync(HttpClient graphHttp, string messageId, CancellationToken ct)
    {
        try
        {
            await graphHttp.PatchAsync($"{GraphBase}/me/messages/{messageId}",
                new StringContent("""{"isRead":true}""", Encoding.UTF8, "application/json"), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GraphMailWorker: could not mark message {Id} as read", messageId);
        }
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

    private static string StripHtml(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return System.Text.RegularExpressions.Regex.Replace(input, "<[^>]+>", " ")
            .Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
            .Trim();
    }

    // ─── Token state persistence ──────────────────────────────────────────────

    private sealed class TokenState
    {
        public string? RefreshToken { get; set; }
    }

    private static TokenState LoadTokenState()
    {
        if (!File.Exists(TokenStatePath)) return new TokenState();
        try
        {
            return JsonSerializer.Deserialize<TokenState>(File.ReadAllText(TokenStatePath), JsonOpts)
                   ?? new TokenState();
        }
        catch { return new TokenState(); }
    }

    private static void SaveTokenState(TokenState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TokenStatePath)!);
            File.WriteAllText(TokenStatePath, JsonSerializer.Serialize(state, JsonOpts));
        }
        catch { /* non-fatal — token still works in memory until restart */ }
    }
}
