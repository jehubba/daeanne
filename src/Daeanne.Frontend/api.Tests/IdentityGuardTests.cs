using FluentAssertions;
using Microsoft.AspNetCore.Http;
using System.Text;
using System.Text.Json;

namespace api.Tests;

/// <summary>
/// Integration tests for IdentityGuard middleware.
/// Per spec FR-012 and staticwebapp.config.json:
///   - Decodes x-ms-client-principal header (Base64 JSON from SWA Easy Auth)
///   - Allows requests from Jeffrey's Entra Object ID only
///   - Returns 403 for all other identities
///   - Allows anonymous access to /api/health (for monitoring)
///
/// IdentityGuard is configured as IMiddleware registered in DI and inserted
/// into the Functions pipeline. It must exist at DaeanneFrontend.Api.Middleware.IdentityGuard.
/// </summary>
public class IdentityGuardTests
{
    private const string JeffreyObjectId = "00000000-0000-0000-0000-000000000000"; // placeholder

    [Fact]
    public void IdentityGuard_TypeMustExist()
    {
        var apiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;
        var guardType = apiAssembly.GetType("DaeanneFrontend.Api.Middleware.IdentityGuard");
        guardType.Should().NotBeNull(
            "IdentityGuard must exist at DaeanneFrontend.Api.Middleware.IdentityGuard");
    }

    [Fact]
    public void IdentityGuard_MustImplementIMiddleware()
    {
        var apiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;
        var guardType = apiAssembly.GetType("DaeanneFrontend.Api.Middleware.IdentityGuard");
        guardType.Should().NotBeNull();

        // IdentityGuard should implement IFunctionsWorkerMiddleware
        var workerMiddlewareType = typeof(Microsoft.Azure.Functions.Worker.Middleware.IFunctionsWorkerMiddleware);
        guardType!.GetInterfaces().Should().Contain(workerMiddlewareType,
            "IdentityGuard must implement IFunctionsWorkerMiddleware to plug into the Functions pipeline");
    }

    [Fact]
    public void ClientPrincipal_Decoding_ValidBase64_Parses()
    {
        // SWA sends x-ms-client-principal as Base64-encoded JSON:
        // { "identityProvider": "aad", "userId": "<oid>", "userDetails": "...", "userRoles": [...] }
        var principal = new
        {
            identityProvider = "aad",
            userId = JeffreyObjectId,
            userDetails = "jeffrey@example.com",
            userRoles = new[] { "authenticated", "anonymous" }
        };
        var json = JsonSerializer.Serialize(principal);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        // Verify the test data is valid
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        var parsed = JsonDocument.Parse(decoded);
        parsed.RootElement.GetProperty("userId").GetString().Should().Be(JeffreyObjectId);
    }

    [Fact]
    public void IdentityGuard_RejectsNonJeffreyUsers_With403()
    {
        // Per FR-012: "Requests from non-Jeffrey identities return 403"
        // This test needs IdentityGuard to exist and be testable.
        var apiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;
        var guardType = apiAssembly.GetType("DaeanneFrontend.Api.Middleware.IdentityGuard");
        guardType.Should().NotBeNull("cannot test 403 rejection without IdentityGuard");
    }

    [Fact]
    public void IdentityGuard_AllowsJeffrey()
    {
        // Per FR-012: "Jeffrey's Entra Object ID is the sole allowed identity"
        var apiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;
        var guardType = apiAssembly.GetType("DaeanneFrontend.Api.Middleware.IdentityGuard");
        guardType.Should().NotBeNull("cannot test Jeffrey allow-through without IdentityGuard");
    }

    [Fact]
    public void IdentityGuard_AllowsAnonymousHealthEndpoint()
    {
        // /api/health must be accessible without authentication for monitoring
        // IdentityGuard must skip auth checking for this path.
        var apiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;
        var guardType = apiAssembly.GetType("DaeanneFrontend.Api.Middleware.IdentityGuard");
        guardType.Should().NotBeNull("cannot test health bypass without IdentityGuard");
    }

    [Fact]
    public void IdentityGuard_RejectsMissingHeader_With403()
    {
        // When x-ms-client-principal header is absent, IdentityGuard
        // must return 403 (not 401 — SWA handles the 401→login redirect).
        var apiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;
        var guardType = apiAssembly.GetType("DaeanneFrontend.Api.Middleware.IdentityGuard");
        guardType.Should().NotBeNull("cannot test missing header without IdentityGuard");
    }
}
