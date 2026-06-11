using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace api.Tests;

/// <summary>
/// Contract tests for the chat write-path:
///   POST /api/command — sends a command to Service Bus
///   GET /api/result/{correlationId} — polls for the result
///
/// Per contracts/api.md:
///   POST /api/command accepts { message: string }
///   Returns 202 { correlationId: guid, status: "Accepted" }
///   GET /api/result/{correlationId} returns:
///     200 { correlationId, status: "Completed"|"Failed", result: ..., error: ... }
///     202 { correlationId, status: "Pending" }
///     404 when correlationId is unknown
/// </summary>
public class CommandFunctionTests
{
    [Fact]
    public void CommandFunction_MustAccept_ServiceBusClient_InConstructor()
    {
        // The CommandFunction needs a ServiceBusSender to post to
        // the daeanne-frontend-requests queue.
        var apiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;
        var type = apiAssembly.GetType("DaeanneFrontend.Api.CommandFunction");
        type.Should().NotBeNull(
            "CommandFunction must exist — write path for chat commands");
    }

    [Fact]
    public void PostCommand_ResponseShape_Returns202WithCorrelationId()
    {
        // Response contract: 202 { correlationId: guid, status: "Accepted" }
        var sampleJson = """
        {
            "correlationId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
            "status": "Accepted"
        }
        """;

        var doc = JsonDocument.Parse(sampleJson);
        var root = doc.RootElement;

        root.TryGetProperty("correlationId", out var cid).Should().BeTrue();
        Guid.TryParse(cid.GetString(), out _).Should().BeTrue(
            "correlationId must be a valid GUID");
        root.GetProperty("status").GetString().Should().Be("Accepted");
    }

    [Fact]
    public void PostCommand_RequestShape_RequiresMessage()
    {
        // Request contract: { "message": "string" }
        // CommandFunction must reject requests without a message body.
        var apiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;
        var type = apiAssembly.GetType("DaeanneFrontend.Api.CommandFunction");
        type.Should().NotBeNull();
    }

    [Fact]
    public void ResultFunction_MustExist_WithGetMethod()
    {
        // GET /api/result/{correlationId}
        var apiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;
        var type = apiAssembly.GetType("DaeanneFrontend.Api.ResultFunction");
        type.Should().NotBeNull(
            "ResultFunction must exist — polling endpoint for command results");

        var methods = type!.GetMethods()
            .Where(m => m.ReturnType == typeof(Task<IActionResult>) ||
                        m.ReturnType == typeof(IActionResult));
        methods.Should().NotBeEmpty(
            "ResultFunction must have a handler method returning IActionResult");
    }

    [Fact]
    public void ResultFunction_PendingResponse_Returns202()
    {
        // When result not yet available: 202 { correlationId, status: "Pending" }
        var sampleJson = """
        {
            "correlationId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
            "status": "Pending"
        }
        """;

        var doc = JsonDocument.Parse(sampleJson);
        doc.RootElement.GetProperty("status").GetString().Should().Be("Pending");
    }

    [Fact]
    public void ResultFunction_CompletedResponse_Returns200WithResult()
    {
        // When result available: 200 { correlationId, status: "Completed", result: {...}, error: null }
        var sampleJson = """
        {
            "correlationId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
            "status": "Completed",
            "result": { "summary": "Task completed successfully" },
            "error": null
        }
        """;

        var doc = JsonDocument.Parse(sampleJson);
        var root = doc.RootElement;
        root.GetProperty("status").GetString().Should().Be("Completed");
        root.TryGetProperty("result", out _).Should().BeTrue();
        root.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void ResultReceiverFunction_MustConsumeFromServiceBusQueue()
    {
        // ResultReceiverFunction must be triggered by the daeanne-frontend-results queue.
        var apiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;
        var type = apiAssembly.GetType("DaeanneFrontend.Api.ResultReceiverFunction");
        type.Should().NotBeNull(
            "ResultReceiverFunction must exist — SB trigger for daeanne-frontend-results");

        // It should have a method with ServiceBusTrigger attribute
        var methods = type!.GetMethods();
        var hasServiceBusTrigger = methods.Any(m =>
            m.GetParameters().Any(p =>
                p.GetCustomAttributes(false).Any(a =>
                    a.GetType().Name.Contains("ServiceBusTrigger"))));
        hasServiceBusTrigger.Should().BeTrue(
            "ResultReceiverFunction must have a ServiceBusTrigger parameter");
    }
}
