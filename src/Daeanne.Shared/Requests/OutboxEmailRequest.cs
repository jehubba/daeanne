namespace Daeanne.Shared.Requests;

public class OutboxEmailRequest
{
    public string To { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Graph message ID to reply to. When provided, the Bridge sends a threaded reply
    /// rather than a new email.
    /// </summary>
    public string? ReplyToGraphMessageId { get; set; }
}
