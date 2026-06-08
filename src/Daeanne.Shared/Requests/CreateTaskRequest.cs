using Daeanne.Shared.Models;

namespace Daeanne.Shared.Requests;

public class CreateTaskRequest
{
    public AgentTaskType Type { get; set; } = AgentTaskType.Generic;
    public string Prompt { get; set; } = string.Empty;
    public string? ContextJson { get; set; }
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Graph message ID of the inbound email that triggered this task.
    /// When present, stored in ContextJson so the agent can use it
    /// as replyToGraphMessageId when queueing outbox emails.
    /// </summary>
    public string? GraphMessageId { get; set; }

    /// <summary>
    /// True when this task is created by the scheduler (built-in or dynamic cron).
    /// Task directory will be placed under scheduled/ namespace.
    /// </summary>
    public bool IsScheduled { get; set; } = false;

    /// <summary>ID of the ScheduledJob that spawned this task, if any.</summary>
    public Guid? ScheduledJobId { get; set; }

    /// <summary>
    /// Phone number of the sender for InboundSms tasks (E.164 format).
    /// When present, stored in ContextJson so Daeanne can reply via POST /outbox/sms.
    /// </summary>
    public string? SenderPhone { get; set; }

    /// <summary>ID of the parent task that dispatched this as a sub-task.</summary>
    public Guid? ParentTaskId { get; set; }

    /// <summary>
    /// Optional stable Copilot session name for --name flag.
    /// When set, agent accumulates context across separate firings.
    /// When null, task ID is used (isolated per dispatch).
    /// </summary>
    public string? SessionName { get; set; }
}
