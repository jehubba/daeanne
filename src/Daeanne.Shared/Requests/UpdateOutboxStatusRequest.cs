namespace Daeanne.Shared.Requests;

public record UpdateOutboxStatusRequest(string Status, string? Error = null);
