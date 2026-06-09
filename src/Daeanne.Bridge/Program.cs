using Daeanne.Bridge;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("dispatcher");
builder.Services.AddHostedService<BridgeWorker>();
builder.Services.AddHostedService<GraphMailWorker>();
builder.Services.AddHostedService<SmsSenderWorker>();

if (OperatingSystem.IsWindows())
    builder.Services.AddWindowsService();

var httpPort = builder.Configuration.GetValue<int>("Bridge:HttpPort", 47778);
builder.WebHost.UseUrls($"http://127.0.0.1:{httpPort}");

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    status    = "healthy",
    service   = "bridge",
    timestamp = DateTime.UtcNow
}));

app.Run();
