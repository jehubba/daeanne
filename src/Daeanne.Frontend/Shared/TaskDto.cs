namespace DaeanneFrontend.Shared;

public record TaskDto(
    int Id,
    string Type,
    string Topic,
    string Status,
    string Age,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    string? ResultJson = null,
    string? Error = null,
    string? CorrelationId = null);
