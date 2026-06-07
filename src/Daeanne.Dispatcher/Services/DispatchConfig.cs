using Daeanne.Shared.Models;

namespace Daeanne.Dispatcher.Services;

/// <summary>
/// Maps task types to Copilot CLI agent names and controls dispatch behavior.
/// Configured via "Dispatch" section in appsettings.json.
/// </summary>
public class DispatchConfig
{
    public string? WorkDir { get; set; }

    public string ResolvedWorkDir => string.IsNullOrWhiteSpace(WorkDir)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".daeanne", "tasks")
        : WorkDir;

    /// <summary>Maximum minutes a dispatched task may run before being timed out.</summary>
    public int TaskTimeoutMinutes { get; set; } = 10;

    /// <summary>Maximum number of tasks that may run concurrently.</summary>
    public int MaxConcurrency { get; set; } = 3;

    /// <summary>Maps AgentTaskType → Copilot CLI agent name (as in ~/.copilot/agents/<name>.agent.md).</summary>
    public Dictionary<string, string> AgentMap { get; set; } = new()
    {
        ["Research"]  = "research-agent",
        ["Generic"]   = "daeanne"
    };

    public string? GetAgentName(AgentTaskType type) =>
        AgentMap.TryGetValue(type.ToString(), out var name) ? name : null;
}
