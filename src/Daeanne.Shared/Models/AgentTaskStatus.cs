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
    TimedOut
}
