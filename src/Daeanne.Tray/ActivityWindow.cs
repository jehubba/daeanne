using System.Text.Json;

namespace Daeanne.Tray;

/// <summary>
/// Dashboard window — system status, live stats, cron schedule, and recent task history.
/// </summary>
internal class ActivityWindow : Form
{
    private readonly HttpClient _http;

    // ── Header ────────────────────────────────────────────────────────────────
    private readonly Label _dispatcherDot;
    private readonly Label _dispatcherLabel;
    private readonly Label _bridgeDot;
    private readonly Label _bridgeLabel;
    private readonly Label _daeanneD;
    private readonly Label _daeanneLabel;

    // ── Stats strip: system ───────────────────────────────────────────────────
    private readonly Label  _statRunning;
    private readonly Label  _statToday;
    private readonly Label  _statSuccessRate;
    private readonly Panel  _statusBar;          // GDI+ painted segmented bar

    // ── Stats strip: schedule ─────────────────────────────────────────────────
    private readonly ListView _cronList;

    // ── Main panel: task list + detail ───────────────────────────────────────
    private readonly ListView _taskList;
    private readonly TextBox  _filterBox;
    private readonly TextBox  _detailBox;
    private readonly Label    _lastUpdated;

    // ── Sort / filter state ───────────────────────────────────────────────────
    private List<TaskSummary> _allTasks = [];
    private int  _sortCol = 2;      // default: Started
    private bool _sortAsc = false;  // default: newest first

    // ── Auto-refresh ──────────────────────────────────────────────────────────
    private readonly System.Windows.Forms.Timer _autoRefreshTimer;
    private string? _lastRefreshError;

    // cached for the status bar painter
    private int _cntRunning, _cntPending, _cntSucceeded, _cntFailed, _cntTotal;

    public ActivityWindow(HttpClient http)
    {
        _http = http;

        Text            = "Daeanne";
        Size            = new Size(1100, 700);
        MinimumSize     = new Size(820, 520);
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = DashboardTheme.BgBase;
        ForeColor       = DashboardTheme.TextPrimary;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar   = false;

        var layout = new DashboardLayout();

        // Assign named controls from layout
        _dispatcherDot   = layout.DispatcherDot;
        _dispatcherLabel = layout.DispatcherLabel;
        _bridgeDot       = layout.BridgeDot;
        _bridgeLabel     = layout.BridgeLabel;
        _daeanneD        = layout.DaeanneD;
        _daeanneLabel    = layout.DaeanneLabel;
        _statRunning     = layout.StatRunning;
        _statToday       = layout.StatToday;
        _statSuccessRate = layout.StatSuccessRate;
        _statusBar       = layout.StatusBar;
        _cronList        = layout.CronList;
        _filterBox       = layout.FilterBox;
        _taskList        = layout.TaskList;
        _detailBox       = layout.DetailBox;
        _lastUpdated     = layout.LastUpdated;

        // Wire events that capture form state
        layout.RefreshButton.Click += async (_, _) => await RefreshAsync();

        _statusBar.Paint += (_, e) =>
            DashboardPainter.PaintStatusBar(e, _statusBar.Width, _statusBar.Height,
                _cntRunning, _cntPending, _cntSucceeded, _cntFailed, _cntTotal);

        _filterBox.TextChanged += (_, _) => RepopulateTaskList();

        _taskList.Resize += (_, _) =>
        {
            int used = _taskList.Columns.Cast<ColumnHeader>().Take(4).Sum(c => c.Width);
            int fill = _taskList.ClientSize.Width - used - 2;
            if (fill > 80) _taskList.Columns[4].Width = fill;
        };

        _taskList.ColumnClick += (_, e) =>
        {
            if (_sortCol == e.Column) _sortAsc = !_sortAsc;
            else { _sortCol = e.Column; _sortAsc = true; }
            RepopulateTaskList();
        };

        var errorTip = new ToolTip { AutoPopDelay = 10000, InitialDelay = 400 };
        _taskList.MouseMove += (_, me) =>
        {
            var hit = _taskList.HitTest(me.Location);
            if (hit.SubItem is not null && hit.Item?.Tag is TaskSummary ts)
                errorTip.SetToolTip(_taskList, string.IsNullOrWhiteSpace(ts.Error) ? ts.AgentResponse ?? "" : ts.Error ?? "");
            else
                errorTip.SetToolTip(_taskList, "");
        };

        _taskList.DoubleClick          += OnOpenWorkDir;
        _taskList.SelectedIndexChanged += OnTaskSelected;

        AttachTaskContextMenu();

        // Dock order: Fill last, Top panels stack top-down
        Controls.Add(layout.MainSplit);
        Controls.Add(layout.FilterBar);
        Controls.Add(layout.StatsStrip);
        Controls.Add(layout.Header);

        Shown += (_, _) =>
        {
            BeginInvoke(() =>
            {
                try { layout.MainSplit.SplitterDistance = Math.Max(25, ClientSize.Height - 200); } catch { }
                _ = RefreshAsync();
            });
        };

        _autoRefreshTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _autoRefreshTimer.Tick += async (_, _) => await RefreshAsync();
        _autoRefreshTimer.Start();

        FormClosed += (_, _) => _autoRefreshTimer.Dispose();
    }

