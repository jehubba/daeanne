using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((ctx, services) =>
    {
        // ServiceBusClient shared by EmailSend + MailPoll
        var sbConnStr = ctx.Configuration["ServiceBusConnection"];
        if (!string.IsNullOrEmpty(sbConnStr))
            services.AddSingleton(new ServiceBusClient(sbConnStr));

        // Named HttpClient for Microsoft Graph calls
        services.AddHttpClient("graph", c =>
        {
            c.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
            c.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        // Default HttpClient for token refresh calls
        services.AddHttpClient();
    })
    .Build();

await host.RunAsync();

await host.RunAsync();
