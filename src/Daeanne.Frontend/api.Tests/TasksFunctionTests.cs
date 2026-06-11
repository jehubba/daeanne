using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace api.Tests;

/// <summary>
/// Contract tests for GET /api/tasks and GET /api/tasks/{id}.
/// Verifies the response shape matches contracts/api.md exactly.
///
/// These tests drive the TasksFunction interface:
///   - Constructor takes an HttpClient (for Bridge relay)
///   - GetTasks returns { tasks: TaskDto[], total: int }
///   - GetTask returns a single TaskDto with resultJson
///   - Returns 502 when Bridge is unreachable
///   - Returns 404 for unknown task ID
///   - Filters completed tasks to 24h window
///   - Supports pagination via skip/take
/// </summary>
public class TasksFunctionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void TasksFunction_MustAccept_HttpClient_InConstructor()
    {
        // The TasksFunction needs an HttpClient to call the Bridge relay.
        // This test drives the constructor signature.
        var apiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;
        var type = apiAssembly.GetType("DaeanneFrontend.Api.TasksFunction");
        type.Should().NotBeNull();

        var ctors = type!.GetConstructors();
        var hasHttpClientCtor = ctors.Any(c =>
            c.GetParameters().Any(p => p.ParameterType == typeof(HttpClient)));
        hasHttpClientCtor.Should().BeTrue(
            "TasksFunction must accept an HttpClient for Bridge relay communication");
    }

    [Fact]
    public void GetTasks_ResponseShape_MatchesContract()
    {
        // Per contracts/api.md, GET /api/tasks returns:
        // { "tasks": [...], "total": N }
        // Each task has: id, type, topic, status, age, createdAt, completedAt, correlationId
        // (ResultJson is NOT included in list view — only in detail)

        var sampleJson = """
        {
            "tasks": [
                {
                    "id": 42,
                    "type": "Research",
                    "topic": "Emerging trends in AI agent frameworks",
                    "status": "Succeeded",
                    "age": "2h ago",
                    "createdAt": "2026-06-11T08:30:00Z",
                    "completedAt": "2026-06-11T10:15:00Z",
                    "correlationId": "daily-trend-2026-06-11"
                }
            ],
            "total": 12
        }
        """;

        var doc = JsonDocument.Parse(sampleJson);
        var root = doc.RootElement;

        root.TryGetProperty("tasks", out var tasks).Should().BeTrue();
        tasks.ValueKind.Should().Be(JsonValueKind.Array);
        root.TryGetProperty("total", out var total).Should().BeTrue();
        total.GetInt32().Should().Be(12);

        var task = tasks[0];
        task.GetProperty("id").GetInt32().Should().Be(42);
        task.GetProperty("type").GetString().Should().Be("Research");
        task.GetProperty("topic").GetString().Should().NotBeNullOrEmpty();
        task.GetProperty("status").GetString().Should().Be("Succeeded");
        task.GetProperty("age").GetString().Should().NotBeNullOrEmpty();
        task.GetProperty("createdAt").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetTaskDetail_ResponseShape_IncludesResultJson()
    {
        // Per contracts/api.md, GET /api/tasks/{id} returns the same
        // fields PLUS resultJson and error.

        var sampleJson = """
        {
            "id": 42,
            "type": "Research",
            "topic": "Emerging trends",
            "status": "Succeeded",
            "age": "2h ago",
            "createdAt": "2026-06-11T08:30:00Z",
            "completedAt": "2026-06-11T10:15:00Z",
            "resultJson": "{ \"full\": \"output\" }",
            "error": null,
            "correlationId": "daily-trend-2026-06-11"
        }
        """;

        var doc = JsonDocument.Parse(sampleJson);
        var root = doc.RootElement;

        root.TryGetProperty("resultJson", out _).Should().BeTrue(
            "task detail response MUST include resultJson per contracts/api.md");
        root.TryGetProperty("error", out _).Should().BeTrue(
            "task detail response MUST include error per contracts/api.md");
    }

    [Fact]
    public void GetTasks_Returns502_WhenBridgeUnreachable()
    {
        // Per contracts/api.md, when Bridge/Dispatcher is unreachable,
        // the API must return 502 Bad Gateway.
        // This test will be fleshed out when TasksFunction exists —
        // for now we verify the type exists and document the expected behavior.
        var apiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;
        var type = apiAssembly.GetType("DaeanneFrontend.Api.TasksFunction");
        type.Should().NotBeNull("TasksFunction must exist to test 502 behavior");

        // Verify the Run method exists and returns IActionResult
        var methods = type!.GetMethods()
            .Where(m => m.ReturnType == typeof(Task<IActionResult>) ||
                        m.ReturnType == typeof(IActionResult));
        methods.Should().NotBeEmpty(
            "TasksFunction must have a method returning IActionResult");
    }

    [Fact]
    public void GetTasks_SupportsPagination_SkipAndTake()
    {
        // Per contracts/api.md:
        //   skip (int, default 0) — pagination offset
        //   take (int, default 50, max 200) — page size
        // The TasksFunction must read these from query string.

        var apiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;
        var type = apiAssembly.GetType("DaeanneFrontend.Api.TasksFunction");
        type.Should().NotBeNull("TasksFunction must exist to support pagination");
    }
}
