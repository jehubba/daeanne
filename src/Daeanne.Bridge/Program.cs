using Daeanne.Bridge;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient("dispatcher");
builder.Services.AddHostedService<BridgeWorker>();
builder.Services.AddHostedService<GraphMailWorker>();

if (OperatingSystem.IsWindows())
    builder.Services.AddWindowsService();

var host = builder.Build();
host.Run();
