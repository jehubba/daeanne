using System.Text.Json;
using Daeanne.Shared.Models;

namespace Daeanne.Dispatcher.Services;

/// <summary>
/// Manages the physical layout of per-task directories under the Dispatcher's workDir.
///
/// Layout:
///   {baseDir}/active/{id}/       — Pending or Running
///   {baseDir}/complete/{id}/     — Succeeded (≤ archiveDays old)
///   {baseDir}/complete/archive/{id}/ — Succeeded (> archiveDays old)
///   {baseDir}/failed/{id}/       — Failed or TimedOut
///
/// The Dispatcher moves task dirs at status transitions and keeps resultJson.workDir
/// in the DB in sync so callers always get the correct current path.
/// </summary>
public static class TaskDirManager
{
    // ─── Path helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Layout:
    ///   Non-scheduled:  {baseDir}/active/{id}/
    ///                   {baseDir}/complete/{id}/
    ///                   {baseDir}/failed/{id}/
    ///                   {baseDir}/complete/archive/{id}/
    ///   Scheduled:      {baseDir}/scheduled/active/{id}/
    ///                   {baseDir}/scheduled/complete/{id}/
    ///                   {baseDir}/scheduled/failed/{id}/
    ///                   {baseDir}/scheduled/complete/archive/{id}/
    /// </summary>
    private static string Ns(string baseDir, bool isScheduled) =>
        isScheduled ? Path.Combine(baseDir, "scheduled") : baseDir;

    public static string ActivePath(string baseDir, Guid id, bool isScheduled = false) =>
        Path.Combine(Ns(baseDir, isScheduled), "active", id.ToString());

    public static string CompletePath(string baseDir, Guid id, bool isScheduled = false) =>
        Path.Combine(Ns(baseDir, isScheduled), "complete", id.ToString());

    public static string FailedPath(string baseDir, Guid id, bool isScheduled = false) =>
        Path.Combine(Ns(baseDir, isScheduled), "failed", id.ToString());

    public static string ArchivePath(string baseDir, Guid id, bool isScheduled = false) =>
        Path.Combine(Ns(baseDir, isScheduled), "complete", "archive", id.ToString());

    /// <summary>Returns the target directory path for a task with the given final status.</summary>
    public static string PathForStatus(string baseDir, Guid id, AgentTaskStatus status, bool isScheduled = false) =>
        status switch
        {
            AgentTaskStatus.Succeeded or AgentTaskStatus.Escalated => CompletePath(baseDir, id, isScheduled),
            AgentTaskStatus.Failed or AgentTaskStatus.TimedOut     => FailedPath(baseDir, id, isScheduled),
            _                                                       => ActivePath(baseDir, id, isScheduled)
        };

    /// <summary>
    /// Searches all known locations for the task's directory.
    /// Returns null if the directory doesn't exist anywhere.
    /// </summary>
    public static string? FindTaskDir(string baseDir, Guid id)
    {
        var candidates = new[]
        {
            // Non-scheduled paths
            ActivePath(baseDir, id),
            CompletePath(baseDir, id),
            FailedPath(baseDir, id),
            ArchivePath(baseDir, id),
            // Scheduled paths
            ActivePath(baseDir, id, isScheduled: true),
            CompletePath(baseDir, id, isScheduled: true),
            FailedPath(baseDir, id, isScheduled: true),
            ArchivePath(baseDir, id, isScheduled: true),
            // Legacy flat layout
            Path.Combine(baseDir, id.ToString())
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    // ─── Moves ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Moves the task directory to the location appropriate for <paramref name="finalStatus"/>.
    /// Returns the new path (whether or not a move was needed or possible).
    /// Safe to call even if the source dir doesn't exist.
    /// </summary>
    public static string MoveToFinalLocation(string baseDir, Guid id, AgentTaskStatus finalStatus,
        bool isScheduled = false, ILogger? logger = null)
    {
        var targetPath = PathForStatus(baseDir, id, finalStatus, isScheduled);
        var sourcePath = FindTaskDir(baseDir, id);

        if (sourcePath is null || sourcePath == targetPath)
            return targetPath;

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        // Retry a few times — on Windows the agent process may still hold the dir
        // as its working directory immediately after posting its result.
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                Directory.Move(sourcePath, targetPath);
                return targetPath;
            }
            catch (Exception ex) when (attempt < 4)
            {
                logger?.LogWarning("TaskDirManager: move attempt {Attempt} failed for {Id}: {Msg} — retrying",
                    attempt, id, ex.Message);
                Thread.Sleep(attempt * 500);
            }
            catch (Exception ex)
            {
                logger?.LogError("TaskDirManager: could not move {Id} to {Target} after 4 attempts: {Msg}",
                    id, targetPath, ex.Message);
                return sourcePath;   // report actual location, not intended target
            }
        }

        return targetPath;
    }

