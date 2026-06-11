using System.Collections.Concurrent;
using System.Text.Json;
using Daeanne.Shared.Models;
using Microsoft.Azure.Functions.Worker;

namespace DaeanneFrontend.Api;

public class ResultReceiverFunction
{
    internal static readonly ConcurrentDictionary<string, FrontendResult> Results = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Function("resultReceiver")]
    public void Run(
        [ServiceBusTrigger("daeanne-frontend-results", Connection = "ServiceBusConnection")] string messageBody)
    {
        var result = JsonSerializer.Deserialize<FrontendResult>(messageBody, JsonOpts);
        if (result is not null)
        {
            Results[result.CorrelationId] = result;
        }
    }
}
