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
    private readonly TextBox  _detailBox;
    private readonly Label    _lastUpdated;

    public ActivityWindow(HttpClient http)
    {
        _http = http;

        Text            = "Daeanne — Activity";
        Size            = new Size(660, 480);
        MinimumSize     = new Size(500, 360);
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = Color.FromArgb(28, 28, 30);
        ForeColor       = Color.White;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar   = false;

        // ── Service status header ─────────────────────────────────────────────
        var headerPanel = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 52,
            BackColor = Color.FromArgb(38, 38, 42),
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
            OwnerDraw     = true,
            BackColor     = Color.FromArgb(28, 28, 30),
            ForeColor     = Color.FromArgb(220, 220, 220),
            BorderStyle   = BorderStyle.None,
            Font          = new Font("Segoe UI", 9f)
        };
        _taskList.Columns.Add("Type",     155);
        _taskList.Columns.Add("Status",    90);
        _taskList.Columns.Add("Started",  128);
        _taskList.Columns.Add("Duration",  80);
        _taskList.Columns.Add("Note",     185);

        // Owner-draw: dark column headers
        _taskList.DrawColumnHeader += (_, e) =>
        {
            using var bgBrush = new SolidBrush(Color.FromArgb(50, 50, 54));
            e.Graphics.FillRectangle(bgBrush, e.Bounds);
            using var pen = new Pen(Color.FromArgb(68, 68, 72));
            e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom - 1);
            e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", _taskList.Font,
                Rectangle.Inflate(e.Bounds, -4, 0),
                Color.FromArgb(190, 190, 195),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };

        // Owner-draw: suppress default item background (DrawSubItem handles it)
        _taskList.DrawItem += (_, e) => { e.DrawDefault = false; };

        // Owner-draw: rows with alternating shade + custom selection color
        _taskList.DrawSubItem += (_, e) =>
        {
            bool selected = e.Item.Selected;
            bool alt      = e.Item.Index % 2 == 1;
            var bg = selected
                ? Color.FromArgb(42, 88, 148)
                : alt ? Color.FromArgb(34, 34, 38) : Color.FromArgb(28, 28, 30);
            using var bgBrush = new SolidBrush(bg);
            e.Graphics.FillRectangle(bgBrush, e.Bounds);

            // Per-subitem color when not selected; fall back to item color
            var fg = selected ? Color.White
                : (e.SubItem?.ForeColor is { } c && c != Color.Empty && c != e.Item.ForeColor
                    ? c : e.Item.ForeColor);
            TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", _taskList.Font,
                Rectangle.Inflate(e.Bounds, -4, 0),
                fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };

        var rowMenu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(40, 40, 44),
            ForeColor = Color.FromArgb(220, 220, 220),
            RenderMode = ToolStripRenderMode.System
        };
        rowMenu.Items.Add("Open Work Dir",             null, OnOpenWorkDir);
        rowMenu.Items.Add("Copy Task ID",              null, OnCopyTaskId);
        rowMenu.Items.Add(new ToolStripSeparator());
        var troubleshootItem = (ToolStripMenuItem)rowMenu.Items.Add("Troubleshoot with Daeanne", null, OnTroubleshoot);

        // Troubleshoot available on any terminal task — Succeeded ≠ goal achieved
        rowMenu.Opening += (_, _) =>
        {
            var t = SelectedTask();
            troubleshootItem.Enabled = t?.Status is "Failed" or "TimedOut" or "Succeeded" or "Partial";
        };

        // Tooltip on Note column: show full text
        var errorTip = new ToolTip { AutoPopDelay = 10000, InitialDelay = 400 };
        _taskList.MouseMove += (_, me) =>
        {
            var hit = _taskList.HitTest(me.Location);
            if (hit.SubItem is not null && hit.Item?.Tag is TaskSummary ts)
            {
                var tip = string.IsNullOrWhiteSpace(ts.Error) ? ts.AgentResponse : ts.Error;
                errorTip.SetToolTip(_taskList, tip ?? "");
            }
            else
                errorTip.SetToolTip(_taskList, "");
        };

        _taskList.ContextMenuStrip = rowMenu;
        _taskList.DoubleClick     += OnOpenWorkDir;
        _taskList.SelectedIndexChanged += OnTaskSelected;

        // ── Detail panel ──────────────────────────────────────────────────────
        _detailBox = new TextBox
        {
            Dock        = DockStyle.Bottom,
            Height      = 80,
            Multiline   = true,
            ReadOnly    = true,
            ScrollBars  = ScrollBars.Vertical,
            BackColor   = Color.FromArgb(22, 22, 22),
            ForeColor   = Color.FromArgb(200, 200, 200),
            BorderStyle = BorderStyle.None,
            Font        = new Font("Segoe UI", 8.5f),
            Padding     = new Padding(6)
        };

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
            BackColor = Color.FromArgb(50, 50, 54),
            ForeColor = Color.FromArgb(210, 210, 215),
            Font      = new Font("Segoe UI", 9f)
        };
        refreshBtn.FlatAppearance.BorderSize = 0;
        refreshBtn.Click += async (_, _) => await RefreshAsync();

        Controls.Add(_taskList);
        Controls.Add(_detailBox);
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

                // Note column: error if present, otherwise Daeanne's self-reported response
                var note = string.IsNullOrWhiteSpace(t.Error)
                    ? t.AgentResponse
                    : t.Error;

                var item = new ListViewItem(t.Type ?? "—");
                item.SubItems.Add(t.Status ?? "—");
                item.SubItems.Add(t.StartedAt?.ToLocalTime().ToString("MM/dd HH:mm:ss") ?? "—");
                item.SubItems.Add(duration);
                item.SubItems.Add(note ?? "");
                item.Tag = t;   // store full task for detail panel / context menu

                item.ForeColor = (t.Status ?? "") switch
                {
                    "Succeeded" => Color.FromArgb(100, 220, 100),
                    "Partial"   => Color.FromArgb(255, 185, 60),
                    "Failed" or "TimedOut" => Color.FromArgb(255, 100, 80),
                    "Running"  => Color.FromArgb(100, 180, 255),
                    "Awaiting" => Color.FromArgb(200, 160, 255),
                    _ => Color.FromArgb(200, 200, 200)
                };

                // Dim the note text slightly relative to status color
                item.SubItems[4].ForeColor = string.IsNullOrWhiteSpace(t.Error)
                    ? Color.FromArgb(160, 160, 165)
                    : Color.FromArgb(255, 130, 110);

                _taskList.Items.Add(item);
            }

            _taskList.EndUpdate();
        }
        catch
        {
            // Dispatcher unreachable — leave list as-is
        }
    }

    private void OnTaskSelected(object? sender, EventArgs e)
    {
        if (_taskList.SelectedItems.Count == 0) { _detailBox.Text = ""; return; }
        var t = _taskList.SelectedItems[0].Tag as TaskSummary;
        if (t is null) return;

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(t.Id))
            lines.Add($"Task ID : {t.Id}");
        if (!string.IsNullOrWhiteSpace(t.WorkDir))
            lines.Add($"Work Dir: {t.WorkDir}");
        if (!string.IsNullOrWhiteSpace(t.Error))
            lines.Add($"Error   : {t.Error}");

        // Parse resultJson for Daeanne's response summary
        if (!string.IsNullOrWhiteSpace(t.ResultJson))
        {
            try
            {
                var r = JsonSerializer.Deserialize<JsonElement>(t.ResultJson);
                if (r.TryGetProperty("response", out var resp) && resp.GetString() is { } s)
                    lines.Add($"Result  : {s}");
            }
            catch { /* not parseable — skip */ }
        }

        _detailBox.Text = string.Join(Environment.NewLine, lines);
    }

    private void OnOpenWorkDir(object? sender, EventArgs e)
    {
        var t = SelectedTask();
        if (t?.WorkDir is { } dir && Directory.Exists(dir))
            System.Diagnostics.Process.Start("explorer.exe", dir);
        else
            MessageBox.Show("No work directory found for this task.", "Daeanne",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnCopyTaskId(object? sender, EventArgs e)
    {
        var t = SelectedTask();
        if (t?.Id is { Length: > 0 } id)
            Clipboard.SetText(id);
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
                : $"Failed to create task: {resp.StatusCode}",
                "Daeanne", MessageBoxButtons.OK,
                resp.IsSuccessStatusCode ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not reach Dispatcher: {ex.Message}", "Daeanne",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private TaskSummary? SelectedTask() =>
        _taskList.SelectedItems.Count > 0
            ? _taskList.SelectedItems[0].Tag as TaskSummary
            : null;

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
        public string?   Id          { get; set; }
        public string?   Type        { get; set; }
        public string?   Status      { get; set; }
        public DateTime? StartedAt   { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string?   Error       { get; set; }
        public string?   WorkDir     { get; set; }
        public string?   ResultJson  { get; set; }

        // Parsed lazily from ResultJson
        private string? _agentResponse;
        public string? AgentResponse
        {
            get
            {
                if (_agentResponse is not null) return _agentResponse;
                if (string.IsNullOrWhiteSpace(ResultJson)) return null;
                try
                {
                    var r = JsonSerializer.Deserialize<JsonElement>(ResultJson);
                    if (r.TryGetProperty("response", out var v))
                        _agentResponse = v.GetString();
                }
                catch { /* ignore */ }
                return _agentResponse;
            }
        }
    }
}
