using System.Threading.Channels;
using Daeanne.Dispatcher.Data;
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

            // Determine final status
            AgentTaskStatus finalStatus = result.Succeeded
                ? AgentTaskStatus.Succeeded
                : (ct.IsCancellationRequested ? AgentTaskStatus.Failed : AgentTaskStatus.Failed);

            // Check if the failure was a timeout (error message indicates it)
            if (!result.Succeeded && result.Error?.Contains("timed out") == true)
                finalStatus = AgentTaskStatus.TimedOut;

            // Save result
            using (var scope = scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DispatcherDbContext>();
                var t = await db.Tasks.FindAsync([taskId], ct);

                if (t is not null && !t.IsTerminal())
                {
                    t.Status = finalStatus;
                    t.ResultJson = result.ResultJson;
                    t.Error = result.Error;
                    t.CompletedAt = DateTime.UtcNow;
                    t.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);

                    logger.LogInformation("Task {TaskId} → {Status}", taskId, finalStatus);
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

    /// <summary>On startup, re-enqueue any tasks that were interrupted by a previous shutdown.</summary>
    private async Task RehydrateAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DispatcherDbContext>();

        var stalledIds = await db.Tasks
            .Where(t => t.Status == AgentTaskStatus.Pending || t.Status == AgentTaskStatus.Running)
            .Select(t => t.Id)
            .ToListAsync(ct);

        foreach (var id in stalledIds)
            await queue.Writer.WriteAsync(id, ct);

        if (stalledIds.Count > 0)
            logger.LogInformation("Rehydrated {Count} stalled task(s) from DB.", stalledIds.Count);
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
