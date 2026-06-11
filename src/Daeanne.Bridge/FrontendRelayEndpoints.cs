namespace Daeanne.Bridge;

public static class FrontendRelayEndpoints
{
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
                var content = await response.Content.ReadAsStringAsync();
                return Results.Content(content, "application/json", statusCode: (int)response.StatusCode);
            }
            catch (HttpRequestException)
            {
                return Results.StatusCode(502);
            }
        });

        app.MapGet("/relay/tasks/{id:int}", async (int id, IHttpClientFactory clientFactory) =>
        {
            var client = clientFactory.CreateClient("dispatcher");
            try
            {
                var response = await client.GetAsync($"{dispatcherBase}/tasks/{id}");
                var content = await response.Content.ReadAsStringAsync();
                return Results.Content(content, "application/json", statusCode: (int)response.StatusCode);
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
}
