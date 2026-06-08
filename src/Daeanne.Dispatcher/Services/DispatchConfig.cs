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

    /// <summary>
    /// Base URL of this Dispatcher, injected into sub-task prompts for callback_url.
    /// Defaults to the localhost binding — override in appsettings if port changes.
    /// </summary>
    public string DispatcherUrl { get; set; } = "http://127.0.0.1:47777";

    /// <summary>
    /// When true, Daeanne runs in a visible PowerShell window on cold start.
    /// The window closes automatically when she finishes the task.
    /// Useful for debugging; stdout is not captured in this mode (session.md is the record).
    /// </summary>
    public bool ShowAgentWindow { get; set; } = false;

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
