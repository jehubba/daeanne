using System.Net;
using System.Net.Http.Json;
using Daeanne.Shared.Models;
using FluentAssertions;

namespace Daeanne.Dispatcher.Tests;

/// <summary>
/// Integration tests for closing dormant (Blocked/Deferred/Future) tasks via
/// PATCH /tasks/{id}/status with a terminal status.
///
/// Issue #60: before the fix, the dormant branch only allowed status=Pending,
/// returning 400 for any terminal status. These tests fail until the fix is applied.
/// </summary>
[Collection("Dispatcher")]
public class DormantTaskCloseTests : IClassFixture<DispatcherWebAppFactory>
{
    private readonly DispatcherWebAppFactory _factory;

    public DormantTaskCloseTests(DispatcherWebAppFactory factory)
    {
        _factory = factory;
    }

    // ── Tests that fail BEFORE the fix ──────────────────────────────────────

    [Theory]
    [InlineData("Blocked",  "Succeeded")]
    [InlineData("Blocked",  "Failed")]
    [InlineData("Blocked",  "Escalated")]
    [InlineData("Deferred", "Succeeded")]
    [InlineData("Future",   "Succeeded")]
    public async Task PatchStatus_DormantToTerminal_Returns200(
        string initialStatus, string terminalStatus)
    {
        var client = _factory.CreateClient();

        // Create a dormant task
        var create = await client.PostAsJsonAsync("/tasks", new
        {
            type          = "Generic",
            prompt        = $"dormant close test [{initialStatus}→{terminalStatus}]",
            initialStatus,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var task = await create.Content.ReadFromJsonAsync<AgentTask>(_factory.JsonOptions);
        task.Should().NotBeNull();
        task!.Status.ToString().Should().Be(initialStatus);

        // PATCH to terminal — FAILS before fix (returns 400), passes after
        var patch = await client.PatchAsJsonAsync($"/tasks/{task.Id}/status", new
        {
            status = terminalStatus,
        });

        patch.StatusCode.Should().Be(HttpStatusCode.OK,
            $"closing dormant task from {initialStatus} with {terminalStatus} should return 200");

        var updated = await patch.Content.ReadFromJsonAsync<AgentTask>(_factory.JsonOptions);
        updated.Should().NotBeNull();
        updated!.Status.ToString().Should().Be(terminalStatus);
        updated.CompletedAt.Should().NotBeNull("terminal status must stamp CompletedAt");
        updated.AgentReported.Should().BeTrue("explicit close must set AgentReported=true");
    }

    [Fact]
    public async Task PatchStatus_DormantToTerminal_TaskDirMovesToFinalLocation()
    {
        var client  = _factory.CreateClient();
        var workDir = _factory.WorkDir;

        // Create a Blocked task
        var create = await client.PostAsJsonAsync("/tasks", new
        {
            type          = "Generic",
            prompt        = "dormant dir-move test",
            initialStatus = "Blocked",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var task = await create.Content.ReadFromJsonAsync<AgentTask>(_factory.JsonOptions);
        task.Should().NotBeNull();

        var blockedDir  = Path.Combine(workDir, "blocked",  task!.Id.ToString());
        var completeDir = Path.Combine(workDir, "complete", task.Id.ToString());

        Directory.Exists(blockedDir).Should().BeTrue($"task dir should be at {blockedDir} after creation");

        // Close it — FAILS before fix
        var patch = await client.PatchAsJsonAsync($"/tasks/{task.Id}/status", new
        {
            status = "Succeeded",
        });
        patch.StatusCode.Should().Be(HttpStatusCode.OK);

        Directory.Exists(completeDir).Should().BeTrue($"task dir should be at {completeDir} after close");
        Directory.Exists(blockedDir).Should().BeFalse("blocked dir should no longer exist after close");
    }

    [Fact]
    public async Task PatchStatus_DormantToTerminal_ResultJsonPreserved()
    {
        var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync("/tasks", new
        {
            type          = "Generic",
            prompt        = "dormant resultJson test",
            initialStatus = "Deferred",
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var task = await create.Content.ReadFromJsonAsync<AgentTask>(_factory.JsonOptions);
        task.Should().NotBeNull();

        var expectedResponse = "manually resolved";

        // PATCH with resultJson — FAILS before fix
        var patch = await client.PatchAsJsonAsync($"/tasks/{task!.Id}/status", new
        {
            status     = "Succeeded",
            resultJson = new { response = expectedResponse },
        });
        patch.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await patch.Content.ReadFromJsonAsync<AgentTask>(_factory.JsonOptions);
        updated.Should().NotBeNull();
        updated!.ResultJson.Should().NotBeNullOrWhiteSpace("resultJson must be stored");
        updated.ResultJson.Should().Contain(expectedResponse);
    }

    // ── Regression guard: existing Pending promotion must still work ─────────

    [Theory]
    [InlineData("Blocked")]
    [InlineData("Deferred")]
    [InlineData("Future")]
    public async Task PatchStatus_DormantToPending_StillWorks(string initialStatus)
    {
        var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync("/tasks", new
        {
            type          = "Generic",
            prompt        = $"dormant promote regression [{initialStatus}]",
            initialStatus,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var task = await create.Content.ReadFromJsonAsync<AgentTask>(_factory.JsonOptions);
        task.Should().NotBeNull();

        var patch = await client.PatchAsJsonAsync($"/tasks/{task!.Id}/status", new
        {
            status = "Pending",
        });

        patch.StatusCode.Should().Be(HttpStatusCode.OK, "promoting a dormant task to Pending must still work");

        var updated = await patch.Content.ReadFromJsonAsync<AgentTask>(_factory.JsonOptions);
        updated.Should().NotBeNull();
        updated!.Status.Should().Be(AgentTaskStatus.Pending);
        updated.PromotedAt.Should().NotBeNull("PromotedAt must be set on promotion");
    }
}
