namespace Daeanne.Shared.Requests;

public class QueueSmsRequest
{
    public string  To            { get; set; } = string.Empty;  // E.164 phone number
    public string  Body          { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }

    /// <summary>
    /// AgentTask that produced this message.
    /// When provided, the Dispatcher auto-logs the outbound SMS to SmsMessages
    /// and appends a reference token to the body if not already present.
    /// </summary>
    public Guid?   TaskId        { get; set; }
}
