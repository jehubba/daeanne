namespace DaeanneFrontend.Client.Models;

public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public ChatDirection Direction { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public ChatStatus Status { get; set; }
    public string? CorrelationId { get; set; }
    public bool IsPending => Status is ChatStatus.Sending or ChatStatus.Sent;
}

public enum ChatDirection
{
    Sent,
    Received
}

public enum ChatStatus
{
    Sending,
    Sent,
    Delivered,
    Error
}
