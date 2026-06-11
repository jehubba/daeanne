using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace DaeanneFrontend.Api.Middleware;

public class IdentityGuard : IFunctionsWorkerMiddleware
{
    private static readonly HashSet<string> AnonymousFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "health",
        "get-roles"
    };

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var functionName = context.FunctionDefinition.Name;

        if (AnonymousFunctions.Contains(functionName))
        {
            await next(context);
            return;
        }

        var requestData = await context.GetHttpRequestDataAsync();
        if (requestData is null)
        {
            // Non-HTTP trigger (e.g., Service Bus) — allow through
            await next(context);
            return;
        }

        // SWA already enforces role-based auth via staticwebapp.config.json (admin role required).
        // We only verify the principal header exists — confirming the request came through SWA.
        if (!requestData.Headers.TryGetValues("x-ms-client-principal", out var headerValues)
            || string.IsNullOrEmpty(headerValues.FirstOrDefault()))
        {
            var response = requestData.CreateResponse(System.Net.HttpStatusCode.Forbidden);
            await response.WriteStringAsync("Forbidden");
            SetHttpResponseData(context, response);
            return;
        }

        await next(context);
    }

    private static void SetHttpResponseData(FunctionContext context, HttpResponseData response)
    {
        var invocationResult = context.GetInvocationResult();
        invocationResult.Value = response;
    }
}
