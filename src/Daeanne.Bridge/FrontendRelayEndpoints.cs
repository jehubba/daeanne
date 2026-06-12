using System.Text.Json;

namespace Daeanne.Bridge;

public static class FrontendRelayEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static void MapFrontendRelayEndpoints(this WebApplication app)
    {
        var dispatcherBase = app.Configuration.GetValue<string>("Bridge:DispatcherBaseUrl")
            ?? "http://127.0.0.1:47777";

        app.MapGet("/relay/tasks", async (HttpContext ctx, IHttpClientFactory clientFactory) =>
        {
            var client = clientFactory.CreateClient("dispatcher");
            var query = ctx.Request.QueryString.Value ?? "";
            try
            {
                var response = await client.GetAsync($"{dispatcherBase}/tasks{query}");
                if (!response.IsSuccessStatusCode)
                    return Results.StatusCode((int)response.StatusCode);

                var content = await response.Content.ReadAsStringAsync();
                var rawTasks = JsonSerializer.Deserialize<JsonElement[]>(content, JsonOpts) ?? [];

                var mapped = rawTasks.Select(MapTask).ToList();
                return Results.Json(new { tasks = mapped, total = mapped.Count }, JsonOpts);
            }
            catch (HttpRequestException)
            {
                return Results.StatusCode(502);
            }
        });

        app.MapGet("/relay/tasks/{id}", async (string id, IHttpClientFactory clientFactory) =>
        {
            var client = clientFactory.CreateClient("dispatcher");
            try
            {
                var response = await client.GetAsync($"{dispatcherBase}/tasks/{id}");
                if (!response.IsSuccessStatusCode)
                    return Results.StatusCode((int)response.StatusCode);

                var content = await response.Content.ReadAsStringAsync();
                var raw = JsonSerializer.Deserialize<JsonElement>(content, JsonOpts);
                return Results.Json(MapTask(raw), JsonOpts);
            }
            catch (HttpRequestException)
            {
                return Results.StatusCode(502);
            }
        });

        app.MapGet("/relay/trends/today", async (IHttpClientFactory clientFactory) =>
        {
            var client = clientFactory.CreateClient("dispatcher");
            try
            {
                var response = await client.GetAsync($"{dispatcherBase}/tasks?type=TrendAnalyzer&status=Succeeded&take=10");
                if (!response.IsSuccessStatusCode)
                    return Results.StatusCode(502);

                var content = await response.Content.ReadAsStringAsync();
                // Extract highlights from recent TrendAnalyzer tasks
                return Results.Content(content, "application/json");
            }
            catch (HttpRequestException)
            {
                return Results.StatusCode(502);
            }
        });
    }

    private static object MapTask(JsonElement t)
    {
        var id = t.GetProperty("id").GetString() ?? "";
        var type = t.TryGetProperty("type", out var tp) ? tp.GetString() ?? "" : "";
        var prompt = t.TryGetProperty("prompt", out var pr) ? pr.GetString() ?? "" : "";
        var status = t.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";

        // Dispatcher stores UTC timestamps without the Z suffix
        DateTime createdAt = DateTime.UtcNow;
        if (t.TryGetProperty("createdAt", out var ca))
        {
            if (ca.TryGetDateTimeOffset(out var cao))
                createdAt = DateTime.SpecifyKind(cao.DateTime, DateTimeKind.Utc);
            else if (ca.ValueKind == JsonValueKind.String && DateTime.TryParse(ca.GetString(), out var parsed))
                createdAt = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        DateTime? completedAt = null;
        if (t.TryGetProperty("completedAt", out var co) && co.ValueKind != JsonValueKind.Null)
        {
            if (co.TryGetDateTimeOffset(out var coo))
                completedAt = DateTime.SpecifyKind(coo.DateTime, DateTimeKind.Utc);
            else if (co.ValueKind == JsonValueKind.String && DateTime.TryParse(co.GetString(), out var parsed))
                completedAt = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        var resultJson = t.TryGetProperty("resultJson", out var rj) && rj.ValueKind == JsonValueKind.String ? rj.GetString() : null;
        var error = t.TryGetProperty("error", out var er) && er.ValueKind == JsonValueKind.String ? er.GetString() : null;
        var correlationId = t.TryGetProperty("correlationId", out var ci) && ci.ValueKind == JsonValueKind.String ? ci.GetString() : null;

        // Derive Topic: first line of prompt, truncated
        var topic = prompt.Length > 80 ? prompt[..80] + "…" : prompt;
        var newlineIdx = topic.IndexOf('\n');
        if (newlineIdx > 0) topic = topic[..newlineIdx];

        // Derive Age
        var elapsed = DateTime.UtcNow - createdAt;
        var age = elapsed.TotalSeconds < 0 ? "just now"
            : elapsed.TotalDays >= 1 ? $"{(int)elapsed.TotalDays}d ago"
            : elapsed.TotalHours >= 1 ? $"{(int)elapsed.TotalHours}h ago"
            : $"{Math.Max(0, (int)elapsed.TotalMinutes)}m ago";

        return new
        {
            id,
            type,
            topic,
            status,
            age,
            createdAt,
            completedAt,
            resultJson,
            error,
            correlationId
        };
    }
}
