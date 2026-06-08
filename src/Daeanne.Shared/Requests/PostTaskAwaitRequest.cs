namespace Daeanne.Shared.Requests;

/// <summary>
/// Posted by an orchestrating agent (e.g. Daeanne) to self-suspend
/// and wait for a sub-task's callback before continuing.
/// </summary>
public class PostTaskAwaitRequest
{
    /// <summary>The sub-task ID whose completion will re-queue this task.</summary>
    public Guid SubtaskId { get; set; }
}
