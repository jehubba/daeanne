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

    /// <summary>
    /// ID of the ScheduledJob that spawned this task, if any.
    /// Null for built-in scheduler jobs and manually created tasks.
    /// </summary>
    public Guid? ScheduledJobId { get; set; }

    public string? ResultJson { get; set; }
    public string? Error { get; set; }

    public static readonly AgentTaskStatus[] TerminalStatuses =
        [AgentTaskStatus.Succeeded, AgentTaskStatus.Partial, AgentTaskStatus.Failed, AgentTaskStatus.TimedOut];

    public bool IsTerminal() => TerminalStatuses.Contains(Status);
}
