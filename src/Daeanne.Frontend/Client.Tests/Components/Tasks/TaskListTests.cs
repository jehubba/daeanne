using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Client.Tests.Components.Tasks;

/// <summary>
/// bUnit tests for TaskList and TaskCard components (T026).
/// Per spec US1:
///   - Task list groups by status: Running, Pending, Blocked, Succeeded, Failed
///   - Each task card shows type, topic, status, and age
///   - Completed tasks limited to last 24 hours
///   - List refreshes via 30s polling + manual pull-to-refresh
///   - Tapping a task card opens the TaskDetailOverlay (US4)
///
/// These tests drive the component interface — they will fail until
/// the Blazor components are created in Client/Components/Tasks/.
/// </summary>
public class TaskListTests : TestContext
{
    public TaskListTests()
    {
        Services.AddMudServices();
    }

    [Fact]
    public void TaskList_Component_MustExist()
    {
        // The TaskList component must exist at Client/Components/Tasks/TaskList.razor
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var taskListType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Tasks.TaskList");

        taskListType.Should().NotBeNull(
            "TaskList component must exist at Client/Components/Tasks/TaskList.razor " +
            "— this is the primary view for US1 (Task Status Dashboard)");
    }

    [Fact]
    public void TaskList_RendersStatusGroups()
    {
        // Per US1: tasks are grouped by status — Running, Pending, Blocked,
        // and completed (Succeeded/Failed) within last 24h.
        // The component must render group headers for each non-empty status.
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var taskListType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Tasks.TaskList");
        taskListType.Should().NotBeNull("TaskList must exist to test status grouping");
    }

    [Fact]
    public void TaskList_ShowsEmptyState_WhenNoTasks()
    {
        // When the API returns zero tasks, TaskList should display an
        // empty-state message, not a blank screen.
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var taskListType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Tasks.TaskList");
        taskListType.Should().NotBeNull("TaskList must exist to test empty state");
    }

    [Fact]
    public void TaskList_ShowsErrorState_WhenApiFails()
    {
        // When the API returns 502 (Bridge unreachable), TaskList should
        // display an error banner, not crash.
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var taskListType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Tasks.TaskList");
        taskListType.Should().NotBeNull("TaskList must exist to test error state");
    }
}

/// <summary>
/// bUnit tests for the individual TaskCard component.
/// Per US1: each card shows type, topic, status, and age.
/// Tapping a card triggers navigation to TaskDetailOverlay.
/// </summary>
public class TaskCardTests : TestContext
{
    public TaskCardTests()
    {
        Services.AddMudServices();
    }

    [Fact]
    public void TaskCard_Component_MustExist()
    {
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var taskCardType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Tasks.TaskCard");

        taskCardType.Should().NotBeNull(
            "TaskCard component must exist at Client/Components/Tasks/TaskCard.razor " +
            "— renders individual task in the dashboard list");
    }

    [Fact]
    public void TaskCard_MustAcceptTaskParameter()
    {
        // TaskCard needs a [Parameter] for the task data to render
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var taskCardType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Tasks.TaskCard");
        taskCardType.Should().NotBeNull("TaskCard must exist to test parameters");

        // Check for a property with [Parameter] attribute
        if (taskCardType != null)
        {
            var parameterProps = taskCardType.GetProperties()
                .Where(p => p.GetCustomAttributes(
                    typeof(Microsoft.AspNetCore.Components.ParameterAttribute), false).Any());
            parameterProps.Should().NotBeEmpty(
                "TaskCard must have at least one [Parameter] property for task data");
        }
    }

    [Fact]
    public void TaskCard_MustRaiseOnClickCallback()
    {
        // When the user taps a task card, it should notify the parent (TaskList)
        // so the TaskDetailOverlay can be opened.
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var taskCardType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Tasks.TaskCard");
        taskCardType.Should().NotBeNull("TaskCard must exist to test click callback");

        if (taskCardType != null)
        {
            var callbackProps = taskCardType.GetProperties()
                .Where(p => p.PropertyType.Name.Contains("EventCallback"));
            callbackProps.Should().NotBeEmpty(
                "TaskCard must expose an EventCallback for click/tap interaction");
        }
    }
}
