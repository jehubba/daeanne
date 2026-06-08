using System.Text;
using System.Text.Json;
using Daeanne.Dispatcher.Data;
using Daeanne.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Daeanne.Dispatcher.Services;

/// <summary>
/// Fires scheduled tasks based on wall-clock time, checked every minute.
/// Handles both built-in schedules (daily summary, weekly 1:1) and
/// dynamic jobs registered via POST /scheduler/crons.
/// Uses correlationId idempotency to avoid duplicate tasks on restart.
/// </summary>
public class SchedulerWorker(
    IConfiguration config,
    IOptions<DispatchConfig> dispatchConfig,
    IServiceScopeFactory scopeFactory,
    ILogger<SchedulerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled   = config.GetValue<bool>("Scheduler:DailySummaryEnabled", true);
        var recipient = config["Scheduler:DailySummaryRecipient"];
        var dispatcherUrl = config["Dispatcher:Url"] ?? "http://127.0.0.1:47777";

        if (!enabled || string.IsNullOrWhiteSpace(recipient))
        {
            logger.LogWarning("SchedulerWorker: disabled or recipient not configured.");
            return;
        }

        if (!TimeOnly.TryParse(config["Scheduler:DailySummaryTime"] ?? "08:00", out var dailyTime))
            dailyTime = new TimeOnly(8, 0);

        if (!TimeOnly.TryParse(config["Scheduler:WeeklyTime"] ?? "08:00", out var weeklyTime))
            weeklyTime = new TimeOnly(8, 0);

        var weeklyDay = config.GetValue("Scheduler:WeeklyDayOfWeek", DayOfWeek.Friday);

        logger.LogInformation(
            "SchedulerWorker: daily at {Daily}, weekly 1:1 on {Day} at {Weekly} → {Recipient}",
            dailyTime, weeklyDay, weeklyTime, recipient);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;

            // Built-in: daily summary
            if (TimeOnly.FromDateTime(now) >= dailyTime)
                await TryPostTaskAsync(BuildDailySummaryPrompt(now, recipient),
                    "DailySummary", $"daily-summary-{now:yyyyMMdd}", dispatcherUrl, stoppingToken);

            // Built-in: weekly 1:1
            if (now.DayOfWeek == weeklyDay && TimeOnly.FromDateTime(now) >= weeklyTime)
                await TryPostTaskAsync(BuildWeeklyPrompt(now, recipient),
                    "WeeklyOneOnOne", $"weekly-oneonone-{now:yyyyMMdd}", dispatcherUrl, stoppingToken);

            // Archive old task dirs (piggyback on daily check)
            if (TimeOnly.FromDateTime(now) >= dailyTime && TimeOnly.FromDateTime(now) < dailyTime.AddMinutes(2))
                TaskDirManager.ArchiveOld(dispatchConfig.Value.ResolvedWorkDir, archiveDays: 30, logger);

            // Dynamic jobs from DB
            await FireDueJobsAsync(dispatcherUrl, stoppingToken);

            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task FireDueJobsAsync(string dispatcherUrl, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DispatcherDbContext>();

        var nowUtc = DateTime.UtcNow;
        var dueJobs = await db.ScheduledJobs
            .Where(j => j.IsActive && j.NextRunAt <= nowUtc)
            .ToListAsync(ct);

        foreach (var job in dueJobs)
        {
            var correlationId = BuildCorrelationId(job);
            await TryPostTaskAsync(job.Prompt, job.TaskType.ToString(), correlationId, dispatcherUrl, ct,
                scheduledJobId: job.Id);

            job.LastFiredAt = nowUtc;
            job.NextRunAt   = ComputeNextRun(job, DateTime.Now);

            // One-time jobs deactivate after firing
            if (job.JobType == ScheduledJobType.Once)
                job.IsActive = false;

            logger.LogInformation("SchedulerWorker: fired dynamic job '{Name}' ({Id}), next={Next}",
                job.Name, job.Id, job.IsActive ? job.NextRunAt : (DateTime?)null);
        }

        if (dueJobs.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    private static string? BuildCorrelationId(ScheduledJob job)
    {
        if (string.IsNullOrWhiteSpace(job.CorrelationIdTemplate)) return null;
        var now = DateTime.Now;
        return job.CorrelationIdTemplate
            .Replace("{yyyyMMdd}", now.ToString("yyyyMMdd"))
            .Replace("{HHmm}",    now.ToString("HHmm"))
            .Replace("{id}",      job.Id.ToString());
    }

    private static DateTime ComputeNextRun(ScheduledJob job, DateTime localNow) => job.JobType switch
    {
        ScheduledJobType.Once     => DateTime.MaxValue, // will be deactivated
        ScheduledJobType.Interval => localNow.AddMinutes(job.IntervalMinutes!.Value).ToUniversalTime(),
        ScheduledJobType.Daily    => localNow.Date.AddDays(1).Add(job.TimeOfDay!.Value.ToTimeSpan()).ToUniversalTime(),
        ScheduledJobType.Weekly   => NextWeeklyRun(localNow, job.DayOfWeek!.Value, job.TimeOfDay!.Value).ToUniversalTime(),
        _                         => DateTime.MaxValue
    };

    private static DateTime NextWeeklyRun(DateTime localNow, DayOfWeek target, TimeOnly time)
    {
        int daysUntil = ((int)target - (int)localNow.DayOfWeek + 7) % 7;
        if (daysUntil == 0) daysUntil = 7;
        return localNow.Date.AddDays(daysUntil).Add(time.ToTimeSpan());
    }

    private async Task TryPostTaskAsync(
        string prompt, string type, string? correlationId, string dispatcherUrl, CancellationToken ct,
        Guid? scheduledJobId = null)
    {
        var body = JsonSerializer.Serialize(new
        {
            type,
            prompt,
            correlationId,
            isScheduled   = true,
            scheduledJobId = scheduledJobId?.ToString()
        });
        try
        {
            using var http = new HttpClient();
            var resp = await http.PostAsync(
                $"{dispatcherUrl}/tasks",
                new StringContent(body, Encoding.UTF8, "application/json"), ct);

            if (resp.IsSuccessStatusCode)
                logger.LogInformation("SchedulerWorker: created task [{CorrelationId}]", correlationId ?? type);
            else if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
                logger.LogInformation("SchedulerWorker: [{CorrelationId}] already exists — skipping", correlationId ?? type);
            else
                logger.LogWarning("SchedulerWorker: failed to create [{CorrelationId}] ({Code})",
                    correlationId ?? type, resp.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SchedulerWorker: error posting [{CorrelationId}]", correlationId ?? type);
        }
    }

    private static string BuildDailySummaryPrompt(DateTime now, string recipient) => $"""
        Daily summary request — {now:yyyy-MM-dd HH:mm} local

        Summarize all Daeanne activity in the past 24 hours.
          Window: {now.AddHours(-24):O} → {now:O}
          Recipient: {recipient}

        See the Daily Summary section of your instructions for the full format and procedure.
        Include today's journal entries from ~/.daeanne/journal/{now:yyyy-MM-dd}.md if it exists.
        """;

    private static string BuildWeeklyPrompt(DateTime now, string recipient) => $"""
        Weekly 1:1 report — {now:yyyy-MM-dd HH:mm} local

        Prepare a weekly review for Jeffrey. This is your reflective 1:1 as Chief of Staff.
          Week: {now.AddDays(-7):O} → {now:O}
          Recipient: {recipient}

        Cover: what got done, what worked, what didn't, blockers, things you wish you had,
        ideas or suggestions, anything you want Jeffrey to know or decide on.
        Be candid — this is your opportunity to speak up, not just summarize.
        """;
}
