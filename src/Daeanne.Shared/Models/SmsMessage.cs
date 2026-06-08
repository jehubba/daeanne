namespace Daeanne.Shared.Models;

/// <summary>
/// Unified log of all SMS messages — both inbound and outbound.
/// Used to provide Daeanne with conversation history when handling an InboundSms task,
/// and to resolve Signal quote references back to task IDs.
/// </summary>
public class SmsMessage
{
    public Guid         Id               { get; set; } = Guid.NewGuid();
    public SmsDirection Direction        { get; set; }
    public string       Phone            { get; set; } = string.Empty;  // the non-Daeanne number (E.164)
    public string       Body             { get; set; } = string.Empty;
    public DateTime     Timestamp        { get; set; } = DateTime.UtcNow;

    /// <summary>AgentTask that produced or was created by this message.</summary>
    public Guid?   TaskId           { get; set; }

    /// <summary>
    /// Signal quote timestamp (from signal-cli quoteTimestamp field).
    /// When Jeffrey replies to a specific Daeanne message in Signal, this identifies
    /// which outbound message was quoted — used to resolve which task is being referenced.
    /// </summary>
    public string? QuoteTimestamp   { get; set; }

    /// <summary>
    /// Short 4-char token derived from TaskId (last 4 hex chars of GUID).
    /// Included at the end of outbound SMS when disambiguation is useful: "Done. [a3f2]"
    /// Jeffrey can optionally quote it to reference the task explicitly.
    /// </summary>
    public string? ReferenceToken   { get; set; }

    /// <summary>Links to the OutboxSms record for outbound messages.</summary>
    public Guid?   OutboxSmsId      { get; set; }

    /// <summary>Derives a 4-char reference token from a task ID.</summary>
    public static string TokenFromTaskId(Guid taskId) =>
        taskId.ToString("N")[^4..];
}
