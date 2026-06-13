using Azure.Messaging.ServiceBus;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Storage.Blobs;
using DaeanneFrontend.Api.Middleware;
using DaeanneFrontend.Api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.UseMiddleware<IdentityGuard>();

var bridgeBaseUrl = Environment.GetEnvironmentVariable("BRIDGE_BASE_URL") ?? "http://127.0.0.1:47778";
builder.Services.AddHttpClient<DaeanneFrontend.Api.TasksFunction>(client =>
{
    client.BaseAddress = new Uri(bridgeBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient<DaeanneFrontend.Api.TrendFunction>(client =>
{
    client.BaseAddress = new Uri(bridgeBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});

var sbConnectionString = Environment.GetEnvironmentVariable("ServiceBusConnection");
if (!string.IsNullOrEmpty(sbConnectionString))
{
    builder.Services.AddSingleton(new ServiceBusClient(sbConnectionString));
}

var storageConnection = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
    ?? Environment.GetEnvironmentVariable("FRONTEND_STORAGE_CONNECTION");
if (!string.IsNullOrEmpty(storageConnection))
{
    builder.Services.AddSingleton(new BlobServiceClient(storageConnection));
    builder.Services.AddSingleton<ResultStore>();
}
else
{
    builder.Services.AddSingleton<ResultStore>(_ => ResultStore.Unconfigured);
}

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter();
}

builder.Build().Run();
