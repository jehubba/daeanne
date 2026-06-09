using System.Text.Json;

namespace Daeanne.Tray;

/// <summary>
/// System tray presence for Daeanne.
/// Polls Dispatcher + Bridge health every 30 seconds.
/// Icon states: green = healthy, orange = degraded or recent failure, red = both down.
/// </summary>
internal class TrayContext : ApplicationContext
{
    private readonly NotifyIcon    _trayIcon;
    private readonly HttpClient    _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(30));

    private ActivityWindow? _activityWindow;

    // Track last-seen task statuses to detect transitions
    private Dictionary<string, string> _lastTaskStatuses = new();
    private bool _firstPoll = true;

    // Icon state
    private enum TrayState { Green, Orange, Red }
    private TrayState _currentState = TrayState.Green;

    public TrayContext()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("View Activity",   null, OnOpenActivity);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Refresh Now",     null, async (s, e) => await PollAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit",            null, OnExit);

        _trayIcon = new NotifyIcon
        {
            Text             = "Daeanne",
            Icon             = IconLoader.Green,
            ContextMenuStrip = menu,
            Visible          = true
        };

        _trayIcon.DoubleClick += OnOpenActivity;

        // Initial poll + start background loop
        _ = Task.Run(PollLoopAsync);
    }

    private async Task PollLoopAsync()
    {
        await PollAsync();           // immediate first poll

        while (await _timer.WaitForNextTickAsync())
            await PollAsync();
    }

    private async Task PollAsync()
    {
        var dispatcherOk = await CheckHealthAsync("http://127.0.0.1:47777/health");
        var bridgeOk     = await CheckHealthAsync("http://127.0.0.1:47778/health");

        // Check for recent failures (last hour)
        bool recentFailure = await CheckRecentFailureAsync();

        var newState = (!dispatcherOk && !bridgeOk)   ? TrayState.Red
                     : (!dispatcherOk || !bridgeOk || recentFailure) ? TrayState.Orange
                     : TrayState.Green;

        _currentState  = newState;
        _trayIcon.Icon = newState switch
        {
            TrayState.Red    => IconLoader.Red,
            TrayState.Orange => IconLoader.Orange,
            _                => IconLoader.Green
        };

        // Build specific tooltip so orange is self-explaining
        string statusText;
        if (!dispatcherOk && !bridgeOk)
            statusText = "⚠ Dispatcher + Bridge unreachable";
        else if (!dispatcherOk)
            statusText = "⚠ Dispatcher unreachable";
        else if (!bridgeOk)
            statusText = "⚠ Bridge unreachable";
        else if (recentFailure)
            statusText = "⚠ Recent task failures";
        else
            statusText = "Healthy";

        _trayIcon.Text = $"Daeanne — {statusText}";
    }

    private async Task<bool> CheckHealthAsync(string url)
    {
        try
        {
            var resp = await _http.GetAsync(url);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task<bool> CheckRecentFailureAsync()
    {
        try
        {
            var json  = await _http.GetStringAsync("http://127.0.0.1:47777/tasks?take=50");
            var tasks = JsonSerializer.Deserialize<List<TaskPoll>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

            var cutoff  = DateTime.UtcNow.AddHours(-1);
            var newStatuses = tasks.ToDictionary(t => t.Id, t => t.Status ?? "");

            if (_firstPoll)
            {
                // Seed known statuses silently — don't balloon things that already happened
                _firstPoll = false;
                _lastTaskStatuses = newStatuses;
                _trayIcon.ShowBalloonTip(3000, "Daeanne", "Tray monitor started.", ToolTipIcon.Info);
            }
            else
            {
                // Detect transitions to terminal states since last poll → balloon tip
                foreach (var t in tasks)
                {
                    var isNewlyTerminal =
                        (t.Status == "Succeeded" || t.Status == "Failed" || t.Status == "TimedOut") &&
                        (!_lastTaskStatuses.TryGetValue(t.Id, out var prev) ||
                         (prev != "Succeeded" && prev != "Failed" && prev != "TimedOut"));

                    if (isNewlyTerminal)
                        ShowBalloon(t);
                }

                _lastTaskStatuses = newStatuses;
            }

            return tasks.Any(t =>
                (t.Status == "Failed" || t.Status == "TimedOut") &&
                t.CompletedAt >= cutoff);
        }
        catch { return false; }
    }

    private void ShowBalloon(TaskPoll t)
    {
        var icon  = t.Status == "Succeeded" ? ToolTipIcon.Info : ToolTipIcon.Warning;
        var title = $"{(t.Status == "Succeeded" ? "✅" : "❌")} {t.Type ?? "Task"}";
        var text  = t.Status == "Succeeded"
            ? "Completed successfully."
            : $"Failed: {t.Error?.Truncate(80) ?? "unknown error"}";

        _trayIcon.ShowBalloonTip(4000, title, text, icon);
    }

    private void OnOpenActivity(object? sender, EventArgs e)
    {
        if (_activityWindow is { IsDisposed: false })
        {
            _activityWindow.BringToFront();
            return;
        }

        _activityWindow = new ActivityWindow(_http);
        _activityWindow.Show();
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _trayIcon.Visible = false;
        _timer.Dispose();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Dispose();
            _timer.Dispose();
            _http.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class TaskPoll
    {
        public string    Id          { get; set; } = "";
        public string?   Type        { get; set; }
        public string?   Status      { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string?   Error       { get; set; }
    }
}

internal static class StringExtensions
{
    public static string Truncate(this string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}

