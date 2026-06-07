namespace Daeanne.Shared.Models;

/// <summary>
/// Canonical message contract for emails on the Service Bus.
/// Used by Daeanne.Bridge (inbound and outbound) and Daeanne.Functions.
/// </summary>
public record BridgeEmailMessage
{
    /// <summary>Unique ID for this bridge message (not the ACS message ID).</summary>
    public Guid MessageId { get; init; } = Guid.NewGuid();

    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string BodyText { get; init; } = string.Empty;
    public string? BodyHtml { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Original ACS message ID (inbound only).</summary>
    public string? AcsMessageId { get; init; }

    /// <summary>Dispatcher OutboxEmail.Id this message was built from (outbound only).</summary>
    public Guid? OutboxEmailId { get; init; }
}
