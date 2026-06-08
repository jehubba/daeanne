using Daeanne.Dispatcher.Data;
using Daeanne.Shared.Models;
using Daeanne.Shared.Requests;
using Microsoft.EntityFrameworkCore;

namespace Daeanne.Dispatcher.Endpoints;

public static class SchedulerCronEndpoints
{
    public static void MapSchedulerCronEndpoints(this WebApplication app)
    {
        app.MapGet("/scheduler/crons",         ListJobs);
        app.MapPost("/scheduler/crons",        CreateJob);
        app.MapDelete("/scheduler/crons/{id}", DeleteJob);
    }

    private static async Task<IResult> ListJobs(DispatcherDbContext db, bool? activeOnly = true)
    {
        var q = db.ScheduledJobs.AsQueryable();
        if (activeOnly == true) q = q.Where(j => j.IsActive);
        var jobs = await q.OrderBy(j => j.NextRunAt).ToListAsync();
        return Results.Ok(jobs);
    }

    private static async Task<IResult> CreateJob(
        CreateScheduledJobRequest req, DispatcherDbContext db)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Results.BadRequest("Name is required.");
        if (string.IsNullOrWhiteSpace(req.Prompt))
            return Results.BadRequest("Prompt is required.");

        var now = DateTime.UtcNow;
        DateTime nextRun;
        TimeOnly? timeOfDay = null;
        DayOfWeek? dayOfWeek = null;

        switch (req.JobType)
        {
            case ScheduledJobType.Once:
                if (!DateTime.TryParse(req.RunAt, out var runAt))
                    return Results.BadRequest("RunAt must be a valid ISO 8601 datetime for 'once' jobs.");
                nextRun = runAt.ToUniversalTime();
                break;

            case ScheduledJobType.Daily:
                if (!TimeOnly.TryParse(req.RunAt, out var dailyTime))
                    return Results.BadRequest("RunAt must be 'HH:mm' for 'daily' jobs.");
                timeOfDay = dailyTime;
                var localNow = DateTime.Now;
                nextRun = DateTime.SpecifyKind(
                    localNow.Date.Add(dailyTime.ToTimeSpan()) > localNow
                        ? localNow.Date.Add(dailyTime.ToTimeSpan())
                        : localNow.Date.AddDays(1).Add(dailyTime.ToTimeSpan()),
                    DateTimeKind.Local).ToUniversalTime();
                break;

            case ScheduledJobType.Weekly:
                if (!TimeOnly.TryParse(req.RunAt, out var weeklyTime))
                    return Results.BadRequest("RunAt must be 'HH:mm' for 'weekly' jobs.");
                if (!Enum.TryParse<DayOfWeek>(req.DayOfWeek, ignoreCase: true, out var dow))
                    return Results.BadRequest("DayOfWeek must be a valid day name (e.g. 'Friday').");
                timeOfDay = weeklyTime;
                dayOfWeek = dow;
                nextRun = NextWeeklyRun(DateTime.Now, dow, weeklyTime).ToUniversalTime();
                break;

            case ScheduledJobType.Interval:
                if (req.IntervalMinutes is null or <= 0)
                    return Results.BadRequest("IntervalMinutes must be > 0 for 'interval' jobs.");
                nextRun = now.AddMinutes(req.IntervalMinutes.Value);
                break;

            default:
                return Results.BadRequest($"Unknown job type: {req.JobType}");
        }

        var job = new ScheduledJob
        {
            Name                 = req.Name,
            JobType              = req.JobType,
            TaskType             = req.TaskType,
            Prompt               = req.Prompt,
            RunAt                = req.JobType == ScheduledJobType.Once ? nextRun : null,
            TimeOfDay            = timeOfDay,
            DayOfWeek            = dayOfWeek,
            IntervalMinutes      = req.IntervalMinutes,
            CorrelationIdTemplate = req.CorrelationIdTemplate,
            NextRunAt            = nextRun,
            IsActive             = true,
            CreatedAt            = now
        };

        db.ScheduledJobs.Add(job);
        await db.SaveChangesAsync();
        return Results.Created($"/scheduler/crons/{job.Id}", job);
    }

    private static async Task<IResult> DeleteJob(Guid id, DispatcherDbContext db)
    {
        var job = await db.ScheduledJobs.FindAsync(id);
        if (job is null) return Results.NotFound();
        job.IsActive = false;
        await db.SaveChangesAsync();
        return Results.Ok(job);
    }

    private static DateTime NextWeeklyRun(DateTime localNow, DayOfWeek target, TimeOnly time)
    {
        var candidate = localNow.Date.Add(time.ToTimeSpan());
        int daysUntil = ((int)target - (int)localNow.DayOfWeek + 7) % 7;
        if (daysUntil == 0 && candidate <= localNow) daysUntil = 7;
        return candidate.AddDays(daysUntil);
    }
}
