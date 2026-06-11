using Daeanne.Bridge;

var builder = WebApplication.CreateBuilder(args);

// Configure the named "dispatcher" HttpClient — adds the shared API key when configured.
var dispatcherApiKey = builder.Configuration["Bridge:DispatcherApiKey"] ?? "";
builder.Services.AddHttpClient("dispatcher", client =>
{
    if (!string.IsNullOrWhiteSpace(dispatcherApiKey))
        client.DefaultRequestHeaders.Add("X-Daeanne-Key", dispatcherApiKey);
});
builder.Services.AddHostedService<BridgeWorker>();
builder.Services.AddHostedService<GraphMailWorker>();
builder.Services.AddHostedService<SmsSenderWorker>();
builder.Services.AddHostedService<FrontendRelayWorker>();

if (OperatingSystem.IsWindows())
    builder.Services.AddWindowsService();

var httpPort = builder.Configuration.GetValue<int>("Bridge:HttpPort", 47778);
builder.WebHost.UseUrls($"http://127.0.0.1:{httpPort}");

var app = builder.Build();

app.MapGet("/health", () =>
{
    if (!BridgeHealth.GraphTokenOk)
        return Results.Json(new
        {
            status = "degraded",
            service = "bridge",
            graphToken = "invalid",
            error = BridgeHealth.GraphTokenError,
            timestamp = DateTime.UtcNow
        }, statusCode: 503);

    return Results.Ok(new
    {
        status = "healthy",
        service = "bridge",
        graphToken = "ok",
        timestamp = DateTime.UtcNow
    });
});

app.MapFrontendRelayEndpoints();

app.Run();
