namespace Daeanne.Shared.Requests;

public class QueueSmsRequest
{
    public string  To            { get; set; } = string.Empty;  // E.164 phone number
    public string  Body          { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
}
