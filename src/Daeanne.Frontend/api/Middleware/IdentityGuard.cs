using System.Text;
using System.Text.Json;
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

    private readonly string _allowedUserId;

    public IdentityGuard()
    {
        _allowedUserId = Environment.GetEnvironmentVariable("ALLOWED_USER_OID")
            ?? throw new InvalidOperationException("ALLOWED_USER_OID environment variable is required");
    }

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

        if (!requestData.Headers.TryGetValues("x-ms-client-principal", out var headerValues))
        {
            var response = requestData.CreateResponse(System.Net.HttpStatusCode.Forbidden);
            await response.WriteStringAsync("Forbidden");
            SetHttpResponseData(context, response);
            return;
        }

        var principalHeader = headerValues.FirstOrDefault();
        if (string.IsNullOrEmpty(principalHeader))
        {
            var response = requestData.CreateResponse(System.Net.HttpStatusCode.Forbidden);
            await response.WriteStringAsync("Forbidden");
            SetHttpResponseData(context, response);
            return;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(principalHeader));
            var principal = JsonSerializer.Deserialize<ClientPrincipal>(decoded);

            if (principal is null || !string.Equals(principal.UserId, _allowedUserId, StringComparison.OrdinalIgnoreCase))
            {
                var response = requestData.CreateResponse(System.Net.HttpStatusCode.Forbidden);
                await response.WriteStringAsync("Forbidden");
                SetHttpResponseData(context, response);
                return;
            }
        }
        catch
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

    private record ClientPrincipal(
        string? IdentityProvider,
        string? UserId,
        string? UserDetails,
        string[]? UserRoles);
}
