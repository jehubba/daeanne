using Daeanne.Shared.Models;

namespace Daeanne.Shared.Requests;

public class CreateTaskRequest
{
    public AgentTaskType Type { get; set; } = AgentTaskType.Generic;
    public string Prompt { get; set; } = string.Empty;
    public string? ContextJson { get; set; }
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Graph message ID of the inbound email that triggered this task.
    /// When present, stored in ContextJson so the agent can use it
    /// as replyToGraphMessageId when queueing outbox emails.
    /// </summary>
    public string? GraphMessageId { get; set; }
}
