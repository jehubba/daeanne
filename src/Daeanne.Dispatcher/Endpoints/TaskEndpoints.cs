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

        var task = new AgentTask
        {
            Type = request.Type,
            Prompt = request.Prompt,
            ContextJson = request.ContextJson,
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
}

