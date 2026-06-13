using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Daeanne.Bridge;

/// <summary>
/// HTTP endpoints that proxy Microsoft Graph calendar operations.
/// The scheduler agent calls these via Invoke-RestMethod — no MCP server required.
///
/// All endpoints return Graph's response directly (or a thin envelope on error).
/// They require a valid Graph access token in GraphTokenCache; if the token is
/// absent (Bridge just started before the first mail poll cycle), they return 503.
///
/// Base path: /calendar
///   GET  /calendar/events              — list events (start/end query params, ISO 8601 UTC)
///   POST /calendar/events              — create event
///   GET  /calendar/events/{id}         — get single event
///   PATCH /calendar/events/{id}        — update event
///   DELETE /calendar/events/{id}       — cancel/delete event
///   GET  /calendar/freebusy            — get free/busy schedule
/// </summary>
public static class CalendarEndpoints
{
    private const string GraphBase = "https://graph.microsoft.com/v1.0";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static void MapCalendarEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/calendar");

        // ── List events ────────────────────────────────────────────────────────
        group.MapGet("/events", async (
            HttpContext ctx,
            IHttpClientFactory clientFactory,
            string? start,
            string? end,
            int take = 20) =>
        {
            var token = GraphTokenCache.Get();
            if (token is null)
                return Results.Json(new { error = "Graph token not yet available — Bridge may be initialising." }, statusCode: 503);

            var g = MakeGraphClient(clientFactory, token);

            // Build $filter for date range if provided
            var filter = BuildDateFilter(start, end);
            var url = $"{GraphBase}/me/events?$top={take}&$orderby=start/dateTime asc&$select=id,subject,start,end,location,attendees,bodyPreview,isCancelled,organizer";
            if (!string.IsNullOrWhiteSpace(filter))
                url += $"&$filter={Uri.EscapeDataString(filter)}";

            var resp = await g.GetAsync(url);
            var body = await resp.Content.ReadAsStringAsync();
            return Results.Content(body, "application/json", statusCode: (int)resp.StatusCode);
        });

        // ── Get single event ───────────────────────────────────────────────────
        group.MapGet("/events/{id}", async (string id, IHttpClientFactory clientFactory) =>
        {
            var token = GraphTokenCache.Get();
            if (token is null) return Results.Json(new { error = "Graph token not available." }, statusCode: 503);

            var g = MakeGraphClient(clientFactory, token);
            var resp = await g.GetAsync(
                $"{GraphBase}/me/events/{Uri.EscapeDataString(id)}?$select=id,subject,start,end,location,attendees,body,isCancelled,organizer");
            var body = await resp.Content.ReadAsStringAsync();
            return Results.Content(body, "application/json", statusCode: (int)resp.StatusCode);
        });

        // ── Create event ───────────────────────────────────────────────────────
        group.MapPost("/events", async (HttpContext ctx, IHttpClientFactory clientFactory) =>
        {
            var token = GraphTokenCache.Get();
            if (token is null) return Results.Json(new { error = "Graph token not available." }, statusCode: 503);

            var bodyText = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(bodyText))
                return Results.BadRequest(new { error = "Request body required." });

            var g    = MakeGraphClient(clientFactory, token);
            var resp = await g.PostAsync(
                $"{GraphBase}/me/events",
                new StringContent(bodyText, Encoding.UTF8, "application/json"));
            var body = await resp.Content.ReadAsStringAsync();
            return Results.Content(body, "application/json", statusCode: (int)resp.StatusCode);
        });

        // ── Update event ───────────────────────────────────────────────────────
        group.MapMethods("/events/{id}", ["PATCH"], async (string id, HttpContext ctx, IHttpClientFactory clientFactory) =>
        {
            var token = GraphTokenCache.Get();
            if (token is null) return Results.Json(new { error = "Graph token not available." }, statusCode: 503);

            var bodyText = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var g    = MakeGraphClient(clientFactory, token);
            var req  = new HttpRequestMessage(HttpMethod.Patch, $"{GraphBase}/me/events/{Uri.EscapeDataString(id)}")
            {
                Content = new StringContent(bodyText, Encoding.UTF8, "application/json")
            };
            var resp = await g.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            return Results.Content(body, "application/json", statusCode: (int)resp.StatusCode);
        });

        // ── Delete / cancel event ──────────────────────────────────────────────
        group.MapDelete("/events/{id}", async (string id, IHttpClientFactory clientFactory) =>
        {
            var token = GraphTokenCache.Get();
            if (token is null) return Results.Json(new { error = "Graph token not available." }, statusCode: 503);

            var g    = MakeGraphClient(clientFactory, token);
            var resp = await g.DeleteAsync($"{GraphBase}/me/events/{Uri.EscapeDataString(id)}");
            return resp.IsSuccessStatusCode
                ? Results.NoContent()
                : Results.Json(new { error = await resp.Content.ReadAsStringAsync() }, statusCode: (int)resp.StatusCode);
        });

        // ── Free/busy ──────────────────────────────────────────────────────────
        group.MapGet("/freebusy", async (
            IHttpClientFactory clientFactory,
            IConfiguration config,
            string? start,
            string? end) =>
        {
            var token = GraphTokenCache.Get();
            if (token is null) return Results.Json(new { error = "Graph token not available." }, statusCode: 503);

            var mailAddress = config["Graph:MailAddress"] ?? "daeanne-srs@outlook.com";
            var startDt = start ?? DateTime.UtcNow.ToString("o");
            var endDt   = end   ?? DateTime.UtcNow.AddDays(1).ToString("o");

            var payload = JsonSerializer.Serialize(new
            {
                schedules = new[] { mailAddress },
                startTime = new { dateTime = startDt, timeZone = "UTC" },
                endTime   = new { dateTime = endDt,   timeZone = "UTC" },
                availabilityViewInterval = 30
            });

            var g    = MakeGraphClient(clientFactory, token);
            var resp = await g.PostAsync(
                $"{GraphBase}/me/calendar/getSchedule",
                new StringContent(payload, Encoding.UTF8, "application/json"));
            var body = await resp.Content.ReadAsStringAsync();
            return Results.Content(body, "application/json", statusCode: (int)resp.StatusCode);
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static HttpClient MakeGraphClient(IHttpClientFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string? BuildDateFilter(string? start, string? end)
    {
        if (string.IsNullOrWhiteSpace(start) && string.IsNullOrWhiteSpace(end))
            return null;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(start))
            parts.Add($"start/dateTime ge '{start}'");
        if (!string.IsNullOrWhiteSpace(end))
            parts.Add($"end/dateTime le '{end}'");
        return string.Join(" and ", parts);
    }
}
