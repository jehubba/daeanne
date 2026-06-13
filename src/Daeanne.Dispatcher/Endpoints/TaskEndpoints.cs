using System.Text.Json;
using System.Threading.Channels;
using System.Threading.RateLimiting;
using Daeanne.Dispatcher.Data;
using Daeanne.Dispatcher.Services;
using Daeanne.Shared.Models;
using Daeanne.Shared.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Daeanne.Dispatcher.Endpoints;

public static class TaskEndpoints
{
    // Task types that originate externally — subject to inbound rate limiting.
    // All other types (SitRep, Diagnostic, scheduled, sub-tasks, etc.) bypass the limiter.
    private static readonly HashSet<AgentTaskType> ExternalInboundTypes =
        [AgentTaskType.Email, AgentTaskType.InboundSms];
    public static void MapTaskEndpoints(this WebApplication app)
    {
        app.MapGet("/tasks",                         GetTasks);
        app.MapGet("/tasks/{id:guid}",               GetTask);
        app.MapPost("/tasks",                        CreateTask);
        app.MapPost("/tasks/{id:guid}/result",       PostResult);
        app.MapPatch("/tasks/{id:guid}/status",      PostResult);   // alias — agents use PATCH
        app.MapPost("/tasks/{id:guid}/requeue",      RequeueTask);
        app.MapPost("/tasks/{id:guid}/resume",       ResumeTask);
        app.MapPost("/tasks/{id:guid}/callback/ack", CallbackAck);
        app.MapPost("/tasks/{id:guid}/callback",     Callback);
    }

    private static async Task<IResult> GetTasks(
        DispatcherDbContext db,
        string? status,
        string? type,
        int take = 50,
        int skip = 0)
    {
        var query = db.Tasks.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<AgentTaskStatus>(status, ignoreCase: true, out var parsed))
        {
            query = query.Where(t => t.Status == parsed);
        }

        if (!string.IsNullOrWhiteSpace(type) &&
            Enum.TryParse<AgentTaskType>(type, ignoreCase: true, out var parsedType))
        {
            query = query.Where(t => t.Type == parsedType);
        }

        var tasks = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip(skip)
            .Take(Math.Min(take, 200))
            .ToListAsync();

