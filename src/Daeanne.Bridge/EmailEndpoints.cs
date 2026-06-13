using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Daeanne.Bridge;

/// <summary>
/// HTTP endpoints that proxy Microsoft Graph email search.
/// Daeanne calls these via Invoke-RestMethod to retrieve prior email context.
///
/// Base path: /email
///   GET  /email/search?q=...           — search mailbox (OData $search, KQL syntax)
///                      &maxResults=10  — default 10, max 50
///                      &semantic=false — if true, attempts /search/query with AI ranking
///                      &folder=inbox   — inbox (default) or all
///
/// KQL examples:
///   q=budget meeting          — implicit AND
///   q="Q3 budget"             — exact phrase
///   q=budget OR forecast      — OR
///   q=subject:agenda          — field-scoped
///   q=from:jeffrey            — sender filter
///   q=received>=2026-01-01    — date range
///   q=hasattachment:true      — has attachment
///
/// Returns: array of { id, subject, from, receivedDateTime, bodyPreview, webLink }
/// </summary>
public static class EmailEndpoints
{
    private const string GraphBase = "https://graph.microsoft.com/v1.0";
    private static readonly string[] DefaultFields = ["id", "subject", "from", "receivedDateTime", "bodyPreview", "webLink"];

    public static void MapEmailEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/email");

        // ── Search ─────────────────────────────────────────────────────────────
        group.MapGet("/search", async (
            HttpContext ctx,
            IHttpClientFactory clientFactory,
            string? q,
            int maxResults = 10,
            bool semantic = false,
            string folder = "inbox") =>
        {
            var token = GraphTokenCache.Get();
            if (token is null)
                return Results.Json(new { error = "Graph token not yet available — Bridge may be initialising." }, statusCode: 503);

            if (string.IsNullOrWhiteSpace(q))
                return Results.BadRequest(new { error = "Query parameter 'q' is required." });

            maxResults = Math.Clamp(maxResults, 1, 50);
            var g = MakeGraphClient(clientFactory, token);

            if (semantic)
            {
                var semanticResult = await TrySemanticSearchAsync(g, q, maxResults, ctx.RequestAborted);
                if (semanticResult is not null)
                    return semanticResult;
                // Fall through to OData $search on any failure (403 = no license, etc.)
            }

            return await ODataSearchAsync(g, q, maxResults, folder, ctx.RequestAborted);
        });
    }

    // ── Option A: OData $search (no extra license needed) ─────────────────────

    private static async Task<IResult> ODataSearchAsync(
        HttpClient g, string q, int maxResults, string folder, CancellationToken ct)
    {
        var folderPath = folder.Equals("inbox", StringComparison.OrdinalIgnoreCase)
            ? "mailFolders/inbox/messages"
            : "messages";

        var select = string.Join(",", DefaultFields);
        var url = $"{GraphBase}/me/{folderPath}" +
                  $"?$search=\"{Uri.EscapeDataString(q)}\"" +
                  $"&$top={maxResults}" +
                  $"&$select={select}";

        var resp = await g.GetAsync(url, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            return Results.Json(new { error = "Graph search failed.", detail = body }, statusCode: (int)resp.StatusCode);

        return ProjectMessages(body);
    }

    // ── Option B: /search/query with semantic ranking ─────────────────────────
    // Requires M365 E3/E5 or Copilot license. Falls back to OData on 403.

    private static async Task<IResult?> TrySemanticSearchAsync(
        HttpClient g, string q, int maxResults, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            requests = new[]
            {
                new
                {
                    entityTypes    = new[] { "message" },
                    query          = new { queryString = q },
                    from           = 0,
                    size           = maxResults,
                    enableTopResults = true
                }
            }
        });

        try
        {
            var resp = await g.PostAsync(
                $"{GraphBase}/search/query",
                new StringContent(payload, Encoding.UTF8, "application/json"),
                ct);

            if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return null; // No license — caller falls through to OData

            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return null; // Any other error — fall through silently

            // Extract hits from the /search/query response shape
            using var doc = JsonDocument.Parse(body);
            var hits = doc.RootElement
                .GetProperty("value")[0]
                .GetProperty("hitsContainers")[0]
                .GetProperty("hits");

            var results = hits.EnumerateArray().Select(hit =>
            {
                var resource = hit.GetProperty("resource");
                return new
                {
                    id              = TryGet(resource, "id"),
                    subject         = TryGet(resource, "subject"),
                    from            = TryGetSender(resource),
                    receivedDateTime = TryGet(resource, "receivedDateTime"),
                    bodyPreview     = TryGet(resource, "bodyPreview"),
                    webLink         = TryGet(resource, "webLink"),
                    relevanceScore  = hit.TryGetProperty("_score", out var score) ? score.GetDouble() : (double?)null
                };
            }).ToArray();

            return Results.Ok(new { source = "semantic", count = results.Length, results });
        }
        catch
        {
            return null; // Any parsing/network failure — fall through to OData
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static IResult ProjectMessages(string graphBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(graphBody);
            var messages = doc.RootElement.GetProperty("value").EnumerateArray().Select(m => new
            {
                id               = TryGet(m, "id"),
                subject          = TryGet(m, "subject"),
                from             = TryGetSender(m),
                receivedDateTime = TryGet(m, "receivedDateTime"),
                bodyPreview      = TryGet(m, "bodyPreview"),
                webLink          = TryGet(m, "webLink")
            }).ToArray();

            return Results.Ok(new { source = "odata", count = messages.Length, results = messages });
        }
        catch
        {
            return Results.Content(graphBody, "application/json");
        }
    }

    private static string? TryGet(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() : null;

    private static string? TryGetSender(JsonElement el)
    {
        if (!el.TryGetProperty("from", out var from)) return null;
        if (!from.TryGetProperty("emailAddress", out var ea)) return null;
        var name    = ea.TryGetProperty("name", out var n)    ? n.GetString()    : null;
        var address = ea.TryGetProperty("address", out var a) ? a.GetString()    : null;
        return name is not null && address is not null ? $"{name} <{address}>" : address ?? name;
    }

    private static HttpClient MakeGraphClient(IHttpClientFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
