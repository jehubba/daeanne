namespace Daeanne.Shared.Requests;

/// <summary>
/// Phase 2 of the callback contract.
/// Sub-agent POSTs this when its work is complete.
/// Triggers the parent task to be re-queued as Pending for fresh dispatch.
/// </summary>
public class PostCallbackRequest
{
    public Guid    SubtaskId  { get; set; }

    /// <summary>One-paragraph summary the parent agent can read at resume time.</summary>
    public string? Summary    { get; set; }

    /// <summary>Path to the detailed output file (relative to sub-task work dir).</summary>
    public string? ResultPath { get; set; }

    /// <summary>Whether the sub-task considers itself to have succeeded.</summary>
    public bool    Succeeded  { get; set; } = true;
}
