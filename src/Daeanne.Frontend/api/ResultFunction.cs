using DaeanneFrontend.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace DaeanneFrontend.Api;

public class ResultFunction
{
    private readonly ResultStore _store;

    public ResultFunction(ResultStore store)
    {
        _store = store;
    }

    [Function("result")]
    public async Task<IActionResult> GetResult(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "result/{correlationId}")] HttpRequest req,
        string correlationId)
    {
        var result = await _store.GetAsync(correlationId);
        if (result is not null)
        {
            return new OkObjectResult(new
            {
                correlationId = result.CorrelationId,
                status = result.Succeeded ? "completed" : "failed",
                succeeded = (bool?)result.Succeeded,
                response = result.Response,
                error = result.Error
            });
        }

        return new OkObjectResult(new
        {
            correlationId,
            status = "pending",
            succeeded = (bool?)null,
            response = (string?)null,
            error = (string?)null
        });
    }
}
