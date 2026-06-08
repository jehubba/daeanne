namespace Daeanne.Shared.Models;

public class OutboxSms
{
    public Guid   Id            { get; set; } = Guid.NewGuid();
    public string To            { get; set; } = string.Empty;  // E.164 phone number
    public string Body          { get; set; } = string.Empty;  // ≤ 1600 chars (10 SMS segments)
    public OutboxSmsStatus Status { get; set; } = OutboxSmsStatus.Pending;
    public DateTime  CreatedAt  { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt     { get; set; }
    public string?   Error      { get; set; }
    public string?   CorrelationId { get; set; }
}
