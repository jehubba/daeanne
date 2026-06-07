using Daeanne.Bridge;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient("dispatcher");
builder.Services.AddHostedService<BridgeWorker>();

if (OperatingSystem.IsWindows())
    builder.Services.AddWindowsService();

var host = builder.Build();
host.Run();
