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
    private readonly HttpClient    _http = CreateDispatcherClient();
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(30));

    private ActivityWindow? _activityWindow;

    // Track last-seen task statuses to detect transitions
    private Dictionary<string, string> _lastTaskStatuses = new();
    private bool _firstPoll = true;

    // Health transition tracking — suppress balloons on very first poll
    private bool  _firstHealthPoll    = true;
    private bool  _prevDispatcherOk   = true;
    private bool  _prevBridgeOk       = true;

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

        // Balloon on health state transitions (skip the very first poll)
        if (!_firstHealthPoll)
        {
            if (!dispatcherOk && _prevDispatcherOk)
                _trayIcon.ShowBalloonTip(5000, "⚠ Daeanne — Dispatcher Down",
                    "Dispatcher is unreachable. Tasks will not be dispatched until it restarts.",
                    ToolTipIcon.Warning);

            if (!bridgeOk && _prevBridgeOk)
                _trayIcon.ShowBalloonTip(5000, "⚠ Daeanne — Bridge Down",
                    "Bridge is unreachable. Emails and SMS will not be sent until it restarts.",
                    ToolTipIcon.Warning);

            if (dispatcherOk && !_prevDispatcherOk)
                _trayIcon.ShowBalloonTip(3000, "✅ Daeanne — Dispatcher Back",
                    "Dispatcher is reachable again.", ToolTipIcon.Info);

            if (bridgeOk && !_prevBridgeOk)
                _trayIcon.ShowBalloonTip(3000, "✅ Daeanne — Bridge Back",
                    "Bridge is reachable again.", ToolTipIcon.Info);
        }

        _firstHealthPoll  = false;
        _prevDispatcherOk = dispatcherOk;
        _prevBridgeOk     = bridgeOk;
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
                // Detect transitions to terminal/blocked states since last poll → balloon tip
                foreach (var t in tasks)
                {
                    static bool IsNotableStatus(string? s) =>
                        s is "Succeeded" or "Failed" or "TimedOut" or "Blocked";

                    var isNewlyNotable =
                        IsNotableStatus(t.Status) &&
                        (!_lastTaskStatuses.TryGetValue(t.Id, out var prev) || !IsNotableStatus(prev));

                    if (isNewlyNotable)
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
        // Auth-expired tasks get a specific actionable balloon
        if (t.Status == "Blocked" &&
            t.Error?.Contains("Copilot auth expired", StringComparison.OrdinalIgnoreCase) == true)
        {
            _trayIcon.ShowBalloonTip(6000,
                $"🔐 Copilot Auth Expired — {t.Type ?? "Task"}",
                "Run /login in the Copilot CLI, then promote the task to Pending.",
                ToolTipIcon.Warning);
            return;
        }

        // Non-auth Blocked tasks are parked intentionally by Daeanne — no balloon needed
        if (t.Status == "Blocked")
            return;

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

    /// <summary>
    /// Creates an HttpClient pre-configured with the Dispatcher API key when available.
    /// Key is read from ~/.daeanne/secrets/dispatcher-api-key.txt — same file used by Daeanne.
    /// </summary>
    private static HttpClient CreateDispatcherClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        var keyFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".daeanne", "secrets", "dispatcher-api-key.txt");

        if (File.Exists(keyFile))
        {
            var key = File.ReadAllText(keyFile).Trim();
            if (!string.IsNullOrEmpty(key))
                client.DefaultRequestHeaders.Add("X-Daeanne-Key", key);
        }

        return client;
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

