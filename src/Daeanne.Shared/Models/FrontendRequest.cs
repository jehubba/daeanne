namespace Daeanne.Shared.Models;

public record FrontendRequest(string Prompt, string CorrelationId, string TaskType = "Generic");
