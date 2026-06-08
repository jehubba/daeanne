namespace Daeanne.Shared.Requests;

/// <summary>
/// Phase 1 of the callback contract.
/// Sub-agent POSTs this immediately on startup after reading its context,
/// before doing any work. Confirms the agent started and received the callback URL.
/// Analogous to HTTP 202 Accepted — "I got this, I'm working on it."
/// </summary>
public class PostCallbackAckRequest
{
    public Guid SubtaskId { get; set; }
}
