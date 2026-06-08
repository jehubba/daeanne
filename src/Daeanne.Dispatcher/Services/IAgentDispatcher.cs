using Daeanne.Shared.Models;

namespace Daeanne.Dispatcher.Services;

public interface IAgentDispatcher
{
    Task<DispatchResult> DispatchAsync(AgentTask task, CancellationToken ct = default);

    /// <summary>
    /// Resumes a prior agent session using the CLI --resume flag.
    /// Reads session ID from {workDir}/session.md and re-launches with an orienting prompt.
    /// Returns null if no session.md exists or session ID can't be parsed
    /// (caller should fall back to DispatchAsync or mark Failed).
    /// </summary>
    Task<DispatchResult?> TryResumeAsync(AgentTask task, string workDir, CancellationToken ct = default);
}

public record DispatchResult(bool Succeeded, string? ResultJson, string? Error);
