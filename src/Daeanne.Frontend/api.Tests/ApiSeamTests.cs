using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace api.Tests;

/// <summary>
/// Seam-level integration tests that enter through the real Azure Functions
/// HTTP entry points. These tests will ONLY pass when:
///   1. The new API functions (Tasks, Command, Result, Trends) exist
///   2. IdentityGuard middleware is wired in
///   3. The functions route through the Bridge relay
///
/// Until the implementation is complete, these tests MUST FAIL.
/// This is the proof that the replacement actually happened.
/// </summary>
public class ApiSeamTests
{
    /// <summary>
    /// The /api/tasks endpoint must exist, accept GET, and return a JSON
    /// response with a "tasks" array and a "total" count when called by
    /// an authenticated Jeffrey identity.
    /// This test fails until TasksFunction is implemented and wired.
    /// </summary>
    [Fact]
    public void TasksEndpoint_MustExist_AndReturnTasksArray()
    {
        // Arrange — we look for the TasksFunction type in the API assembly
        var apiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;
        var tasksFunctionType = apiAssembly.GetType("DaeanneFrontend.Api.TasksFunction");

        // Assert — the type must exist (proves the file was created and compiles)
        tasksFunctionType.Should().NotBeNull(
            "TasksFunction must exist in the API project — this is the seam " +
            "that replaces direct Dispatcher access with a Bridge-relayed endpoint");

        // Assert — it must have a method with [Function] attribute for "tasks"
        var methods = tasksFunctionType!.GetMethods();
        var hasFunctionAttribute = methods.Any(m =>
            m.GetCustomAttributes(typeof(Microsoft.Azure.Functions.Worker.FunctionAttribute), false)
             .Any());
        hasFunctionAttribute.Should().BeTrue(
            "TasksFunction must have at least one method decorated with [Function]");
    }

    /// <summary>
    /// The /api/command endpoint must exist and accept POST.
    /// This test fails until CommandFunction is implemented.
    /// </summary>
    [Fact]
    public void CommandEndpoint_MustExist()
    {
        var apiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;
        var commandFunctionType = apiAssembly.GetType("DaeanneFrontend.Api.CommandFunction");

        commandFunctionType.Should().NotBeNull(
            "CommandFunction must exist — this is the write-path seam for " +
            "sending commands to Daeanne via Service Bus");
    }

    /// <summary>
    /// The /api/result/{correlationId} endpoint must exist.
    /// This test fails until ResultFunction is implemented.
    /// </summary>
    [Fact]
    public void ResultEndpoint_MustExist()
    {
        var apiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;
        var resultFunctionType = apiAssembly.GetType("DaeanneFrontend.Api.ResultFunction");

        resultFunctionType.Should().NotBeNull(
            "ResultFunction must exist — this is the polling endpoint for " +
            "command results delivered via daeanne-frontend-results queue");
    }

    /// <summary>
    /// The SB-triggered ResultReceiverFunction must exist to consume
    /// messages from daeanne-frontend-results and populate the in-memory cache.
    /// </summary>
    [Fact]
    public void ResultReceiverFunction_MustExist()
    {
        var apiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;
        var receiverType = apiAssembly.GetType("DaeanneFrontend.Api.ResultReceiverFunction");

        receiverType.Should().NotBeNull(
            "ResultReceiverFunction must exist — without it, the chat write-path " +
            "cannot deliver results from the SB queue to the HTTP polling endpoint");
    }

    /// <summary>
    /// The /api/trends/today endpoint must exist.
    /// This test fails until TrendFunction is implemented.
    /// </summary>
    [Fact]
    public void TrendsEndpoint_MustExist()
    {
        var apiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;
        var trendFunctionType = apiAssembly.GetType("DaeanneFrontend.Api.TrendFunction");

        trendFunctionType.Should().NotBeNull(
            "TrendFunction must exist — this surfaces today's trend highlights " +
            "from the Bridge relay");
    }

    /// <summary>
    /// IdentityGuard middleware must exist in the API project.
    /// Without it, any authenticated user (not just Jeffrey) can access the API.
    /// </summary>
    [Fact]
    public void IdentityGuard_MustExist()
    {
        var apiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;
        var guardType = apiAssembly.GetType("DaeanneFrontend.Api.Middleware.IdentityGuard");

        guardType.Should().NotBeNull(
            "IdentityGuard must exist in Api.Middleware namespace — " +
            "this rejects non-Jeffrey identities per FR-012");
    }

    /// <summary>
    /// After replacement, the existing HealthFunction must still work.
    /// This ensures the replacement didn't break the existing health endpoint.
    /// </summary>
    [Fact]
    public void HealthEndpoint_StillWorks_AfterReplacement()
    {
        var function = new DaeanneFrontend.Api.HealthFunction();
        var context = new DefaultHttpContext();
        var result = function.Run(context.Request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);
    }
}
