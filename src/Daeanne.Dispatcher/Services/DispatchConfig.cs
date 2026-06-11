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

    /// <summary>
    /// Per-task-type timeout overrides. Keys are AgentTaskType names (e.g. "Research").
    /// Takes precedence over TaskTimeoutMinutes for matching task types.
    /// </summary>
    public Dictionary<string, int> TaskTimeoutMinutesByType { get; set; } = new();

    /// <summary>Returns the effective timeout for a given task type.</summary>
    public int? GetTimeoutMinutes(AgentTaskType type) =>
        TaskTimeoutMinutesByType.TryGetValue(type.ToString(), out var t) ? t : null;

    /// <summary>Maximum number of tasks that may run concurrently.</summary>
    public int MaxConcurrency { get; set; } = 3;

    /// <summary>
    /// How often (minutes) TaskCleanupWorker runs to move terminal task dirs and
    /// dispatch sit-rep tasks for anomalies. Defaults to 60.
    /// </summary>
    public int CleanupIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Full path to the copilot CLI executable.
    /// Defaults to "copilot" (relies on PATH) — set explicitly to avoid PATH issues
    /// when the Dispatcher is started from a session with a stripped environment.
    /// </summary>
    public string CopilotExe { get; set; } = "copilot";

    /// <summary>Maps AgentTaskType → Copilot CLI agent name (as in ~/.copilot/agents/<name>.agent.md).</summary>
    public Dictionary<string, string> AgentMap { get; set; } = new()
    {
        ["Research"]   = "research-agent",
        ["Generic"]    = "daeanne",
        ["Diagnostic"] = "daeanne"
    };

    public string? GetAgentName(AgentTaskType type) =>
        AgentMap.TryGetValue(type.ToString(), out var name) ? name : null;
}