    // ─── Archive ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Moves completed task dirs older than <paramref name="archiveDays"/> into
    /// complete/archive/. Handles both root and scheduled/ namespaces.
    /// Runs on startup and daily.
    /// </summary>
    public static void ArchiveOld(string baseDir, int archiveDays = 30, ILogger? logger = null)
    {
        ArchiveInNamespace(baseDir, archiveDays, logger);
        ArchiveInNamespace(Path.Combine(baseDir, "scheduled"), archiveDays, logger);
    }

    private static void ArchiveInNamespace(string ns, int archiveDays, ILogger? logger)
    {
        var completePath = Path.Combine(ns, "complete");
        if (!Directory.Exists(completePath)) return;

        var cutoff      = DateTime.UtcNow.AddDays(-archiveDays);
        var archiveRoot = Path.Combine(completePath, "archive");

        foreach (var dir in Directory.GetDirectories(completePath))
        {
            // Skip the archive subdir itself
            if (string.Equals(Path.GetFileName(dir), "archive", StringComparison.OrdinalIgnoreCase))
                continue;

            if (Directory.GetLastWriteTimeUtc(dir) < cutoff)
            {
                var dest = Path.Combine(archiveRoot, Path.GetFileName(dir));
                try
                {
                    Directory.CreateDirectory(archiveRoot);
                    Directory.Move(dir, dest);
                    logger?.LogInformation("TaskDirManager: archived task dir {Name}", Path.GetFileName(dir));
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "TaskDirManager: could not archive {Dir}", dir);
                }
            }
        }
    }

    // ─── Migration ────────────────────────────────────────────────────────────

    /// <summary>
    /// One-time migration: moves legacy flat-layout task dirs (tasks/{id}/) into the
    /// correct subfolder based on current status. Safe to call repeatedly — skips
    /// dirs that are already in the right place.
    /// </summary>
    public static void MigrateFlat(
        string baseDir,
        IEnumerable<(Guid Id, AgentTaskStatus Status)> tasks,
        ILogger? logger = null)
    {
        var moved = 0;
        foreach (var (id, status) in tasks)
        {
            var flatPath = Path.Combine(baseDir, id.ToString());
            if (!Directory.Exists(flatPath)) continue;

            var target = PathForStatus(baseDir, id, status);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                Directory.Move(flatPath, target);
                moved++;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "TaskDirManager: could not migrate {Id}", id);
            }
        }

        if (moved > 0)
            logger?.LogInformation("TaskDirManager: migrated {N} flat task dirs to subfolder layout.", moved);
    }

    // ─── Plan doc parsing ─────────────────────────────────────────────────────

    /// <summary>
    /// Reads daeanne-plan.md from the task's active directory (or wherever FindTaskDir finds it)
    /// and returns the agent's self-reported status and response text.
    /// Returns null if no plan doc exists or it has no parseable frontmatter status.
    /// </summary>
    public static PlanDocResult? ReadPlanDoc(string baseDir, Guid id)
    {
        var taskDir = FindTaskDir(baseDir, id);
        if (taskDir is null) return null;

        var planPath = Path.Combine(taskDir, "daeanne-plan.md");
        if (!File.Exists(planPath)) return null;

        try
        {
            var lines = File.ReadAllLines(planPath);

            // Parse YAML frontmatter (between first two --- lines)
            string? status = null;
            var inFrontmatter = false;
            var fmEnd = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (i == 0 && line == "---") { inFrontmatter = true; continue; }
                if (inFrontmatter && line == "---") { fmEnd = i; break; }
                if (inFrontmatter && line.StartsWith("status:", StringComparison.OrdinalIgnoreCase))
                    status = line["status:".Length..].Trim().ToLowerInvariant();
            }

            if (status is null) return null;

            var agentStatus = status switch
            {
                "complete" or "completed" or "succeeded" or "success" => AgentTaskStatus.Succeeded,
                "failed"   or "failure"                               => AgentTaskStatus.Failed,
                "partial"                                             => AgentTaskStatus.Partial,
                _                                                     => (AgentTaskStatus?)null
            };

            if (agentStatus is null) return null;

            // Extract the ## Result section for a response summary
            string? response = null;
            var resultSection = false;
            var resultLines = new List<string>();
            for (int i = fmEnd + 1; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("## Result", StringComparison.OrdinalIgnoreCase))
                    { resultSection = true; continue; }
                if (resultSection && lines[i].StartsWith("## "))
                    break;
                if (resultSection && !string.IsNullOrWhiteSpace(lines[i]))
                    resultLines.Add(lines[i].TrimStart('#', ' ', '*'));
            }
            if (resultLines.Count > 0)
                response = string.Join(" ", resultLines).Trim();
            if (string.IsNullOrWhiteSpace(response)) response = null;

            return new PlanDocResult(agentStatus.Value, response);
        }
        catch
        {
            return null;
        }
    }

    public record PlanDocResult(AgentTaskStatus Status, string? Response);



    /// <summary>
    /// If <paramref name="resultJson"/> has an empty or absent "response" field,
    /// replaces it with <paramref name="response"/>. Otherwise leaves it alone.
    /// </summary>
    public static string MergeResponseIntoResultJson(string resultJson, string response)
    {
        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            if (doc.RootElement.TryGetProperty("response", out var existing) &&
                !string.IsNullOrWhiteSpace(existing.GetString()))
                return resultJson;

            var dict = new Dictionary<string, object?>();
            foreach (var prop in doc.RootElement.EnumerateObject())
                dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() : (object?)prop.Value.GetRawText();
            dict["response"] = response;
            return JsonSerializer.Serialize(dict);
        }
        catch { return resultJson; }
    }

    /// <summary>
    /// Returns a copy of <paramref name="resultJson"/> with the "workDir" and "sessionLog"
    /// fields updated to reflect <paramref name="newWorkDir"/>.
    /// Returns the original string unchanged if parsing fails.
    /// </summary>
    public static string? UpdateResultJsonWorkDir(string? resultJson, string newWorkDir)
    {
        if (string.IsNullOrWhiteSpace(resultJson)) return resultJson;

        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            var dict = new Dictionary<string, JsonElement>();
            foreach (var prop in doc.RootElement.EnumerateObject())
                dict[prop.Name] = prop.Value.Clone();

            // Rebuild with updated paths
            var updated = new Dictionary<string, object?>();
            foreach (var kv in dict)
                updated[kv.Key] = (object?)kv.Value.ValueKind switch
                {
                    JsonValueKind.String => kv.Value.GetString(),
                    JsonValueKind.Number => kv.Value.GetDouble(),
                    JsonValueKind.True   => (object?)true,
                    JsonValueKind.False  => false,
                    JsonValueKind.Null   => null,
                    _                    => kv.Value.GetRawText()
                };

            updated["workDir"]    = newWorkDir;
            updated["sessionLog"] = Path.Combine(newWorkDir, "session.md");

            return JsonSerializer.Serialize(updated);
        }
        catch
        {
            return resultJson;
        }
    }
}
