using System.Text;
using System.Text.Json;
using Daeanne.Shared.Models;

namespace Daeanne.Bridge;

/// <summary>
/// Handles all Microsoft Graph mail operations for daeanne-srs@outlook.com:
///
/// Inbound: polls inbox every 60s for unread messages → posts to Dispatcher as Email tasks.
/// Outbound: polls Dispatcher outbox every 10s → sends via Graph sendMail API.
///
/// Using Graph for both directions keeps everything in one account, replies appear in-thread,
/// and eliminates the ACS / Service Bus dependency for email.
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

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static readonly string TokenStatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "daeanne", "graph-token.json");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var clientId = config["Graph:ClientId"];
        var mailAddress = config["Graph:MailAddress"] ?? "daeanne-srs@outlook.com";
        var inboundPollSeconds  = int.TryParse(config["Graph:PollIntervalSeconds"], out var s) ? s : 60;
        var outboundPollSeconds = int.TryParse(config["Graph:OutboundPollIntervalSeconds"], out var o) ? o : 10;
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

        logger.LogInformation("GraphMailWorker starting. Inbound every {In}s, outbound every {Out}s for {Mail}",
            inboundPollSeconds, outboundPollSeconds, mailAddress);

        var blockedSenders = new BlockedSendersStore(reloadEveryNPolls: 10);

        await Task.WhenAll(
            RunInboundLoopAsync(clientId, tokenState, mailAddress, dispatcherUrl, inboundPollSeconds, blockedSenders, stoppingToken),
            RunOutboundLoopAsync(clientId, tokenState, dispatcherUrl, outboundPollSeconds, stoppingToken));
    }

    private async Task RunInboundLoopAsync(
        string clientId, TokenState tokenState, string mailAddress,
        string dispatcherUrl, int pollSeconds, BlockedSendersStore blockedSenders, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try   { await PollInboxAsync(clientId, tokenState, mailAddress, dispatcherUrl, blockedSenders, ct); }
            catch (HttpRequestException ex) when (IsConnectionRefused(ex))
            { logger.LogWarning("GraphMailWorker: Dispatcher unreachable (inbound) — will retry in {S}s", pollSeconds); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogError(ex, "GraphMailWorker: unhandled error in inbound poll cycle"); }

            await Task.Delay(TimeSpan.FromSeconds(pollSeconds), ct);
        }
    }

    private async Task RunOutboundLoopAsync(
        string clientId, TokenState tokenState,
        string dispatcherUrl, int pollSeconds, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try   { await SendPendingEmailsAsync(clientId, tokenState, dispatcherUrl, ct); }
            catch (HttpRequestException ex) when (IsConnectionRefused(ex))
            { logger.LogWarning("GraphMailWorker: Dispatcher unreachable (outbound) — will retry in {S}s", pollSeconds); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogError(ex, "GraphMailWorker: unhandled error in outbound send cycle"); }

            await Task.Delay(TimeSpan.FromSeconds(pollSeconds), ct);
        }
    }

    private static bool IsConnectionRefused(HttpRequestException ex) =>
        ex.InnerException is System.Net.Sockets.SocketException se &&
        se.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionRefused;

    // ─── OUTBOUND: Dispatcher outbox → Graph sendMail ─────────────────────────

    private async Task SendPendingEmailsAsync(
        string clientId, TokenState tokenState, string dispatcherUrl, CancellationToken ct)
    {
        var dispatchHttp = http.CreateClient("dispatcher");

        // Fetch Pending + stale Processing (retry after 2 min)
        var pendingJson = await dispatchHttp.GetStringAsync(
            $"{dispatcherUrl}/outbox/email?status=Pending&take=20", ct);
        var processingJson = await dispatchHttp.GetStringAsync(
            $"{dispatcherUrl}/outbox/email?status=Processing&take=20", ct);

        var pending    = JsonSerializer.Deserialize<OutboxEmail[]>(pendingJson,    JsonOpts) ?? [];
        var processing = JsonSerializer.Deserialize<OutboxEmail[]>(processingJson, JsonOpts) ?? [];
        var stale      = processing.Where(e => DateTime.UtcNow - e.CreatedAt > TimeSpan.FromMinutes(2));
        var toSend     = pending.Concat(stale).ToArray();

        if (toSend.Length == 0) return;

        string accessToken;
        try { accessToken = await RefreshAccessTokenAsync(clientId, tokenState, ct); }
        catch (Exception ex)
        {
            logger.LogError(ex, "GraphMailWorker: token refresh failed for outbound send");
            return;
        }

        var graphHttp = http.CreateClient();
        graphHttp.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        foreach (var email in toSend)
        {
            // Claim it
            var claim = await dispatchHttp.PatchAsync(
                $"{dispatcherUrl}/outbox/email/{email.Id}/status",
                new StringContent("""{"status":"Processing"}""", Encoding.UTF8, "application/json"), ct);
            if (!claim.IsSuccessStatusCode) continue;

            try
            {
                var payload = JsonSerializer.Serialize(new
                {
                    message = new
                    {
                        subject = email.Subject,
                        body    = new { contentType = "Text", content = email.Body ?? "" },
                        toRecipients = new[]
                        {
                            new { emailAddress = new { address = email.To } }
                        }
                    },
                    saveToSentItems = true
                });

                var sendResp = await graphHttp.PostAsync(
                    $"{GraphBase}/me/sendMail",
                    new StringContent(payload, Encoding.UTF8, "application/json"), ct);

                if (sendResp.IsSuccessStatusCode)
                {
                    await dispatchHttp.PatchAsync(
                        $"{dispatcherUrl}/outbox/email/{email.Id}/status",
                        new StringContent("""{"status":"Sent"}""", Encoding.UTF8, "application/json"), ct);
                    logger.LogInformation("GraphMailWorker: sent email to {To} re: {Subject}",
                        email.To, email.Subject);
                }
                else
                {
                    var err = await sendResp.Content.ReadAsStringAsync(ct);
                    logger.LogError("GraphMailWorker: Graph sendMail failed ({Code}): {Err}",
                        sendResp.StatusCode, err);
                    await dispatchHttp.PatchAsync(
                        $"{dispatcherUrl}/outbox/email/{email.Id}/status",
                        new StringContent(
                            JsonSerializer.Serialize(new { status = "Failed", error = err }),
                            Encoding.UTF8, "application/json"), ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "GraphMailWorker: exception sending email {Id}", email.Id);
                await dispatchHttp.PatchAsync(
                    $"{dispatcherUrl}/outbox/email/{email.Id}/status",
                    new StringContent(
                        JsonSerializer.Serialize(new { status = "Failed", error = ex.Message }),
                        Encoding.UTF8, "application/json"), ct);
            }
        }
    }

    // ─── INBOUND: Graph inbox → Dispatcher ───────────────────────────────────

    private async Task PollInboxAsync(
        string clientId,
        TokenState tokenState,
        string mailAddress,
        string dispatcherUrl,
        BlockedSendersStore blockedSenders,
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

            // Tier 1: static config denylist (developer-set domain/address patterns)
            if (ShouldIgnoreByConfig(from, config))
            {
                logger.LogInformation("GraphMailWorker: filtered (config) email from {From} re: {Subject}", from, subject);
                blockedSenders.LogFiltered(from, "config-pattern");
                await MarkReadAsync(graphHttp, graphId, ct);
                continue;
            }

            // Tier 2: dynamic blocked-senders.json (Daeanne-managed + auto-detected)
            var (blocked, reason) = blockedSenders.Check(from);
            if (blocked)
            {
                logger.LogInformation("GraphMailWorker: filtered (blocked-senders) email from {From} — {Reason}", from, reason);
                blockedSenders.LogFiltered(from, reason);
                await MarkReadAsync(graphHttp, graphId, ct);
                continue;
            }

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

            if (taskResp.IsSuccessStatusCode || taskResp.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // 201 = created, 409 = duplicate (correlationId already has a non-terminal task)
                var action = taskResp.IsSuccessStatusCode ? "task created" : "task already exists";
                logger.LogInformation("GraphMailWorker: {Action} — from {From} re: {Subject}", action, from, subject);
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
                ["scope"]         = "Mail.Read Mail.ReadWrite Mail.Send offline_access"
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

    private static bool ShouldIgnoreByConfig(string from, IConfiguration config)
    {
        var ignored = config["Graph:IgnoredSenders"]
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];

        foreach (var pattern in ignored)
        {
            if (from.EndsWith(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
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
