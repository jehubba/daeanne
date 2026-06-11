using System.Text.Json;
using Daeanne.Shared.Models;
using DaeanneFrontend.Api.Services;
using Microsoft.Azure.Functions.Worker;

namespace DaeanneFrontend.Api;

public class ResultReceiverFunction
{
    private readonly ResultStore _store;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ResultReceiverFunction(ResultStore store)
    {
        _store = store;
    }

    [Function("resultReceiver")]
    public async Task Run(
        [ServiceBusTrigger("daeanne-frontend-results", Connection = "ServiceBusConnection")] string messageBody)
    {
        var result = JsonSerializer.Deserialize<FrontendResult>(messageBody, JsonOpts);
        if (result is not null)
        {
            await _store.SaveAsync(result);
        }
    }
}