    // ── Section / label factories (see DashboardTheme) ───────────────────────

    // ── Context menu (unchanged logic, rewired to new task list) ──────────────

    private void AttachTaskContextMenu()
    {
        var rowMenu = new ContextMenuStrip
        {
            BackColor  = Color.FromArgb(40, 40, 46),
            ForeColor  = DashboardTheme.TextPrimary,
            RenderMode = ToolStripRenderMode.System
        };
        rowMenu.Items.Add("View Plan",                 null, OnViewPlan);
        rowMenu.Items.Add("Open Work Dir",             null, OnOpenWorkDir);
        rowMenu.Items.Add("Copy Task ID",              null, OnCopyTaskId);
        rowMenu.Items.Add(new ToolStripSeparator());
        var troubleshootItem = (ToolStripMenuItem)rowMenu.Items.Add("Troubleshoot with Daeanne", null, OnTroubleshoot);
        var viewPlanItem     = (ToolStripMenuItem)rowMenu.Items[0];

        rowMenu.Opening += (_, _) =>
        {
            var t = SelectedTask();
            viewPlanItem.Enabled     = ResolveWorkDir(t) is not null;
            troubleshootItem.Enabled = t?.Status is "Failed" or "TimedOut" or "Succeeded" or "Partial" or "Running" or "Awaiting";
        };

        _taskList.ContextMenuStrip = rowMenu;
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    public async Task RefreshAsync()
    {
        _lastRefreshError = null;
        await Task.WhenAll(
            RefreshServicesAsync(),
            RefreshTasksAsync(),
            RefreshScheduleAsync());

        if (_lastRefreshError is not null)
        {
            _lastUpdated.ForeColor = Color.FromArgb(255, 150, 60);
            _lastUpdated.Text = $"⚠ {DateTime.Now:HH:mm:ss}: {_lastRefreshError}  ";
        }
        else
        {
            _lastUpdated.ForeColor = DashboardTheme.TextMuted;
            _lastUpdated.Text = $"Updated {DateTime.Now:HH:mm:ss}  ";
        }
    }

    private async Task RefreshServicesAsync()
    {
        var (dispOk, _)   = await CheckAsync("http://127.0.0.1:47777/health");
        var (bridgeOk, _) = await CheckAsync("http://127.0.0.1:47778/health");
        bool daeanneOk    = await CheckDaeanneHealthAsync();

        UpdateServiceRow(_dispatcherDot, _dispatcherLabel, "Dispatcher", dispOk);
        UpdateServiceRow(_bridgeDot,     _bridgeLabel,     "Bridge",     bridgeOk);
        UpdateDaeanneRow(daeanneOk);
    }

    private async Task<bool> CheckDaeanneHealthAsync()
    {
        try
        {
            var json  = await _http.GetStringAsync("http://127.0.0.1:47777/tasks?take=50");
            var tasks = JsonSerializer.Deserialize<List<TaskSummary>>(json, DashboardHelpers.JsonOpts) ?? [];
            var cutoff = DateTime.UtcNow.AddHours(-1);
            return !tasks.Any(t =>
                (t.Status == "Failed" || t.Status == "TimedOut") &&
                t.CompletedAt >= cutoff);
        }
        catch { return false; }
    }

    private async Task RefreshTasksAsync()
    {
        try
        {
            var json  = await _http.GetStringAsync("http://127.0.0.1:47777/tasks?take=50");
            var tasks = JsonSerializer.Deserialize<List<TaskSummary>>(json, DashboardHelpers.JsonOpts) ?? [];

            // Compute stats for sidebar — exclude Test tasks so pipeline probes
            // don't distort the functional success rate.
            var functional = tasks.Where(t => t.Type != "Test").ToList();
            var cutoff24h  = DateTime.UtcNow.AddHours(-24);
            var cutoff7d   = DateTime.UtcNow.AddDays(-7);

            _cntRunning   = functional.Count(t => t.Status == "Running");
            _cntPending   = functional.Count(t => t.Status is "Pending" or "Awaiting");
            _cntSucceeded = functional.Count(t => t.Status == "Succeeded");
            _cntFailed    = functional.Count(t => t.Status is "Failed" or "TimedOut");
            _cntTotal     = _cntRunning + _cntPending + _cntSucceeded + _cntFailed;

            var today    = functional.Count(t => t.CreatedAt >= cutoff24h);
            var week     = functional.Where(t => t.CreatedAt >= cutoff7d && t.Status is "Succeeded" or "Failed" or "TimedOut").ToList();
            var wSucc    = week.Count(t => t.Status == "Succeeded");
            var rate     = week.Count > 0 ? (int)Math.Round(100.0 * wSucc / week.Count) : 100;

            _statRunning.Text     = _cntRunning.ToString();
            _statToday.Text       = today.ToString();
            _statSuccessRate.Text = $"{rate}%";
            _statusBar.Invalidate();

            // Store full list, then apply sort/filter
            _allTasks = tasks;
            RepopulateTaskList();
        }
        catch (Exception ex) { _lastRefreshError = $"Tasks: {ex.Message.Truncate(60)}"; }
    }

    private void RepopulateTaskList()
    {
        var filter = _filterBox.Text.Trim().ToLower();
        IEnumerable<TaskSummary> data = _allTasks;

        if (!string.IsNullOrEmpty(filter))
            data = data.Where(t =>
                (t.Type          ?? "").ToLower().Contains(filter) ||
                (t.Status        ?? "").ToLower().Contains(filter) ||
                (t.AgentResponse ?? "").ToLower().Contains(filter) ||
                (t.Error         ?? "").ToLower().Contains(filter));

        data = _sortCol switch
        {
            0 => _sortAsc ? data.OrderBy(t => t.Type)         : data.OrderByDescending(t => t.Type),
            1 => _sortAsc ? data.OrderBy(t => t.Status)       : data.OrderByDescending(t => t.Status),
            2 => _sortAsc ? data.OrderBy(t => t.StartedAt)    : data.OrderByDescending(t => t.StartedAt),
            3 => _sortAsc ? data.OrderBy(t => t.CompletedAt.HasValue && t.StartedAt.HasValue
                                ? t.CompletedAt.Value - t.StartedAt.Value : TimeSpan.Zero)
                          : data.OrderByDescending(t => t.CompletedAt.HasValue && t.StartedAt.HasValue
                                ? t.CompletedAt.Value - t.StartedAt.Value : TimeSpan.Zero),
            4 => _sortAsc ? data.OrderBy(t => t.AgentResponse ?? t.Error)
                          : data.OrderByDescending(t => t.AgentResponse ?? t.Error),
            _ => data   // no sort
        };

        _taskList.BeginUpdate();
        _taskList.Items.Clear();
        foreach (var t in data)
        {
            var duration = t.CompletedAt.HasValue && t.StartedAt.HasValue
                ? FormatDuration(t.CompletedAt.Value - t.StartedAt.Value)
                : t.StartedAt.HasValue ? "running…" : "—";

            var note       = string.IsNullOrWhiteSpace(t.Error) ? t.AgentResponse : t.Error;
            var statusText = t.AgentReported && t.Status == "Succeeded" ? "Succeeded ✔" : t.Status ?? "—";

            var item = new ListViewItem(t.Type ?? "—");
            item.SubItems.Add(statusText);
            item.SubItems.Add(t.StartedAt?.ToLocalTime().ToString("MM/dd HH:mm:ss") ?? "—");
            item.SubItems.Add(duration);
            item.SubItems.Add(note ?? "");
            item.Tag = t;

            item.ForeColor = (t.Status ?? "") switch
            {
                "Succeeded" when t.AgentReported => Color.FromArgb(80,  220, 100),
                "Succeeded"                      => Color.FromArgb(110, 165, 110),
                "Partial"                        => Color.FromArgb(255, 185,  60),
                "Failed" or "TimedOut"           => Color.FromArgb(255, 100,  80),
                "Running"                        => Color.FromArgb(100, 180, 255),
                "Awaiting"                       => Color.FromArgb(200, 160, 255),
                "Deferred" or "Blocked"
                             or "Future"         => Color.FromArgb(150, 150, 170),
                _                                => DashboardTheme.TextPrimary
            };
            item.SubItems[4].ForeColor = string.IsNullOrWhiteSpace(t.Error)
                ? Color.FromArgb(150, 150, 160)
                : Color.FromArgb(255, 130, 110);

            _taskList.Items.Add(item);
        }
        _taskList.EndUpdate();
    }

    private async Task RefreshScheduleAsync()
    {
        try
        {
            var json = await _http.GetStringAsync("http://127.0.0.1:47777/scheduler/crons");
            var jobs = JsonSerializer.Deserialize<List<CronJob>>(json, DashboardHelpers.JsonOpts) ?? [];

            _cronList.BeginUpdate();
            _cronList.Items.Clear();
            foreach (var job in jobs.OrderBy(j => j.NextRunAt))
            {
                var item = new ListViewItem("");   // col 0 = dot (painted)
                item.SubItems.Add(job.Name ?? "—");
                item.SubItems.Add(FormatRelative(job.NextRunAt));
                item.SubItems.Add(job.TaskType ?? "—");
                item.Tag = job;

                // Colour next-run by urgency
                var nextFg = job.NextRunAt <= DateTime.UtcNow
                    ? Color.FromArgb(255, 100, 80)       // overdue
                    : job.NextRunAt <= DateTime.UtcNow.AddHours(2)
                        ? Color.FromArgb(255, 185, 60)   // soon
                        : DashboardTheme.TextMuted;
                item.SubItems[2].ForeColor = nextFg;

                _cronList.Items.Add(item);
            }
            _cronList.EndUpdate();

            // Auto-fit cron list height to content
            if (_cronList.Items.Count > 0)
            {
                var rowH = _cronList.GetItemRect(0).Height;
                _cronList.Height = Math.Min(rowH * (_cronList.Items.Count + 1) + 24, 200);
            }
        }
        catch (Exception ex) { _lastRefreshError = $"Schedule: {ex.Message.Truncate(60)}"; }
    }

    // ── Event handlers (unchanged logic) ─────────────────────────────────────

    private void OnTaskSelected(object? sender, EventArgs e)
    {
        if (_taskList.SelectedItems.Count == 0) { _detailBox.Text = ""; return; }
        var t = _taskList.SelectedItems[0].Tag as TaskSummary;
        if (t is null) return;

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(t.Id))
            lines.Add($"Task ID  : {t.Id}");
        lines.Add($"Reported : {(t.AgentReported ? "✔ Daeanne confirmed" : "⚠ Auto-marked by dispatcher")}");
        if (!string.IsNullOrWhiteSpace(t.WorkDir))
            lines.Add($"Work Dir : {t.WorkDir}");
        if (!string.IsNullOrWhiteSpace(t.Error))
            lines.Add($"Error    : {t.Error}");
        if (!string.IsNullOrWhiteSpace(t.AgentResponse))
            lines.Add($"Result   : {t.AgentResponse}");

        _detailBox.Text = string.Join(Environment.NewLine, lines);

        if (!string.IsNullOrWhiteSpace(t.Id))
            _ = LoadLinkedDiagnosticsAsync(t.Id);
    }

