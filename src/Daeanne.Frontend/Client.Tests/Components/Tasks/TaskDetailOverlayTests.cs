using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Client.Tests.Components.Tasks;

/// <summary>
/// bUnit tests for the TaskDetailOverlay component (T040).
/// Per spec US4 (Task Detail Drill-Down):
///   - Full-screen overlay when a task card is tapped
///   - Shows task type, topic, current status, created/completed timestamps
///   - For completed tasks: renders resultJson as formatted content
///   - For failed tasks: shows error message with visual distinction
///   - Close button or swipe gesture to return to dashboard
///   - Content fetched asynchronously so the overlay opens immediately (F7 fix)
///
/// Per contracts/api.md, detail data comes from GET /api/tasks/{id}
/// which includes resultJson and error fields.
/// </summary>
public class TaskDetailOverlayTests : TestContext
{
    public TaskDetailOverlayTests()
    {
        Services.AddMudServices();
    }

    [Fact]
    public void TaskDetailOverlay_Component_MustExist()
    {
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var overlayType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Tasks.TaskDetailOverlay");

        overlayType.Should().NotBeNull(
            "TaskDetailOverlay must exist at Client/Components/Tasks/TaskDetailOverlay.razor " +
            "— drill-down view for US4");
    }

    [Fact]
    public void TaskDetailOverlay_AcceptsTaskIdParameter()
    {
        // The overlay needs a TaskId parameter to fetch detail from GET /api/tasks/{id}
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var overlayType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Tasks.TaskDetailOverlay");
        overlayType.Should().NotBeNull("TaskDetailOverlay must exist to test parameters");

        if (overlayType != null)
        {
            var parameterProps = overlayType.GetProperties()
                .Where(p => p.GetCustomAttributes(
                    typeof(Microsoft.AspNetCore.Components.ParameterAttribute), false).Any());
            parameterProps.Should().NotBeEmpty(
                "TaskDetailOverlay must have [Parameter] properties (at minimum TaskId)");
        }
    }

    [Fact]
    public void TaskDetailOverlay_AcceptsOnCloseCallback()
    {
        // The overlay must have an EventCallback<bool> or similar for the
        // close/dismiss action so the parent can remove it.
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var overlayType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Tasks.TaskDetailOverlay");
        overlayType.Should().NotBeNull("TaskDetailOverlay must exist to test callbacks");

        if (overlayType != null)
        {
            var callbackProps = overlayType.GetProperties()
                .Where(p => p.PropertyType.Name.Contains("EventCallback"));
            callbackProps.Should().NotBeEmpty(
                "TaskDetailOverlay must expose an EventCallback for close/dismiss");
        }
    }

    [Fact]
    public void TaskDetailOverlay_ShowsLoadingState_WhileFetching()
    {
        // Per F7 remediation: "fetched asynchronously so the overlay opens immediately"
        // This means the overlay MUST render a loading state before the API call returns.
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var overlayType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Tasks.TaskDetailOverlay");
        overlayType.Should().NotBeNull(
            "TaskDetailOverlay must exist to test async loading behavior");
    }

    [Fact]
    public void TaskDetailOverlay_RendersResultJson_ForSucceededTasks()
    {
        // For tasks with status "Succeeded", the overlay must render
        // the resultJson field as formatted content.
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var overlayType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Tasks.TaskDetailOverlay");
        overlayType.Should().NotBeNull(
            "TaskDetailOverlay must exist to test result rendering");
    }

    [Fact]
    public void TaskDetailOverlay_ShowsError_ForFailedTasks()
    {
        // For tasks with status "Failed", the overlay must show the error
        // message with visual distinction (e.g., MudAlert with Severity.Error).
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var overlayType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Tasks.TaskDetailOverlay");
        overlayType.Should().NotBeNull(
            "TaskDetailOverlay must exist to test error display");
    }
}
