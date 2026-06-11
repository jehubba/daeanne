using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace DaeanneFrontend.Api;

public class GetRolesFunction
{
    private readonly string _allowedEmail;

    public GetRolesFunction()
    {
        _allowedEmail = Environment.GetEnvironmentVariable("ALLOWED_USER_EMAIL") ?? string.Empty;
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
                    !string.IsNullOrEmpty(_allowedEmail) &&
                    string.Equals(clientPrincipal.UserDetails, _allowedEmail, StringComparison.OrdinalIgnoreCase))
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
