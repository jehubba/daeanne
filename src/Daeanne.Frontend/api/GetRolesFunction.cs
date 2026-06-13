using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace DaeanneFrontend.Api;

public class GetRolesFunction
{
    private readonly string[] _allowedEmails;

    public GetRolesFunction()
    {
        var raw = Environment.GetEnvironmentVariable("ALLOWED_USER_EMAILS")
            ?? Environment.GetEnvironmentVariable("ALLOWED_USER_EMAIL")
            ?? string.Empty;
        _allowedEmails = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    [Function("get-roles")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "get-roles")] HttpRequestData req)
    {
        var body = await req.ReadAsStringAsync();
        var roles = new List<string>();

        if (!string.IsNullOrEmpty(body))
        {
            try
            {
                var clientPrincipal = JsonSerializer.Deserialize<RolesClientPrincipal>(body);
                if (clientPrincipal is not null &&
                    _allowedEmails.Length > 0 &&
                    _allowedEmails.Any(e => string.Equals(clientPrincipal.UserDetails, e, StringComparison.OrdinalIgnoreCase)))
                {
                    roles.Add("admin");
                }
            }
            catch
            {
                // Invalid payload — return no roles
            }
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new RolesResponse { Roles = roles });
        return response;
    }

    private sealed class RolesClientPrincipal
    {
        [JsonPropertyName("identityProvider")]
        public string? IdentityProvider { get; set; }

        [JsonPropertyName("userId")]
        public string? UserId { get; set; }

        [JsonPropertyName("userDetails")]
        public string? UserDetails { get; set; }

        [JsonPropertyName("claims")]
        public List<RoleClaim>? Claims { get; set; }
    }

    private sealed class RoleClaim
    {
        [JsonPropertyName("typ")]
        public string? Type { get; set; }

        [JsonPropertyName("val")]
        public string? Value { get; set; }
    }

    private sealed class RolesResponse
    {
        [JsonPropertyName("roles")]
        public List<string> Roles { get; set; } = new();
    }
}
