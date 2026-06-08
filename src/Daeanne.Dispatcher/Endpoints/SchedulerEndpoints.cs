using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Daeanne.Dispatcher.Data;
using Daeanne.Dispatcher.Services;
using Daeanne.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Daeanne.Dispatcher.Endpoints;

public static class SchedulerEndpoints
{
    public static void MapSchedulerEndpoints(this WebApplication app)
    {
        // Fire a summary on demand — useful for testing and manual requests.
        // Returns 201 with the new task, or 409 if one already ran today.
        app.MapPost("/scheduler/summary", FireSummary);
    }

    private static async Task<IResult> FireSummary(
        IConfiguration config,
        DispatcherDbContext db,
        Channel<Guid> queue)
    {
        var recipient = config["Scheduler:DailySummaryRecipient"];
        if (string.IsNullOrWhiteSpace(recipient))
            return Results.BadRequest("Scheduler:DailySummaryRecipient not configured.");

        var now = DateTime.Now;
        var correlationId = $"daily-summary-{now:yyyyMMdd}";

        var existing = await db.Tasks.FirstOrDefaultAsync(t => t.CorrelationId == correlationId);
        if (existing is not null)
            return Results.Conflict(new
            {
                message = $"A daily summary for {now:yyyy-MM-dd} already exists (status: {existing.Status}).",
                taskId  = existing.Id,
                status  = existing.Status.ToString()
            });

        var prompt = $"""
            Daily summary request — {now:yyyy-MM-dd HH:mm} local (on-demand)

            Summarize all Daeanne activity in the past 24 hours.
              Window: {now.AddHours(-24):O} → {now:O}
              Recipient: {recipient}

            See the Daily Summary section of your instructions for the full format and procedure.
            Include today's journal entries from ~/.daeanne/journal/{now:yyyy-MM-dd}.md if it exists.
            """;

        var task = new AgentTask
        {
            Id            = Guid.NewGuid(),
            Type          = AgentTaskType.DailySummary,
            Prompt        = prompt,
            CorrelationId = correlationId,
            IsScheduled   = true,
            Status        = AgentTaskStatus.Pending,
            CreatedAt     = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        await queue.Writer.WriteAsync(task.Id);

        return Results.Created($"/tasks/{task.Id}", task);
    }
}