        return Results.Ok(tasks);
    }

    private static async Task<IResult> GetTask(Guid id, DispatcherDbContext db)
    {
        var task = await db.Tasks.FindAsync(id);
        return task is null ? Results.NotFound() : Results.Ok(task);
    }

    /// <summary>
    /// Force-requeue a Pending task that was not caught by RehydrateAsync on startup.
    /// Safe to call multiple times — idempotent if the task is already dispatching.
    /// </summary>
    private static async Task<IResult> RequeueTask(
        Guid id,
        DispatcherDbContext db,
        Channel<Guid> queue,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var task = await db.Tasks.FindAsync(id);
        if (task is null) return Results.NotFound();
        if (task.Status != AgentTaskStatus.Pending)
            return Results.Conflict($"Task {id} is in state '{task.Status}', not Pending. Only Pending tasks can be requeued.");

        await queue.Writer.WriteAsync(task.Id, ct);
        logger.LogInformation("Task {Id} manually re-queued via /requeue.", id);
        return Results.Accepted();
    }

    private static async Task<IResult> CreateTask(
        CreateTaskRequest request,
        DispatcherDbContext db,
        Channel<Guid> queue,
        IOptions<DispatchConfig> dispatchConfig,
        IServiceScopeFactory scopeFactory,
        RateLimiter rateLimiter,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return Results.BadRequest("Prompt is required.");

        // Rate-limit external inbound types only — internal orchestration bypasses this.
        if (ExternalInboundTypes.Contains(request.Type))
        {
            var lease = await rateLimiter.AcquireAsync(permitCount: 1, cancellationToken: ct);
            if (!lease.IsAcquired)
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        // Validate InitialStatus if provided — only dormant states are valid here
        if (request.InitialStatus.HasValue)
        {
            if (!AgentTask.DormantStatuses.Contains(request.InitialStatus.Value))
                return Results.BadRequest(
                    $"InitialStatus must be one of: {string.Join(", ", AgentTask.DormantStatuses)}. " +
                    $"Use the default (omit InitialStatus) to create a task that dispatches immediately.");
        }

        // Validate DependsOnTaskId — resolve initial status based on upstream state
        if (request.DependsOnTaskId.HasValue)
        {
            var upstream = await db.Tasks.FindAsync([request.DependsOnTaskId.Value], ct);
            if (upstream is null)
                return Results.BadRequest($"DependsOnTaskId {request.DependsOnTaskId} does not exist.");

            // If upstream is already Succeeded, treat as no dependency — start immediately
            // If upstream is not yet Succeeded, force Blocked regardless of InitialStatus
            if (upstream.Status != AgentTaskStatus.Succeeded)
                request.InitialStatus = AgentTaskStatus.Blocked;
            else
                request.DependsOnTaskId = null; // upstream done — no need to track
        }

        // Idempotency: if a non-terminal task with this correlationId already exists, return it
        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            var existing = await db.Tasks.FirstOrDefaultAsync(t =>
                t.CorrelationId == request.CorrelationId, ct);
            if (existing is not null)
                return Results.Conflict(existing);
        }

        // Merge GraphMessageId and SenderPhone into ContextJson so the agent sees them in its prompt
        var contextJson = request.ContextJson;
        var extras = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(request.GraphMessageId)) extras["graphMessageId"] = request.GraphMessageId;
        if (!string.IsNullOrWhiteSpace(request.SenderPhone))    extras["senderPhone"]    = request.SenderPhone;
        if (extras.Count > 0)
        {
            var ctx = string.IsNullOrWhiteSpace(contextJson)
                ? new Dictionary<string, string?>()
                : JsonSerializer.Deserialize<Dictionary<string, string?>>(contextJson) ?? new();
            foreach (var (k, v) in extras) ctx[k] = v;
            contextJson = JsonSerializer.Serialize(ctx);
        }

        // If this is a sub-task, auto-suspend the parent — no separate /await call needed
        if (request.ParentTaskId.HasValue)
        {
            var parent = await db.Tasks.FindAsync([request.ParentTaskId.Value], ct);
            if (parent is not null && parent.Status == AgentTaskStatus.Running)
            {
                parent.Status    = AgentTaskStatus.Awaiting;
                parent.UpdatedAt = DateTime.UtcNow;
            }
        }

        var task = new AgentTask
        {
            Type           = request.Type,
            Prompt         = request.Prompt,
            ContextJson    = contextJson,
            CorrelationId  = request.CorrelationId,
            IsScheduled    = request.IsScheduled,
            ScheduledJobId = request.ScheduledJobId,
            ParentTaskId   = request.ParentTaskId,
            SessionName    = request.SessionName,
            DependsOnTaskId = request.DependsOnTaskId,
            Status         = request.InitialStatus ?? AgentTaskStatus.Pending
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);

        if (request.InitialStatus.HasValue)
        {
            // Dormant task: create directory under pending/blocked/future/ but do NOT enqueue.
            var dormantDir = TaskDirManager.PathForStatus(
                dispatchConfig.Value.ResolvedWorkDir, task.Id, task.Status, task.IsScheduled);
            Directory.CreateDirectory(dormantDir);

            return Results.Created($"/tasks/{task.Id}", task);
        }

        await queue.Writer.WriteAsync(task.Id, ct);

        // Non-sub-tasks: standard 201 Created
        if (!request.ParentTaskId.HasValue)
            return Results.Created($"/tasks/{task.Id}", task);

        // Sub-tasks: hold the response until the agent acks (or 30s timeout)
        // 202 Accepted  = agent started and acknowledged the callback URL
        // 201 Created   = task queued but agent hasn't acked yet (caller should monitor)
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(500, ct);
            using var scope = scopeFactory.CreateScope();
            var freshDb = scope.ServiceProvider.GetRequiredService<DispatcherDbContext>();
            var fresh   = await freshDb.Tasks.FindAsync([task.Id], ct);
            if (fresh?.CallbackAcknowledgedAt is not null)
                return Results.Accepted($"/tasks/{task.Id}", task);
        }

        return Results.Created($"/tasks/{task.Id}", task);
    }

    private static async Task<IResult> PostResult(
        Guid id,
        PostTaskResultRequest request,
        DispatcherDbContext db,
        Channel<Guid> queue,
        IOptions<DispatchConfig> dispatchConfig,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var task = await db.Tasks.FindAsync(id);
        if (task is null) return Results.NotFound();

        if (!Enum.TryParse<AgentTaskStatus>(request.Status, ignoreCase: true, out var newStatus))
            return Results.BadRequest($"'{request.Status}' is not a valid AgentTaskStatus.");

        // ── Dormant → Pending promotion path ────────────────────────────────
        // A dormant task (Deferred/Blocked/Future) is promoted by patching status to Pending.
        // This moves the task dir to active/ and enqueues it for dispatch.
        // Alternatively, a dormant task may be closed directly with a terminal status
        // (Succeeded/Failed/Escalated/etc.) without ever being dispatched.
        if (task.IsDormant())
        {
            // Allow closing a dormant task directly with a terminal status.
            if (AgentTask.TerminalStatuses.Contains(newStatus))
            {
                var newWorkDir = TaskDirManager.MoveToFinalLocation(
                    dispatchConfig.Value.ResolvedWorkDir, task.Id, newStatus, task.IsScheduled, logger);

                task.Status        = newStatus;
                task.Error         = request.Error;
                task.CompletedAt   = DateTime.UtcNow;
                task.UpdatedAt     = DateTime.UtcNow;
                task.AgentReported = true;

                if (request.ResultJson is not null)
                    task.ResultJson = request.ResultJson;

                task.ResultJson = TaskDirManager.UpdateResultJsonWorkDir(task.ResultJson, newWorkDir);

                await db.SaveChangesAsync(ct);

                logger.LogInformation("Dormant task {Id} closed with status {Status} (no dispatch).", id, newStatus);
                return Results.Ok(task);
            }

            if (newStatus != AgentTaskStatus.Pending)
                return Results.BadRequest(
                    $"Task {id} is in dormant state '{task.Status}'. " +
                    "Transition to Pending (to dispatch) or to a terminal status (to close without dispatching).");

            var workDir = TaskDirManager.FindTaskDir(dispatchConfig.Value.ResolvedWorkDir, id);
            if (workDir is not null)
            {
                var activeDir = TaskDirManager.ActivePath(dispatchConfig.Value.ResolvedWorkDir, id, task.IsScheduled);
                if (!workDir.Equals(activeDir, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(activeDir)!);
                    try { Directory.Move(workDir, activeDir); }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "PostResult/promote: could not move dormant dir for task {Id} — promoting anyway.", id);
                    }
                }
            }

            task.Status     = AgentTaskStatus.Pending;
            task.PromotedAt = DateTime.UtcNow;
            task.UpdatedAt  = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await queue.Writer.WriteAsync(task.Id, ct);

            logger.LogInformation("Task {Id} promoted from {OldStatus} → Pending and enqueued.", id, task.Status);
            return Results.Ok(task);
        }

        // ── Normal terminal-status path (Daeanne reporting completion) ────────
        if (task.IsTerminal())
            return Results.Conflict($"Task {id} is already in terminal state '{task.Status}'. No update applied.");

        if (!AgentTask.TerminalStatuses.Contains(newStatus))
            return Results.BadRequest($"Status must be one of: {string.Join(", ", AgentTask.TerminalStatuses)}.");

        // Update DB status. Do NOT move the task dir here —
        // TaskCleanupWorker runs on a schedule (default 60 min) and moves dirs for
        // all terminal tasks, giving file locks time to release before we attempt moves.
        task.Status      = newStatus;
        task.Error       = request.Error;
        task.CompletedAt = DateTime.UtcNow;
        task.UpdatedAt   = DateTime.UtcNow;
        task.AgentReported = true;   // Daeanne explicitly called this endpoint

        // Only overwrite ResultJson if the agent supplied one; preserve any fallback
        // stdout value captured at dispatch time (written by DispatchWorker on clean exit).
        if (request.ResultJson is not null)
            task.ResultJson = request.ResultJson;

        await db.SaveChangesAsync(ct);

        // If this is a sub-task, notify the parent
        if (task.ParentTaskId.HasValue)
            await TriggerParentResumeAsync(task, db, queue, dispatchConfig.Value, logger);

        // Unblock any tasks waiting on this one
        if (newStatus == AgentTaskStatus.Succeeded)
            await UnblockDependentsAsync(task.Id, db, queue, dispatchConfig.Value, logger);

        return Results.Ok(task);
    }

    /// <summary>
    /// Phase 1 of the callback contract — sub-agent POSTs this immediately on startup.
    /// Analogous to HTTP 202 Accepted: "I received the callback URL and I am working on it."
    /// Stamps CallbackAcknowledgedAt on the sub-task for observability.
    /// </summary>
    private static async Task<IResult> CallbackAck(
        Guid id,
        PostCallbackAckRequest request,
        DispatcherDbContext db)
    {
        var parent = await db.Tasks.FindAsync(id);
        if (parent is null) return Results.NotFound($"Parent task {id} not found.");

        var subtask = await db.Tasks.FindAsync(request.SubtaskId);
        if (subtask is null) return Results.NotFound($"Sub-task {request.SubtaskId} not found.");

        subtask.CallbackAcknowledgedAt = DateTime.UtcNow;
        subtask.UpdatedAt              = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Results.Accepted();
    }

    /// <summary>
    /// Phase 2 of the callback contract — sub-agent POSTs this when its work is complete.
    /// Writes a callback file to the parent's task dir, then re-queues the parent as Pending
    /// for fresh dispatch to any available agent instance.
    /// </summary>
    private static async Task<IResult> Callback(
        Guid id,
        PostCallbackRequest request,
        DispatcherDbContext db,
        Channel<Guid> queue,
        IOptions<DispatchConfig> dispatchConfig,
        ILogger<Program> logger)
    {
        var parent = await db.Tasks.FindAsync(id);
        if (parent is null) return Results.NotFound($"Parent task {id} not found.");

        var subtask = await db.Tasks.FindAsync(request.SubtaskId);
        if (subtask is null) return Results.NotFound($"Sub-task {request.SubtaskId} not found.");

        subtask.CallbackPostedAt = DateTime.UtcNow;
        subtask.UpdatedAt        = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await WriteCallbackFileAsync(
            parent, subtask.Id, request.Summary, request.ResultPath, request.Succeeded,
            dispatchConfig.Value, logger);

        bool requeued = false;
        if (parent.Status == AgentTaskStatus.Awaiting)
        {
            parent.Status    = AgentTaskStatus.Pending;
            parent.UpdatedAt = DateTime.UtcNow;
            requeued         = true;
            await db.SaveChangesAsync();
            await queue.Writer.WriteAsync(parent.Id);
            logger.LogInformation("Callback received from sub-task {SubId} → re-queued parent {ParentId}",
                subtask.Id, parent.Id);
        }

        return Results.Ok(new { parentRequeued = requeued });
    }

    // ─── Shared helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// After a task reaches Succeeded, find all tasks blocked on it via DependsOnTaskId
    /// and promote them to Pending for immediate dispatch.
    /// </summary>
    internal static async Task UnblockDependentsAsync(
        Guid succeededTaskId,
        DispatcherDbContext db,
        Channel<Guid> queue,
        DispatchConfig config,
        ILogger logger)
    {
        var dependents = await db.Tasks
            .Where(t => t.DependsOnTaskId == succeededTaskId &&
                        (t.Status == AgentTaskStatus.Blocked || t.Status == AgentTaskStatus.Deferred))
            .ToListAsync();

        if (dependents.Count == 0) return;

        foreach (var dep in dependents)
        {
            // Move task dir from blocked/deferred → active/
            var workDir = TaskDirManager.FindTaskDir(config.ResolvedWorkDir, dep.Id);
            if (workDir is not null)
            {
                var activeDir = TaskDirManager.ActivePath(config.ResolvedWorkDir, dep.Id, dep.IsScheduled);
                if (!workDir.Equals(activeDir, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(activeDir)!);
                    try { Directory.Move(workDir, activeDir); }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "UnblockDependents: could not move dir for task {Id} — promoting anyway.", dep.Id);
                    }
                }
            }

            dep.Status          = AgentTaskStatus.Pending;
            dep.PromotedAt      = DateTime.UtcNow;
            dep.UpdatedAt       = DateTime.UtcNow;
            dep.DependsOnTaskId = null; // dependency satisfied — clear it
            await queue.Writer.WriteAsync(dep.Id);

            logger.LogInformation(
                "Task {DepId} unblocked → Pending (upstream {UpstreamId} succeeded).",
                dep.Id, succeededTaskId);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Called after a sub-task reaches a terminal state (via natural process exit or PostResult).
    /// Writes a callback file to the parent's task dir and re-queues the parent if it's Awaiting.
    /// </summary>
    internal static async Task TriggerParentResumeAsync(
        AgentTask completedSubTask,
        DispatcherDbContext db,
        Channel<Guid> queue,
        DispatchConfig config,
        ILogger logger)
    {
        if (completedSubTask.ParentTaskId is null) return;

        var parent = await db.Tasks.FindAsync(completedSubTask.ParentTaskId.Value);
        if (parent is null || parent.Status != AgentTaskStatus.Awaiting)
        {
            logger.LogDebug(
                "Sub-task {SubId} completed but parent {ParentId} is not Awaiting (status: {Status}) — skipping.",
                completedSubTask.Id, completedSubTask.ParentTaskId, parent?.Status);
            return;
        }

        await WriteCallbackFileAsync(parent, completedSubTask.Id,
            summary: null, resultPath: null,
            succeeded: completedSubTask.Status == AgentTaskStatus.Succeeded,
            config, logger,
            resultJson: completedSubTask.ResultJson,
            error: completedSubTask.Error);

        parent.Status    = AgentTaskStatus.Pending;
        parent.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await queue.Writer.WriteAsync(parent.Id);

        logger.LogInformation("Sub-task {SubId} terminal → re-queued parent {ParentId}",
            completedSubTask.Id, parent.Id);
    }

    private static async Task WriteCallbackFileAsync(
        AgentTask parent, Guid subtaskId,
        string? summary, string? resultPath, bool succeeded,
        DispatchConfig config, ILogger logger,
        string? resultJson = null, string? error = null)
    {
        try
        {
            var parentDir = TaskDirManager.FindTaskDir(config.ResolvedWorkDir, parent.Id)
                            ?? TaskDirManager.ActivePath(config.ResolvedWorkDir, parent.Id, parent.IsScheduled);
            var callbacksDir = Path.Combine(parentDir, "callbacks");
            Directory.CreateDirectory(callbacksDir);

            var payload = JsonSerializer.Serialize(new
            {
                subtaskId,
                postedAt   = DateTime.UtcNow,
                succeeded,
                summary,
                resultPath,
                resultJson,
                error
            }, new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(Path.Combine(callbacksDir, $"{subtaskId}.json"), payload);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not write callback file for sub-task {SubId} → parent {ParentId}",
                subtaskId, parent.Id);
        }
    }

    /// <summary>
    /// Manually resumes a Failed task that has a session.md in its work directory.
    /// </summary>
    private static async Task<IResult> ResumeTask(
        Guid id,
        DispatcherDbContext db,
        IAgentDispatcher dispatcher,
        IOptions<DispatchConfig> dispatchConfig,
        IServiceScopeFactory scopeFactory,
        ILogger<Program> logger)
    {
        var task = await db.Tasks.FindAsync(id);
        if (task is null) return Results.NotFound();

        if (task.Status == AgentTaskStatus.Running)
            return Results.Conflict($"Task {id} is already Running.");

        var workDir = TaskDirManager.FindTaskDir(dispatchConfig.Value.ResolvedWorkDir, id);
        if (workDir is null)
            return Results.BadRequest($"No work directory found for task {id} — cannot resume.");

        var sessionPath = Path.Combine(workDir, "session.md");
        if (!File.Exists(sessionPath))
            return Results.BadRequest($"No session.md in {workDir} — cannot resume (no prior session).");

        var activeDir = TaskDirManager.ActivePath(dispatchConfig.Value.ResolvedWorkDir, id, task.IsScheduled);
        if (!workDir.Equals(activeDir, StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(activeDir)!);
            Directory.Move(workDir, activeDir);
        }

        task.Status    = AgentTaskStatus.Running;
        task.Error     = null;
        task.StartedAt = DateTime.UtcNow;
        task.UpdatedAt = DateTime.UtcNow;
        task.AttemptCount++;
        await db.SaveChangesAsync();

        _ = Task.Run(async () =>
        {
            var result = await dispatcher.TryResumeAsync(task, activeDir);

            if (result is null || !result.Succeeded)
            {
                // Resume failed — mark the error but leave completion to Daeanne.
                // Only force-fail if the resume process itself errored out.
                using var scope = scopeFactory.CreateScope();
                var freshDb = scope.ServiceProvider.GetRequiredService<DispatcherDbContext>();
                var t = await freshDb.Tasks.FindAsync(id);
                if (t is not null && !t.IsTerminal())
                {
                    var errorMsg = result?.Error ?? "Resume process returned no result.";
                    var newWorkDir = TaskDirManager.MoveToFinalLocation(
                        dispatchConfig.Value.ResolvedWorkDir, id, AgentTaskStatus.Failed, task.IsScheduled);
                    t.Status      = AgentTaskStatus.Failed;
                    t.Error       = errorMsg;
                    t.ResultJson  = TaskDirManager.UpdateResultJsonWorkDir(t.ResultJson, newWorkDir);
                    t.CompletedAt = DateTime.UtcNow;
                    t.UpdatedAt   = DateTime.UtcNow;
                    await freshDb.SaveChangesAsync();
                }
            }
            else
            {
                // Resume process exited cleanly — leave Running, await Daeanne's PATCH.
                logger.LogInformation("Task {Id} resume process exited cleanly — awaiting Daeanne's self-report.", id);
            }
        });

        return Results.Accepted($"/tasks/{id}", task);
    }
}
