using Daeanne.Shared.Models;

namespace Daeanne.Shared.Requests;

public class CreateTaskRequest
{
    public AgentTaskType Type { get; set; } = AgentTaskType.Generic;
    public string Prompt { get; set; } = string.Empty;
    public string? ContextJson { get; set; }
    public string? CorrelationId { get; set; }
}
