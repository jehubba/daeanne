using System.Text.Json;

namespace Daeanne.Tray;

/// <summary>
/// Shows service health status and recent agent task history.
/// </summary>
internal class ActivityWindow : Form
{
    private readonly HttpClient _http;

    // Service status header
    private readonly Label _dispatcherDot;
    private readonly Label _dispatcherLabel;
    private readonly Label _bridgeDot;
    private readonly Label _bridgeLabel;

    // Task list
    private readonly ListView _taskList;
    private readonly Label _lastUpdated;

    public ActivityWindow(HttpClient http)
    {
        _http = http;

        Text            = "Daeanne — Activity";
        Size            = new Size(660, 480);
        MinimumSize     = new Size(500, 360);
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = Color.FromArgb(30, 30, 30);
        ForeColor       = Color.White;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar   = false;

        // ── Service status header ─────────────────────────────────────────────
        var headerPanel = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 52,
            BackColor = Color.FromArgb(45, 45, 45),
            Padding   = new Padding(12, 0, 12, 0)
        };

        _dispatcherDot   = MakeDot();
        _dispatcherLabel = MakeServiceLabel("Dispatcher  ·  checking…");
        _bridgeDot       = MakeDot();
        _bridgeLabel     = MakeServiceLabel("Bridge  ·  checking…");