    private async Task LoadLinkedDiagnosticsAsync(string taskId)
    {
        try
        {
            var json = await _http.GetStringAsync("http://127.0.0.1:47777/tasks?type=Diagnostic&take=50");
            var all  = JsonSerializer.Deserialize<List<TaskSummary>>(json, DashboardHelpers.JsonOpts) ?? [];
            var linked = all.Where(d => d.Prompt?.Contains(taskId, StringComparison.OrdinalIgnoreCase) == true).ToList();
            if (linked.Count == 0) return;

            var extras = linked.Select(d =>
                $"🔍 Diagnostic {d.Status} {d.CompletedAt?.ToLocalTime():MM/dd HH:mm}: {d.AgentResponse ?? d.Error ?? "no result"}");
            _detailBox.Text += Environment.NewLine + string.Join(Environment.NewLine, extras);
        }
        catch { }
    }

    private void OnViewPlan(object? sender, EventArgs e)
    {
        var t   = SelectedTask();
        var dir = ResolveWorkDir(t);
        if (dir is null) { MessageBox.Show("No work directory found.", "Daeanne", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        new PlanViewWindow(t!.Id ?? "unknown", dir).Show();
    }

    private void OnOpenWorkDir(object? sender, EventArgs e)
    {
        var dir = ResolveWorkDir(SelectedTask());
        if (dir is not null) System.Diagnostics.Process.Start("explorer.exe", dir);
        else MessageBox.Show("No work directory found.", "Daeanne", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnCopyTaskId(object? sender, EventArgs e)
    {
        if (SelectedTask()?.Id is { Length: > 0 } id) Clipboard.SetText(id);
    }

    private async void OnTroubleshoot(object? sender, EventArgs e)
    {
        var t = SelectedTask();
        if (t is null) return;

        var prompt = $"""
            Diagnostic task: investigate why task {t.Id} ({t.Type}) ended with status {t.Status}.
            Error (if any): {t.Error ?? "none"}
            Work directory: {t.WorkDir ?? "unknown"}

            Please:
            1. Read the task work directory and daeanne-plan.md if present
            2. Identify the root cause
            3. If recoverable, resubmit as a new task with corrections
            4. If not recoverable, email Jeffrey a concise root cause summary
            """;
        try
        {
            var body = JsonSerializer.Serialize(new { type = "Diagnostic", prompt });
            var resp = await _http.PostAsync(
                "http://127.0.0.1:47777/tasks",
                new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            MessageBox.Show(resp.IsSuccessStatusCode
                ? "Diagnostic task created — Daeanne will investigate."
                : $"Failed: {resp.StatusCode}",
                "Daeanne", MessageBoxButtons.OK,
                resp.IsSuccessStatusCode ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not reach Dispatcher: {ex.Message}", "Daeanne", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private TaskSummary? SelectedTask() =>
        _taskList.SelectedItems.Count > 0 ? _taskList.SelectedItems[0].Tag as TaskSummary : null;

    private async Task<(bool ok, string? body)> CheckAsync(string url)
    {
        try { var r = await _http.GetAsync(url); return (r.IsSuccessStatusCode, null); }
        catch { return (false, null); }
    }

    private static void UpdateServiceRow(Label dot, Label label, string name, bool ok)
    {
        dot.BackColor   = ok ? Color.FromArgb(70, 200, 80) : Color.FromArgb(220, 60, 60);
        label.Text      = $"{name}  ·  {(ok ? "healthy" : "unreachable")}";
        label.ForeColor = ok ? Color.FromArgb(190, 255, 190) : Color.FromArgb(255, 150, 130);
    }

    private void UpdateDaeanneRow(bool ok)
    {
        _daeanneD.BackColor = ok ? Color.FromArgb(70, 200, 80) : Color.FromArgb(230, 140, 30);
        _daeanneLabel.Text  = $"Daeanne  ·  {(ok ? "healthy" : "recent failures")}";
        _daeanneLabel.ForeColor = ok ? Color.FromArgb(190, 255, 190) : Color.FromArgb(255, 210, 140);
    }

    private static string FormatDuration(TimeSpan ts)    => DashboardHelpers.FormatDuration(ts);
    private static string FormatRelative(DateTime? utc)  => DashboardHelpers.FormatRelative(utc);
    private static string? ResolveWorkDir(TaskSummary? t) => DashboardHelpers.ResolveWorkDir(t);

}
