namespace Daeanne.Shared.Requests;

/// <summary>
/// Posted by the Bridge when an inbound SMS arrives (signal-cli → Bridge → Dispatcher).
/// Creates an SmsMessages log entry and optionally resolves a quoted message to a task.
/// </summary>
public class LogInboundSmsRequest
{
    public string  From            { get; set; } = string.Empty;  // sender phone (E.164)
    public string  Body            { get; set; } = string.Empty;
    public string? QuoteTimestamp  { get; set; }  // Signal reply reference, if Jeffrey quoted a message
}
