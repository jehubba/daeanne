namespace Daeanne.Shared.Models;

public class OutboxEmail
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string To { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public OutboxEmailStatus Status { get; set; } = OutboxEmailStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public string? Error { get; set; }
    public string? CorrelationId { get; set; }
}
