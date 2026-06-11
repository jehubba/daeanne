using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace DaeanneFrontend.Api;

public class ResultFunction
{
    [Function("result")]
    public IActionResult GetResult(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "result/{correlationId}")] HttpRequest req,
        string correlationId)
    {
        if (ResultReceiverFunction.Results.TryGetValue(correlationId, out var result))
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
