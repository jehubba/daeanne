namespace Daeanne.Shared.Requests;

public class OutboxEmailRequest
{
    public string To { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
}
