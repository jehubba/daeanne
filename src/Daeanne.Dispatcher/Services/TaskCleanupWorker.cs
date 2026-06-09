using System.Text.Json;
using System.Threading.Channels;
using Daeanne.Dispatcher.Data;
using Daeanne.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Daeanne.Dispatcher.Services;

/// <summary>
/// Runs on a schedule to move completed task directories out of active/ and
/// report on any anomalies (dirs that couldn't be moved) to Daeanne.
///
/// Design principles:
///   - Only DispatchWorker marks failures (process crash / timeout).
///   - Only Daeanne marks successes (via PATCH /tasks/{id}/status).
///   - This worker is purely a janitor: it defers dir moves so file locks have
///     time to release, then asks Daeanne to investigate anything left behind.
/// </summary>
public class TaskCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DispatchConfig> config,
    ILogger<TaskCleanupWorker> logger) : BackgroundService
{
    // How long after startup before the first cleanup run.
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);

    // How long between cleanup cycles (default 60 min — overridable via config).
    private TimeSpan Interval =>
        TimeSpan.FromMinutes(config.Value.CleanupIntervalMinutes > 0
            ? config.Value.CleanupIntervalMinutes : 60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let everything settle before the first run.
        await Task.Delay(InitialDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCleanupCycleAsync(stoppingToken);
            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task RunCleanupCycleAsync(CancellationToken ct)
    {
        try
        {
            var workDir   = config.Value.ResolvedWorkDir;
            var activeDir = Path.Combine(workDir, "active");

            if (!Directory.Exists(activeDir))
            {
                logger.LogDebug("TaskCleanupWorker: active/ dir does not exist — nothing to do.");
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var db      = scope.ServiceProvider.GetRequiredService<DispatcherDbContext>();
            var channel = scope.ServiceProvider.GetRequiredService<Channel<Guid>>();

            // ── Phase 1: move terminal-status dirs out of active/ ──────────────
            var activeFolderIds = Directory.GetDirectories(activeDir)
                .Select(d => Path.GetFileName(d))
                .Where(n => Guid.TryParse(n, out _))
                .Select(n => Guid.Parse(n!))
                .ToList();

            var moved = 0;
            foreach (var id in activeFolderIds)
            {
                var task = await db.Tasks
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == id, ct);

                if (task is null || !task.IsTerminal()) continue;

                var destDir = TaskDirManager.MoveToFinalLocation(
                    workDir, id, task.Status, task.IsScheduled, logger);

                if (!destDir.Contains(Path.Combine(workDir, "active"), StringComparison.OrdinalIgnoreCase))
                    moved++;
            }

            if (moved > 0)
                logger.LogInformation("TaskCleanupWorker: moved {N} terminal task dir(s) out of active/.", moved);

            // ── Phase 2: find what is still in active/ ─────────────────────────
            var remaining = Directory.Exists(activeDir)
                ? Directory.GetDirectories(activeDir)
                    .Select(d => Path.GetFileName(d))
                    .Where(n => Guid.TryParse(n, out _))
                    .Select(n => Guid.Parse(n!))
                    .ToList()
                : new List<Guid>();

            if (remaining.Count == 0)
            {
                logger.LogInformation("TaskCleanupWorker: active/ is clean after cycle.");
                return;
            }

            // Separate in-flight (expected) from anomalies (unexpected)
            var inFlight  = new List<(Guid Id, string Status)>();
            var anomalies = new List<AnomalyEntry>();

            foreach (var id in remaining)
            {
                var task = await db.Tasks
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == id, ct);

                if (task is null)
                {
                    anomalies.Add(new AnomalyEntry(id, "NO_DB_ROW", "unknown", DateTime.UtcNow));
                    continue;
                }

                if (task.Status is AgentTaskStatus.Running
                                 or AgentTaskStatus.Awaiting
                                 or AgentTaskStatus.Pending)
                {
                    inFlight.Add((id, task.Status.ToString()));
                    continue;
                }

                // Terminal but still in active/ — dir move failed (likely file lock)
                anomalies.Add(new AnomalyEntry(
                    id,
                    task.Status.ToString(),
                    task.Type.ToString(),
                    task.CreatedAt));
            }

            logger.LogInformation(
                "TaskCleanupWorker: {Total} dir(s) remain in active/ — {InFlight} in-flight, {Anomalies} anomalies.",
                remaining.Count, inFlight.Count, anomalies.Count);

            if (anomalies.Count == 0) return;

            // ── Phase 3: dispatch sit-rep to Daeanne ───────────────────────────
            var contextJson = JsonSerializer.Serialize(new
            {
                remainingActiveDirs = anomalies.Select(a => new
                {
                    id      = a.Id.ToString(),
                    status  = a.Status,
                    type    = a.Type,
                    created = a.CreatedAt.ToString("u")
                }),
                inFlightCount = inFlight.Count,
                cleanupTime   = DateTime.UtcNow.ToString("u")
            });

            var prompt =
                $"SITUATION REPORT — {anomalies.Count} task dir(s) remain in active/ after scheduled cleanup. " +
                $"These tasks are in terminal DB state but their directories were not moved. " +
                $"Investigate each GUID in context.remainingActiveDirs: " +
                $"if the task is actually complete, call PATCH /tasks/{{id}}/status to close it; " +
                $"if it appears stuck or anomalous, dispatch a separate Diagnostic sub-task (non-blocking). " +
                $"Do NOT block on sub-task results — fire and return your sit-rep summary.";

            var sitRep = new AgentTask
            {
                Id          = Guid.NewGuid(),
                Type        = AgentTaskType.SitRep,
                Prompt      = prompt,
                ContextJson = contextJson,
                Status      = AgentTaskStatus.Pending,
                CreatedAt   = DateTime.UtcNow,
                UpdatedAt   = DateTime.UtcNow
            };

            db.Tasks.Add(sitRep);
            await db.SaveChangesAsync(ct);
            await channel.Writer.WriteAsync(sitRep.Id, ct);

            logger.LogInformation(
                "TaskCleanupWorker: dispatched SitRep task {Id} covering {N} anomalous dir(s).",
                sitRep.Id, anomalies.Count);
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "TaskCleanupWorker: cleanup cycle failed unexpectedly.");
        }
    }

    private record AnomalyEntry(Guid Id, string Status, string Type, DateTime CreatedAt);
}
