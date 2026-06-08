using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Daeanne.Dispatcher.Services;

/// <summary>
/// Fires scheduled tasks based on wall-clock time, checked every minute.
/// Uses correlationId idempotency to avoid duplicate tasks on restart.
///
/// Schedules:
///   - Daily summary: every day at DailySummaryTime (default 08:00)
///   - Weekly 1:1:   every Friday at WeeklyTime (default 08:00)
/// </summary>
public class SchedulerWorker(
    IConfiguration config,
    IOptions<DispatchConfig> dispatchConfig,
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

        // Poll every minute and fire when wall-clock time matches a schedule.
        // correlationId = "daily-summary-{yyyyMMdd}" / "weekly-oneonone-{yyyyMMdd}" ensures
        // at-most-once per period even if Dispatcher restarts multiple times.
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;

            // Daily summary: fire if today's window has been reached and not yet created
            if (TimeOnly.FromDateTime(now) >= dailyTime)
                await TryPostTaskAsync(BuildDailySummaryPrompt(now, recipient),
                    "DailySummary", $"daily-summary-{now:yyyyMMdd}", dispatcherUrl, stoppingToken);

            // Weekly 1:1: fire on the configured day
            if (now.DayOfWeek == weeklyDay && TimeOnly.FromDateTime(now) >= weeklyTime)
                await TryPostTaskAsync(BuildWeeklyPrompt(now, recipient),
                    "WeeklyOneOnOne", $"weekly-oneonone-{now:yyyyMMdd}", dispatcherUrl, stoppingToken);

            // Archive old task dirs once a day (piggyback on daily check)
            if (TimeOnly.FromDateTime(now) >= dailyTime && TimeOnly.FromDateTime(now) < dailyTime.AddMinutes(2))
                TaskDirManager.ArchiveOld(dispatchConfig.Value.ResolvedWorkDir, archiveDays: 30, logger);

            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TryPostTaskAsync(
        string prompt, string type, string correlationId, string dispatcherUrl, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { type, prompt, correlationId });
        try
        {
            using var http = new HttpClient();
            var resp = await http.PostAsync(
                $"{dispatcherUrl}/tasks",
                new StringContent(body, Encoding.UTF8, "application/json"), ct);

            if (resp.IsSuccessStatusCode)
                logger.LogInformation("SchedulerWorker: created task [{CorrelationId}]", correlationId);
            else if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
                logger.LogInformation("SchedulerWorker: [{CorrelationId}] already exists — skipping", correlationId);
            else
                logger.LogWarning("SchedulerWorker: failed to create [{CorrelationId}] ({Code})",
                    correlationId, resp.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SchedulerWorker: error posting [{CorrelationId}]", correlationId);
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
