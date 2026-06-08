using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Daeanne.Dispatcher.Services;

/// <summary>
/// Fires scheduled tasks at configured times.
/// Currently: daily summary at a configurable local time (default 08:00).
///
/// Each scheduled trigger POSTs a task to the Dispatcher's own HTTP API,
/// so it goes through the normal dispatch pipeline — no special handling needed.
/// </summary>
public class SchedulerWorker(
    IConfiguration config,
    IOptions<DispatchConfig> dispatchConfig,
    ILogger<SchedulerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled   = config.GetValue<bool>("Scheduler:DailySummaryEnabled", true);
        var timeStr   = config["Scheduler:DailySummaryTime"] ?? "08:00";
        var recipient = config["Scheduler:DailySummaryRecipient"];
        var dispatcherUrl = config["Dispatcher:Url"] ?? "http://127.0.0.1:47777";

        if (!enabled)
        {
            logger.LogInformation("SchedulerWorker: daily summary disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(recipient))
        {
            logger.LogWarning("SchedulerWorker: Scheduler:DailySummaryRecipient not set — daily summary disabled.");
            return;
        }

        if (!TimeOnly.TryParse(timeStr, out var fireTime))
        {
            logger.LogWarning("SchedulerWorker: invalid Scheduler:DailySummaryTime '{Time}' — using 08:00.", timeStr);
            fireTime = new TimeOnly(8, 0);
        }

        logger.LogInformation("SchedulerWorker: daily summary at {Time} local → {Recipient}", fireTime, recipient);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNext(fireTime);
            logger.LogInformation("SchedulerWorker: next daily summary in {H:F1} hours.", delay.TotalHours);

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }

            await PostDailySummaryTaskAsync(recipient, dispatcherUrl, stoppingToken);

            // Archive completed tasks older than 30 days
            TaskDirManager.ArchiveOld(dispatchConfig.Value.ResolvedWorkDir, archiveDays: 30, logger);
        }
    }

    private async Task PostDailySummaryTaskAsync(
        string recipient, string dispatcherUrl, CancellationToken ct)
    {
        var windowEnd   = DateTime.Now;
        var windowStart = windowEnd.AddHours(-24);

        var prompt = $"""
            Daily summary request — {windowEnd:yyyy-MM-dd HH:mm} local

            Summarize all Daeanne activity in the 24-hour window:
              Window start: {windowStart:O}
              Window end:   {windowEnd:O}
              Recipient:    {recipient}

            See the Daily Summary section of your instructions for the full format and procedure.
            """;

        var body = JsonSerializer.Serialize(new
        {
            type    = "DailySummary",
            prompt,
            correlationId = $"daily-summary-{windowEnd:yyyyMMdd}"
        });

        try
        {
            using var http = new HttpClient();
            var resp = await http.PostAsync(
                $"{dispatcherUrl}/tasks",
                new StringContent(body, Encoding.UTF8, "application/json"), ct);

            if (resp.IsSuccessStatusCode)
                logger.LogInformation("SchedulerWorker: daily summary task created for {Date}.", windowEnd.Date);
            else if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
                logger.LogInformation("SchedulerWorker: daily summary for {Date} already exists — skipping.", windowEnd.Date);
            else
                logger.LogWarning("SchedulerWorker: failed to create daily summary task ({Code}).", resp.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SchedulerWorker: error posting daily summary task.");
        }
    }

    /// <summary>Returns the duration until the next occurrence of <paramref name="time"/> local time.</summary>
    private static TimeSpan TimeUntilNext(TimeOnly time)
    {
        var now  = DateTime.Now;
        var next = now.Date + time.ToTimeSpan();
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}
