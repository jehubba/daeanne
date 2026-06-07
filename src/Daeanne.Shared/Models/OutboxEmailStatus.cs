namespace Daeanne.Shared.Models;

public enum OutboxEmailStatus
{
    Pending,
    Processing,  // Claimed by Bridge, publishing to Service Bus
    Sent,        // Published to Service Bus outbound queue
    Failed
}
