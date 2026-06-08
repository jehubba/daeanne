using System.Text.Json;
using System.Threading.Channels;
using Daeanne.Dispatcher.Data;
using Daeanne.Dispatcher.Services;
using Daeanne.Shared.Models;
using Daeanne.Shared.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Daeanne.Dispatcher.Endpoints;

public static class TaskEndpoints
{
    public static void MapTaskEndpoints(this WebApplication app)
    {
        app.MapGet("/tasks", GetTasks);
        app.MapGet("/tasks/{id:guid}", GetTask);
        app.MapPost("/tasks", CreateTask);
        app.MapPost("/tasks/{id:guid}/result", PostResult);
        app.MapPatch("/tasks/{id:guid}/status", PostResult);   // alias — agents use PATCH
        app.MapPost("/tasks/{id:guid}/resume", ResumeTask);
    }

    private static async Task<IResult> GetTasks(
        DispatcherDbContext db,
        string? status,
        int take = 50,
        int skip = 0)
    {
        var query = db.Tasks.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<AgentTaskStatus>(status, ignoreCase: true, out var parsed))
        {
            query = query.Where(t => t.Status == parsed);
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

    private static async Task<IResult> CreateTask(
        CreateTaskRequest request,
        DispatcherDbContext db,
        Channel<Guid> queue)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return Results.BadRequest("Prompt is required.");

        // Idempotency: if a non-terminal task with this correlationId already exists, return it
        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            var existing = await db.Tasks.FirstOrDefaultAsync(t =>
                t.CorrelationId == request.CorrelationId && !AgentTask.TerminalStatuses.Contains(t.Status));
            if (existing is not null)
                return Results.Conflict(existing);
        }

        // Merge GraphMessageId into ContextJson so the agent sees it in its prompt
        var contextJson = request.ContextJson;
        if (!string.IsNullOrWhiteSpace(request.GraphMessageId))
        {
            var ctx = string.IsNullOrWhiteSpace(contextJson)
                ? new Dictionary<string, string?>()
                : JsonSerializer.Deserialize<Dictionary<string, string?>>(contextJson) ?? new();
            ctx["graphMessageId"] = request.GraphMessageId;
            contextJson = JsonSerializer.Serialize(ctx);
        }

        var task = new AgentTask
        {
            Type = request.Type,
            Prompt = request.Prompt,
            ContextJson = contextJson,
            CorrelationId = request.CorrelationId
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // Signal DispatchWorker — DB is source of truth, channel is wake-up only
        await queue.Writer.WriteAsync(task.Id);

        return Results.Created($"/tasks/{task.Id}", task);
    }

    private static async Task<IResult> PostResult(
        Guid id,
        PostTaskResultRequest request,
        DispatcherDbContext db,
        IOptions<DispatchConfig> dispatchConfig)
    {
        var task = await db.Tasks.FindAsync(id);
        if (task is null) return Results.NotFound();

        if (task.IsTerminal())
            return Results.Conflict($"Task {id} is already in terminal state '{task.Status}'. No update applied.");

        if (!Enum.TryParse<AgentTaskStatus>(request.Status, ignoreCase: true, out var newStatus) ||
            !AgentTask.TerminalStatuses.Contains(newStatus))
        {
            return Results.BadRequest($"Status must be one of: {string.Join(", ", AgentTask.TerminalStatuses)}.");
        }

        // Move the task directory to its final location and update workDir in resultJson
        var newWorkDir = TaskDirManager.MoveToFinalLocation(
            dispatchConfig.Value.ResolvedWorkDir, id, newStatus);

        task.Status      = newStatus;
        task.ResultJson  = TaskDirManager.UpdateResultJsonWorkDir(request.ResultJson, newWorkDir);
        task.Error       = request.Error;
        task.CompletedAt = DateTime.UtcNow;
        task.UpdatedAt   = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Results.Ok(task);
    }

    /// <summary>
    /// Manually resumes a Failed task that has a session.md in its work directory.
    /// Resets status to Running, then re-dispatches using --resume.
    /// Useful for tasks interrupted by a restart or Ctrl+C.
    /// </summary>
    private static async Task<IResult> ResumeTask(
        Guid id,
        DispatcherDbContext db,
        IAgentDispatcher dispatcher,
        IOptions<DispatchConfig> dispatchConfig,
        IServiceScopeFactory scopeFactory)
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

        // Move back to active/ and mark Running
        var activeDir = TaskDirManager.ActivePath(dispatchConfig.Value.ResolvedWorkDir, id);
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

        // Fire resume in background using a fresh scope (request scope will close)
        _ = Task.Run(async () =>
        {
            var result = await dispatcher.TryResumeAsync(task, activeDir);
            result ??= new DispatchResult(false, null, "No session ID found in session.md.");

            var finalStatus = result.Succeeded ? AgentTaskStatus.Succeeded : AgentTaskStatus.Failed;
            var newWorkDir  = TaskDirManager.MoveToFinalLocation(
                dispatchConfig.Value.ResolvedWorkDir, id, finalStatus);

            using var scope = scopeFactory.CreateScope();
            var freshDb = scope.ServiceProvider.GetRequiredService<DispatcherDbContext>();
            var t = await freshDb.Tasks.FindAsync(id);
            if (t is not null)
            {
                t.Status      = finalStatus;
                t.ResultJson  = TaskDirManager.UpdateResultJsonWorkDir(result.ResultJson, newWorkDir);
                t.Error       = result.Error;
                t.CompletedAt = DateTime.UtcNow;
                t.UpdatedAt   = DateTime.UtcNow;
                await freshDb.SaveChangesAsync();
            }
        });

        return Results.Accepted($"/tasks/{id}", task);
    }
}

