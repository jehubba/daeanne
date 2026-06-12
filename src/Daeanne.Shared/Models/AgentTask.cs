namespace Daeanne.Shared.Models;

public class AgentTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AgentTaskType Type { get; set; } = AgentTaskType.Generic;
    public string Prompt { get; set; } = string.Empty;
    public string? ContextJson { get; set; }
    public AgentTaskStatus Status { get; set; } = AgentTaskStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int AttemptCount { get; set; } = 0;

    /// <summary>Correlation ID from an external source (e.g. Service Bus message ID).</summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// True when this task was created by the scheduler (built-in or dynamic cron).
    /// Controls the task directory namespace: scheduled/ vs root.
    /// </summary>
    public bool IsScheduled { get; set; } = false;

    /// <summary>ID of the ScheduledJob that spawned this task, if any.</summary>
    public Guid? ScheduledJobId { get; set; }

    /// <summary>
    /// Optional stable session name for --name flag. When set (e.g. "trend-analyzer"),
    /// the agent's Copilot session accumulates context across separate firings of this task type.
    /// When null, task ID is used (default — isolated per dispatch).
    /// </summary>
    public string? SessionName { get; set; }

    /// <summary>
    /// ID of the parent task that dispatched this task as a sub-task.
    /// When set, the Dispatcher injects callback_url into the prompt so this agent
    /// knows to POST its result back when complete.
    /// </summary>
    public Guid? ParentTaskId { get; set; }

    /// <summary>
    /// When this sub-task posted its ack to {callback_url}/ack (phase 1 of callback contract).
    /// Null until ack is received — a long-null ack indicates the agent never started.
    /// </summary>
    public DateTime? CallbackAcknowledgedAt { get; set; }

    /// <summary>
    /// When this sub-task posted its result to {callback_url} (phase 2 of callback contract).
    /// </summary>
    public DateTime? CallbackPostedAt { get; set; }

    public string? ResultJson { get; set; }
    public string? Error { get; set; }

    /// <summary>
    /// True when the agent explicitly called PATCH /tasks/{id}/status to report its outcome.
    /// False (default) means the dispatcher auto-marked it based on process exit code.
    /// An auto-marked Succeeded task may not have actually completed its goal.
    /// </summary>
    public bool AgentReported { get; set; } = false;

    public static readonly AgentTaskStatus[] TerminalStatuses =
        [AgentTaskStatus.Succeeded, AgentTaskStatus.Partial, AgentTaskStatus.Failed, AgentTaskStatus.TimedOut, AgentTaskStatus.Escalated];

    /// <summary>
    /// Dormant statuses: task is persisted and has a directory, but has not been dispatched.
    /// Promoted to Pending (and enqueued) via PATCH /tasks/{id}/status {"status":"Pending"}.
    /// </summary>
    public static readonly AgentTaskStatus[] DormantStatuses =
        [AgentTaskStatus.Deferred, AgentTaskStatus.Blocked, AgentTaskStatus.Future];

    public bool IsTerminal() => TerminalStatuses.Contains(Status);

    /// <summary>True when the task is in a dormant state (created but not yet dispatched).</summary>
    public bool IsDormant() => DormantStatuses.Contains(Status);

    /// <summary>Timestamp when this task was promoted from a dormant state to Pending.</summary>
    public DateTime? PromotedAt { get; set; }

    /// <summary>
    /// When set, this task is held as Blocked until the upstream task with this ID reaches Succeeded.
    /// The Dispatcher automatically promotes this task to Pending when the upstream task succeeds.
    /// </summary>
    public Guid? DependsOnTaskId { get; set; }
}
