namespace DaeanneFrontend.Shared;

public record CommandResultDto(
    string CorrelationId,
    string Status,
    bool? Succeeded = null,
    string? Response = null,
    string? Error = null);
