using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((ctx, services) =>
    {
        // ServiceBusClient for EmailIngest publishing to daeanne-inbox
        var sbConnStr = ctx.Configuration["ServiceBusConnection"];
        if (!string.IsNullOrEmpty(sbConnStr))
            services.AddSingleton(new ServiceBusClient(sbConnStr));
    })
    .Build();

await host.RunAsync();
