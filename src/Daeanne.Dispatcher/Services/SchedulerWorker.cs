using System.Text;
using System.Text.Json;
using Daeanne.Dispatcher.Data;
using Daeanne.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Daeanne.Dispatcher.Services;

/// <summary>
/// Polls the ScheduledJobs table every minute and fires due jobs as Tasks.
/// Built-in schedules (daily summary, weekly 1:1) are seeded as ScheduledJob
/// records on startup — there is no longer any hardcoded schedule logic here.
///
/// Prompt templates support tokens resolved at fire time:
///   {date}         → local datetime (yyyy-MM-dd HH:mm)
///   {dateUtc}      → UTC ISO 8601
///   {date-24h}     → UTC ISO 8601, 24 hours ago
///   {date-7d}      → UTC ISO 8601, 7 days ago
///   {journal-date} → local date (yyyy-MM-dd)
///   {recipient}    → Scheduler:DailySummaryRecipient from config
/// </summary>
public class SchedulerWorker(
    IConfiguration config,
    IOptions<DispatchConfig> dispatchConfig,
    IServiceScopeFactory scopeFactory,
    ILogger<SchedulerWorker> logger) : BackgroundService
{
    private DateTime _lastArchived = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("SchedulerWorker: started — polling DB every 60s");

        while (!stoppingToken.IsCancellationRequested)
        {
            await FireDueJobsAsync(stoppingToken);

            if (DateTime.UtcNow - _lastArchived > TimeSpan.FromHours(23))
            {
                TaskDirManager.ArchiveOld(dispatchConfig.Value.ResolvedWorkDir, archiveDays: 30, logger);
                _lastArchived = DateTime.UtcNow;
            }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    // ─── Seeding ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the built-in ScheduledJob records the first time the Dispatcher starts.
    /// Idempotent: checks by Name before inserting. Call from Program.cs after EnsureCreated.
    /// </summary>
    public static async Task SeedBuiltInJobsAsync(
        IServiceProvider services, IConfiguration config, ILogger logger)
    {
        var recipient = config["Scheduler:DailySummaryRecipient"] ?? "";
        var enabled   = config.GetValue<bool>("Scheduler:DailySummaryEnabled", true);

        if (!enabled || string.IsNullOrWhiteSpace(recipient))
        {
            logger.LogWarning("SchedulerWorker: built-in job seeding skipped (disabled or no recipient).");
            return;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DispatcherDbContext>();
        var now = DateTime.Now;

        // ── Daily Summary ──────────────────────────────────────────────────
        if (!await db.ScheduledJobs.AnyAsync(j => j.Name == "daily-summary"))
        {
            if (!TimeOnly.TryParse(config["Scheduler:DailySummaryTime"] ?? "08:00", out var dailyTime))
                dailyTime = new TimeOnly(8, 0);

            db.ScheduledJobs.Add(new ScheduledJob
            {
                Name                  = "daily-summary",
                JobType               = ScheduledJobType.Daily,
                TaskType              = AgentTaskType.DailySummary,
                TimeOfDay             = dailyTime,
                CorrelationIdTemplate = "daily-summary-{yyyyMMdd}",
                Prompt                = DailySummaryPromptTemplate,
                NextRunAt             = ComputeDailyNextRun(now, dailyTime),
                IsActive              = true,
                CreatedAt             = DateTime.UtcNow
            });
            logger.LogInformation("SchedulerWorker: seeded built-in 'daily-summary' job (time={Time})", dailyTime);
        }

        // ── Weekly 1:1 ────────────────────────────────────────────────────
        if (!await db.ScheduledJobs.AnyAsync(j => j.Name == "weekly-oneonone"))
        {
            if (!TimeOnly.TryParse(config["Scheduler:WeeklyTime"] ?? "08:00", out var weeklyTime))
                weeklyTime = new TimeOnly(8, 0);
            var weeklyDay = config.GetValue("Scheduler:WeeklyDayOfWeek", DayOfWeek.Friday);

            db.ScheduledJobs.Add(new ScheduledJob
            {
                Name                  = "weekly-oneonone",
                JobType               = ScheduledJobType.Weekly,
                TaskType              = AgentTaskType.WeeklyOneOnOne,
                TimeOfDay             = weeklyTime,
                DayOfWeek             = weeklyDay,
                CorrelationIdTemplate = "weekly-oneonone-{yyyyMMdd}",
                Prompt                = WeeklyPromptTemplate,
                NextRunAt             = ComputeWeeklyNextRun(now, weeklyDay, weeklyTime),
                IsActive              = true,
                CreatedAt             = DateTime.UtcNow
            });
            logger.LogInformation("SchedulerWorker: seeded built-in 'weekly-oneonone' job (day={Day} time={Time})",
                weeklyDay, weeklyTime);
        }

        await db.SaveChangesAsync();
    }

    // ─── Prompt templates ─────────────────────────────────────────────────

    private const string DailySummaryPromptTemplate = """
        Daily summary request — {date} local

        Summarize all Daeanne activity in the past 24 hours.
          Window: {date-24h} → {dateUtc}
          Recipient: {recipient}

        See the Daily Summary section of your instructions for the full format and procedure.
        Include today's journal entries from ~/.daeanne/journal/{journal-date}.md if it exists.
        """;

    private const string WeeklyPromptTemplate = """
        Weekly 1:1 report — {date} local

        Prepare a weekly review for Jeffrey. This is your reflective 1:1 as Chief of Staff.
          Week: {date-7d} → {dateUtc}
          Recipient: {recipient}

        Cover: what got done, what worked, what didn't, blockers, things you wish you had,
        ideas or suggestions, anything you want Jeffrey to know or decide on.
        Be candid — this is your opportunity to speak up, not just summarize.
        """;

    // ─── Firing ───────────────────────────────────────────────────────────

    private async Task FireDueJobsAsync(CancellationToken ct)
    {
        var dispatcherUrl = config["Dispatcher:Url"] ?? "http://127.0.0.1:47777";

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DispatcherDbContext>();

        var nowUtc = DateTime.UtcNow;
        var dueJobs = await db.ScheduledJobs
            .Where(j => j.IsActive && j.NextRunAt <= nowUtc)
            .ToListAsync(ct);

        foreach (var job in dueJobs)
        {
            var correlationId = BuildCorrelationId(job);
            var resolvedPrompt = ResolveTokens(job.Prompt);

            await TryPostTaskAsync(resolvedPrompt, job.TaskType.ToString(),
                correlationId, dispatcherUrl, ct, scheduledJobId: job.Id);

            job.LastFiredAt = nowUtc;
            job.NextRunAt   = ComputeNextRun(job, DateTime.Now);

            if (job.JobType == ScheduledJobType.Once)
                job.IsActive = false;

            logger.LogInformation("SchedulerWorker: fired '{Name}' ({Id}), next={Next}",
                job.Name, job.Id, job.IsActive ? job.NextRunAt : (DateTime?)null);
        }

        if (dueJobs.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    // ─── Token resolution ─────────────────────────────────────────────────

    private string ResolveTokens(string template)
    {
        var now       = DateTime.Now;
        var recipient = config["Scheduler:DailySummaryRecipient"] ?? "";
        return template
            .Replace("{date}",         now.ToString("yyyy-MM-dd HH:mm"))
            .Replace("{dateUtc}",      now.ToUniversalTime().ToString("O"))
            .Replace("{date-24h}",     now.AddHours(-24).ToUniversalTime().ToString("O"))
            .Replace("{date-7d}",      now.AddDays(-7).ToUniversalTime().ToString("O"))
            .Replace("{journal-date}", now.ToString("yyyy-MM-dd"))
            .Replace("{recipient}",    recipient);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static string? BuildCorrelationId(ScheduledJob job)
    {
        if (string.IsNullOrWhiteSpace(job.CorrelationIdTemplate)) return null;
        var now = DateTime.Now;
        return job.CorrelationIdTemplate
            .Replace("{yyyyMMdd}", now.ToString("yyyyMMdd"))
            .Replace("{HHmm}",    now.ToString("HHmm"))
            .Replace("{id}",      job.Id.ToString());
    }

    internal static DateTime ComputeNextRun(ScheduledJob job, DateTime localNow) => job.JobType switch
    {
        ScheduledJobType.Once     => DateTime.MaxValue,
        ScheduledJobType.Interval => localNow.AddMinutes(job.IntervalMinutes!.Value).ToUniversalTime(),
        ScheduledJobType.Daily    => localNow.Date.AddDays(1).Add(job.TimeOfDay!.Value.ToTimeSpan()).ToUniversalTime(),
        ScheduledJobType.Weekly   => ComputeWeeklyNextRun(localNow, job.DayOfWeek!.Value, job.TimeOfDay!.Value).ToUniversalTime(),
        _                         => DateTime.MaxValue
    };

    internal static DateTime ComputeDailyNextRun(DateTime localNow, TimeOnly time)
    {
        var today = localNow.Date.Add(time.ToTimeSpan());
        return (today > localNow ? today : today.AddDays(1)).ToUniversalTime();
    }

    internal static DateTime ComputeWeeklyNextRun(DateTime localNow, DayOfWeek target, TimeOnly time)
    {
        int daysUntil = ((int)target - (int)localNow.DayOfWeek + 7) % 7;
        if (daysUntil == 0) daysUntil = 7;
        return localNow.Date.AddDays(daysUntil).Add(time.ToTimeSpan());
    }

    private async Task TryPostTaskAsync(
        string prompt, string type, string? correlationId, string dispatcherUrl,
        CancellationToken ct, Guid? scheduledJobId = null)
    {
        var body = JsonSerializer.Serialize(new
        {
            type,
            prompt,
            correlationId,
            isScheduled    = true,
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
}

