using System.Text.Json;

namespace Daeanne.Tray;

/// <summary>
/// Pure static utilities shared across the dashboard: duration formatting,
/// relative time display, work-directory resolution, and JSON options.
/// </summary>
internal static class DashboardHelpers
{
    internal static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    internal static string FormatDuration(TimeSpan ts) =>
        ts.TotalHours   >= 1 ? $"{ts.Hours}h {ts.Minutes}m" :
        ts.TotalMinutes >= 1 ? $"{(int)ts.TotalMinutes}m {ts.Seconds}s" :
                               $"{ts.Seconds}s";

    internal static string FormatRelative(DateTime? utc)
    {
        if (utc is null) return "—";
        var diff = utc.Value - DateTime.UtcNow;
        if (diff.TotalSeconds < 0)  return "overdue";
        if (diff.TotalMinutes < 60) return $"in {(int)diff.TotalMinutes}m";
        if (diff.TotalHours   < 24) return $"in {(int)diff.TotalHours}h";
        return $"in {(int)diff.TotalDays}d";
    }

    internal static string? ResolveWorkDir(TaskSummary? t)
    {
        if (t?.WorkDir is { } dir && Directory.Exists(dir)) return dir;
        if (t?.Id is not { Length: > 0 } idStr || !Guid.TryParse(idStr, out var id)) return null;
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".daeanne", "tasks");
        return FindTaskDirLocal(baseDir, id);
    }

    private static string? FindTaskDirLocal(string baseDir, Guid id)
    {
        string[] candidates =
        [
            Path.Combine(baseDir, "active",              id.ToString()),
            Path.Combine(baseDir, "complete",            id.ToString()),
            Path.Combine(baseDir, "failed",              id.ToString()),
            Path.Combine(baseDir, "complete", "archive", id.ToString()),
            Path.Combine(baseDir, "scheduled", "active",   id.ToString()),
            Path.Combine(baseDir, "scheduled", "complete", id.ToString()),
            Path.Combine(baseDir, "scheduled", "failed",   id.ToString()),
        ];
        return candidates.FirstOrDefault(Directory.Exists);
    }
}
