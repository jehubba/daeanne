using System.Threading.Channels;
using Daeanne.Dispatcher.Data;
using Daeanne.Dispatcher.Endpoints;
using Daeanne.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Daeanne.Dispatcher.Services;

/// <summary>
/// Background service that drains the dispatch queue and launches agents.
/// The SQLite DB is the source of truth — Channel&lt;Guid&gt; is a wake-up signal only.
/// On startup, all Pending or Running tasks are requeued so no work is lost across restarts.
/// </summary>
public class DispatchWorker(
    Channel<Guid> queue,
    IAgentDispatcher dispatcher,
    IServiceScopeFactory scopeFactory,
    IOptions<DispatchConfig> config,
    ILogger<DispatchWorker> logger) : BackgroundService
{
    private readonly DispatchConfig _config = config.Value;
    private readonly SemaphoreSlim _semaphore = new(config.Value.MaxConcurrency);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RehydrateAsync(stoppingToken);

        logger.LogInformation("DispatchWorker ready (max concurrency: {Max})", _config.MaxConcurrency);

        await foreach (var taskId in queue.Reader.ReadAllAsync(stoppingToken))
        {
            await _semaphore.WaitAsync(stoppingToken);
            // Fire and forget — semaphore is released inside ProcessTaskAsync
            _ = ProcessTaskAsync(taskId, stoppingToken);
        }
    }

    private async Task ProcessTaskAsync(Guid taskId, CancellationToken ct)
    {
        try
        {
            AgentTask? task;

            // Mark Running (or skip if no agent handles this type)
            using (var scope = scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DispatcherDbContext>();
                task = await db.Tasks.FindAsync([taskId], ct);

                if (task is null || task.IsTerminal())
                {
                    logger.LogDebug("Task {TaskId} skipped (null or already terminal).", taskId);
                    return;
                }

                // Types with no agent configured stay Pending for manual pickup (e.g. by Daeanne session)
                if (_config.GetAgentName(task.Type) is null)
                {
                    logger.LogDebug("Task {TaskId} ({Type}) has no auto-dispatch agent — leaving Pending.",
                        taskId, task.Type);
                    return;
                }

                task.Status = AgentTaskStatus.Running;
                task.StartedAt = DateTime.UtcNow;
                task.UpdatedAt = DateTime.UtcNow;
                task.AttemptCount++;
                await db.SaveChangesAsync(ct);
            }

            // Dispatch (DB context intentionally NOT held during long-running process)
            var result = await dispatcher.DispatchAsync(task, ct);

            if (result.Succeeded)
            {
                // Process exited cleanly — do NOT mark completion. That is Daeanne's responsibility.
                // She will call PATCH /tasks/{id}/status → Succeeded when her work is truly done.
                // The dir stays in active/ until the TaskCleanupWorker moves it after her signal.
                // However, persist stdout as fallback ResultJson now so the Note column is populated
                // even if the agent's PATCH omits resultJson.
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DispatcherDbContext>();
                var t = await db.Tasks.FindAsync([taskId], ct);
                if (t?.Status == AgentTaskStatus.Awaiting)
                {
                    logger.LogInformation("Task {TaskId} process exited but status is Awaiting — leaving suspended.", taskId);
                }
                else
                {
                    if (t is not null && t.ResultJson is null && result.ResultJson is not null)
                    {
                        t.ResultJson = result.ResultJson;
                        t.UpdatedAt  = DateTime.UtcNow;
                        await db.SaveChangesAsync(ct);
                    }
                    logger.LogInformation("Task {TaskId} process exited cleanly — awaiting Daeanne's self-report via PATCH.", taskId);
                }
            }
            else
            {
                // Process crashed, timed out, or returned non-zero exit — definitive failure.
                // Auth errors are parked as Blocked so the user can promote after /login.
                // DispatchWorker is authoritative for error states; move dir immediately (except for Blocked).
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DispatcherDbContext>();
                var t = await db.Tasks.FindAsync([taskId], ct);

                if (t is not null && !t.IsTerminal())
                {
                    AgentTaskStatus finalStatus;
                    if (result.IsAuthError)
                        finalStatus = AgentTaskStatus.Blocked;
                    else if (result.Error?.Contains("timed out") == true)
                        finalStatus = AgentTaskStatus.TimedOut;
                    else
                        finalStatus = AgentTaskStatus.Failed;

                    // Blocked tasks keep their active/ dir — they can be promoted and retried.
                    // Failed/TimedOut tasks move to their final location.
                    if (finalStatus != AgentTaskStatus.Blocked)
                    {
                        var newWorkDir = TaskDirManager.MoveToFinalLocation(
                            _config.ResolvedWorkDir, taskId, finalStatus, t.IsScheduled, logger);
                        t.ResultJson = TaskDirManager.UpdateResultJsonWorkDir(t.ResultJson, newWorkDir);
                    }

                    var errorMsg = result.IsAuthError
                        ? "Copilot auth expired — run /login in this Copilot CLI session, then promote this task to Pending."
                        : result.Error;

                    t.Status      = finalStatus;
                    t.Error       = errorMsg;
                    t.CompletedAt = DateTime.UtcNow;
                    t.UpdatedAt   = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);

                    logger.LogWarning("Task {TaskId} → {Status}: {Error}", taskId, finalStatus, errorMsg);

                    if (t.ParentTaskId.HasValue)
                        await TaskEndpoints.TriggerParentResumeAsync(t, db, queue, _config, logger);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unhandled error processing task {TaskId}", taskId);
            await TryMarkFailedAsync(taskId, ex.Message, ct);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// On startup: re-enqueue Pending tasks (never started), mark interrupted
    /// Running tasks as Failed, migrate any legacy flat task dirs to subfolder layout,
    /// and archive completed tasks older than 30 days.
    /// </summary>
    private async Task RehydrateAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DispatcherDbContext>();

        // Re-queue tasks that never started
        var pendingIds = await db.Tasks
            .Where(t => t.Status == AgentTaskStatus.Pending)
            .Select(t => t.Id)
            .ToListAsync(ct);

        logger.LogInformation("RehydrateAsync: found {Count} Pending tasks to re-queue: [{Ids}]",
            pendingIds.Count, string.Join(", ", pendingIds.Select(id => id.ToString()[..8])));

        foreach (var id in pendingIds)
            await queue.Writer.WriteAsync(id, ct);

        // Mark interrupted Running tasks as Failed (or resume them if a session exists)
        var interrupted = await db.Tasks
            .Where(t => t.Status == AgentTaskStatus.Running)
            .ToListAsync(ct);

        foreach (var t in interrupted)
        {
            var workDir = TaskDirManager.FindTaskDir(_config.ResolvedWorkDir, t.Id)
                          ?? TaskDirManager.ActivePath(_config.ResolvedWorkDir, t.Id);

            // Try to resume before giving up
            var resumeResult = await dispatcher.TryResumeAsync(t, workDir, ct);
            if (resumeResult is not null)
            {
                var finalStatus = resumeResult.Succeeded ? AgentTaskStatus.Succeeded : AgentTaskStatus.Failed;
                var newWorkDir  = TaskDirManager.MoveToFinalLocation(_config.ResolvedWorkDir, t.Id, finalStatus, t.IsScheduled, logger);
                t.Status     = finalStatus;
                t.ResultJson = TaskDirManager.UpdateResultJsonWorkDir(resumeResult.ResultJson, newWorkDir);
                t.Error      = resumeResult.Error;
                t.CompletedAt = DateTime.UtcNow;
                t.UpdatedAt   = DateTime.UtcNow;
                logger.LogInformation("Resumed interrupted task {Id} → {Status}", t.Id, finalStatus);
            }
            else
            {
                // No session.md — can't resume, mark Failed and move dir
                var newWorkDir = TaskDirManager.MoveToFinalLocation(_config.ResolvedWorkDir, t.Id, AgentTaskStatus.Failed, t.IsScheduled, logger);
                t.Status      = AgentTaskStatus.Failed;
                t.Error       = "Dispatcher restarted while task was Running; no session found for resume.";
                t.CompletedAt = DateTime.UtcNow;
                t.UpdatedAt   = DateTime.UtcNow;
                t.ResultJson  = TaskDirManager.UpdateResultJsonWorkDir(t.ResultJson, newWorkDir);
                logger.LogWarning("Task {Id} had no session.md — marked Failed (no resume possible)", t.Id);
            }
        }

        if (interrupted.Count > 0)
            await db.SaveChangesAsync(ct);

        // Migrate legacy flat-layout dirs (tasks/{id}/ → tasks/{status}/{id}/)
        var allTasks = await db.Tasks
            .Select(t => new { t.Id, t.Status })
            .ToListAsync(ct);

        TaskDirManager.MigrateFlat(
            _config.ResolvedWorkDir,
            allTasks.Select(t => (t.Id, t.Status)),
            logger);

        // Archive completed tasks older than 30 days
        TaskDirManager.ArchiveOld(_config.ResolvedWorkDir, archiveDays: 30, logger);

        if (pendingIds.Count > 0 || interrupted.Count > 0)
            logger.LogInformation(
                "Rehydrated: {Pending} pending re-queued, {Interrupted} running → Failed.",
                pendingIds.Count, interrupted.Count);

        var awaitingCount = await db.Tasks.CountAsync(t => t.Status == AgentTaskStatus.Awaiting, ct);
        if (awaitingCount > 0)
            logger.LogInformation(
                "Rehydrated: {Count} Awaiting task(s) found — suspended pending sub-task callbacks.",
                awaitingCount);
    }

    private async Task TryMarkFailedAsync(Guid taskId, string error, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DispatcherDbContext>();
            var t = await db.Tasks.FindAsync([taskId], ct);
            if (t is not null && !t.IsTerminal())
            {
                t.Status = AgentTaskStatus.Failed;
                t.Error = error;
                t.CompletedAt = DateTime.UtcNow;
                t.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to mark task {TaskId} as failed.", taskId);
        }
    }
}
