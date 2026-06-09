namespace Daeanne.Shared.Models;

public enum AgentTaskStatus
{
    Pending,
    Running,

    /// <summary>
    /// Task self-suspended waiting for one or more sub-task callbacks.
    /// The agent process has exited; the task dir holds all state.
    /// Re-queued as Pending automatically when a sub-task posts its callback result.
    /// NOT a terminal status — task dir stays in active/, not re-queued on rehydration.
    /// </summary>
    Awaiting,

    Succeeded,
    Partial,
    Failed,
    TimedOut,

    /// <summary>
    /// Task parked pending a human decision. Daeanne sent an escalation email and
    /// is not processing further. When Jeffrey replies, a new Email task arrives
    /// with [Escalation Ref: &lt;task_id&gt;] in the subject — handle that to resume.
    /// Terminal: task dir will be moved by TaskCleanupWorker on schedule.
    /// </summary>
    Escalated
}
