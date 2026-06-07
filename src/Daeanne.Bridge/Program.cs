using Daeanne.Bridge;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<BridgeWorker>();

var host = builder.Build();
host.Run();
