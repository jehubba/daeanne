using System.Threading.Channels;
using Daeanne.Dispatcher.Data;
using Daeanne.Shared.Models;
using Daeanne.Shared.Requests;
using Microsoft.EntityFrameworkCore;

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
        DispatcherDbContext db)
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

        task.Status = newStatus;
        task.ResultJson = request.ResultJson;
        task.Error = request.Error;
        task.CompletedAt = DateTime.UtcNow;
        task.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Results.Ok(task);
    }
}