        var headerFlow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
        };
        headerFlow.Controls.Add(_dispatcherDot);
        headerFlow.Controls.Add(_dispatcherLabel);
        var sep = new Label { Text = "│", ForeColor = Color.Gray, Width = 20, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false };
        sep.Font = new Font(sep.Font.FontFamily, 14);
        headerFlow.Controls.Add(sep);
        headerFlow.Controls.Add(_bridgeDot);
        headerFlow.Controls.Add(_bridgeLabel);
        headerPanel.Controls.Add(headerFlow);

        // ── Task list ─────────────────────────────────────────────────────────
        _taskList = new ListView
        {
            Dock          = DockStyle.Fill,
            View          = View.Details,
            FullRowSelect = true,
            GridLines     = false,
            BackColor     = Color.FromArgb(30, 30, 30),
            ForeColor     = Color.White,
            BorderStyle   = BorderStyle.None,
            Font          = new Font("Segoe UI", 9f)
        };
        _taskList.Columns.Add("Type",     160);
        _taskList.Columns.Add("Status",    90);
        _taskList.Columns.Add("Started",  130);
        _taskList.Columns.Add("Duration", 100);
        _taskList.Columns.Add("Error",    160);

        _taskList.DoubleClick += OnTaskDoubleClick;

        // ── Footer ────────────────────────────────────────────────────────────
        _lastUpdated = new Label
        {
            Dock      = DockStyle.Bottom,
            Height    = 22,
            Text      = "",
            ForeColor = Color.Gray,
            Font      = new Font("Segoe UI", 8f),
            TextAlign = ContentAlignment.MiddleRight,
            Padding   = new Padding(0, 0, 8, 0)
        };

        var refreshBtn = new Button
        {
            Dock      = DockStyle.Bottom,
            Height    = 28,
            Text      = "↻  Refresh",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(55, 55, 55),
            ForeColor = Color.White,
            Font      = new Font("Segoe UI", 9f)
        };
        refreshBtn.FlatAppearance.BorderSize = 0;
        refreshBtn.Click += async (_, _) => await RefreshAsync();

        Controls.Add(_taskList);
        Controls.Add(refreshBtn);
        Controls.Add(_lastUpdated);
        Controls.Add(headerPanel);

        Shown += async (_, _) => await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        await RefreshServicesAsync();
        await RefreshTasksAsync();
        _lastUpdated.Text = $"Updated {DateTime.Now:HH:mm:ss}   ";
    }

    private async Task RefreshServicesAsync()
    {
        var (dispOk, _) = await CheckAsync("http://127.0.0.1:47777/health");
        var (bridgeOk, _) = await CheckAsync("http://127.0.0.1:47778/health");

        UpdateServiceRow(_dispatcherDot, _dispatcherLabel, "Dispatcher", dispOk);
        UpdateServiceRow(_bridgeDot, _bridgeLabel, "Bridge", bridgeOk);
    }

    private async Task RefreshTasksAsync()
    {
        try
        {
            var json = await _http.GetStringAsync("http://127.0.0.1:47777/tasks?take=30");
            var tasks = JsonSerializer.Deserialize<List<TaskSummary>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

            _taskList.BeginUpdate();
            _taskList.Items.Clear();

            foreach (var t in tasks)
            {
                var duration = t.CompletedAt.HasValue && t.StartedAt.HasValue
                    ? FormatDuration(t.CompletedAt.Value - t.StartedAt.Value)
                    : t.StartedAt.HasValue ? "running…" : "—";

                var item = new ListViewItem(t.Type ?? "—");
                item.SubItems.Add(t.Status ?? "—");
                item.SubItems.Add(t.StartedAt?.ToLocalTime().ToString("MM/dd HH:mm:ss") ?? "—");
                item.SubItems.Add(duration);
                item.SubItems.Add(t.Error ?? "");
                item.Tag = t.WorkDir;

                item.ForeColor = (t.Status ?? "") switch
                {
                    "Succeeded" => Color.FromArgb(100, 220, 100),
                    "Failed" or "TimedOut" => Color.FromArgb(255, 100, 80),
                    "Running" => Color.FromArgb(100, 180, 255),
                    "Awaiting" => Color.FromArgb(255, 200, 80),
                    _ => Color.White
                };

                _taskList.Items.Add(item);
            }

            _taskList.EndUpdate();
        }
        catch
        {
            // Dispatcher unreachable — leave list as-is
        }
    }

    private void OnTaskDoubleClick(object? sender, EventArgs e)
    {
        if (_taskList.SelectedItems.Count == 0) return;
        var workDir = _taskList.SelectedItems[0].Tag as string;
        if (!string.IsNullOrWhiteSpace(workDir) && Directory.Exists(workDir))
            System.Diagnostics.Process.Start("explorer.exe", workDir);
    }

    private async Task<(bool ok, string? body)> CheckAsync(string url)
    {
        try
        {
            var resp = await _http.GetAsync(url);
            return (resp.IsSuccessStatusCode, null);
        }
        catch { return (false, null); }
    }

    private static void UpdateServiceRow(Label dot, Label label, string name, bool ok)
    {
        dot.BackColor  = ok ? Color.FromArgb(80, 200, 80) : Color.FromArgb(220, 60, 60);
        label.Text     = $"{name}  ·  {(ok ? "healthy" : "unreachable")}";
        label.ForeColor = ok ? Color.FromArgb(200, 255, 200) : Color.FromArgb(255, 160, 140);
    }

    private static Label MakeDot() => new()
    {
        Width     = 14,
        Height    = 14,
        BackColor = Color.Gray,
        Text      = "",
        Margin    = new Padding(0, 19, 6, 0),
    };

    private static Label MakeServiceLabel(string text) => new()
    {
        Text      = text,
        ForeColor = Color.White,
        AutoSize  = true,
        Font      = new Font("Segoe UI", 9.5f),
        Margin    = new Padding(0, 17, 20, 0)
    };

    private static string FormatDuration(TimeSpan ts) =>
        ts.TotalHours >= 1
            ? $"{ts.Hours}h {ts.Minutes}m"
            : ts.TotalMinutes >= 1
                ? $"{(int)ts.TotalMinutes}m {ts.Seconds}s"
                : $"{ts.Seconds}s";

    // Minimal DTO — matches AgentTask JSON shape from dispatcher
    private sealed class TaskSummary
    {
        public string?   Type        { get; set; }
        public string?   Status      { get; set; }
        public DateTime? StartedAt   { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string?   Error       { get; set; }
        public string?   WorkDir     { get; set; }
    }
}
