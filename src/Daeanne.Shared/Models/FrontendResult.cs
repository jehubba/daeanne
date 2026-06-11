namespace Daeanne.Shared.Models;

public record FrontendResult(string CorrelationId, bool Succeeded, string? Response = null, string? Error = null);
