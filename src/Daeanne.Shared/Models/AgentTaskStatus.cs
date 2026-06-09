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
    Escalated,

    // ── Dormant states ──────────────────────────────────────────────────────
    // These are NOT terminal and NOT dispatched. Task is persisted in the DB
    // and a directory is created, but no agent is launched until the task is
    // promoted to Pending via PATCH /tasks/{id}/status {"status":"Pending"}.
    // Task dirs live under tasks/pending/, tasks/blocked/, or tasks/future/.

    /// <summary>
    /// Task is known but intentionally parked — no urgency, no blocker.
    /// Daeanne created this task ahead of time and will promote it when ready.
    /// Dir: tasks/pending/{id}/
    /// </summary>
    Deferred,

    /// <summary>
    /// Task cannot proceed until an external decision or input is resolved.
    /// Daeanne created this task while waiting on Jeffrey or an external dependency.
    /// Dir: tasks/blocked/{id}/
    /// </summary>
    Blocked,

    /// <summary>
    /// Task is speculative / horizon item — not yet actionable.
    /// Created to capture intent without committing to dispatch.
    /// Dir: tasks/future/{id}/
    /// </summary>
    Future
}
