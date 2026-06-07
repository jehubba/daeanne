using Daeanne.Shared.Models;

namespace Daeanne.Dispatcher.Services;

public interface IAgentDispatcher
{
    Task<DispatchResult> DispatchAsync(AgentTask task, CancellationToken ct = default);
}

public record DispatchResult(bool Succeeded, string? ResultJson, string? Error);
