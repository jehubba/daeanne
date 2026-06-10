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

    // ── Sidebar: stats ────────────────────────────────────────────────────────
    private readonly Label  _statRunning;
    private readonly Label  _statToday;
    private readonly Label  _statSuccessRate;
    private readonly Panel  _statusBar;          // GDI+ painted segmented bar

    // ── Sidebar: schedule ─────────────────────────────────────────────────────
    private readonly ListView _cronList;

    // ── Main panel: task list + detail ───────────────────────────────────────
    private readonly ListView _taskList;
    private readonly TextBox  _detailBox;
    private readonly Label    _lastUpdated;

    // ── Design tokens ─────────────────────────────────────────────────────────
    private static readonly Color BgBase    = Color.FromArgb(22, 22, 26);
    private static readonly Color BgPanel   = Color.FromArgb(30, 30, 34);
    private static readonly Color BgHeader  = Color.FromArgb(38, 38, 44);
    private static readonly Color BgSidebar = Color.FromArgb(26, 26, 30);
    private static readonly Color BgDetail  = Color.FromArgb(18, 18, 20);
    private static readonly Color Separator = Color.FromArgb(50, 50, 56);
    private static readonly Color TextPrimary = Color.FromArgb(220, 220, 228);
    private static readonly Color TextMuted   = Color.FromArgb(130, 130, 140);
    private static readonly Font  FontUi     = new("Segoe UI", 9f);
    private static readonly Font  FontSmall  = new("Segoe UI", 8f);
    private static readonly Font  FontMono   = MakeMonoFont();

    private static Font MakeMonoFont()
    {
        try   { return new Font("Cascadia Mono", 8.5f, FontStyle.Regular); }
        catch { return new Font("Consolas",      8.5f, FontStyle.Regular); }
    }

    // cached for the status bar painter
    private int _cntRunning, _cntPending, _cntSucceeded, _cntFailed, _cntTotal;

    public ActivityWindow(HttpClient http)
    {
        _http = http;

        Text            = "Daeanne";
        Size            = new Size(1100, 700);
        MinimumSize     = new Size(820, 520);
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = BgBase;
        ForeColor       = TextPrimary;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar   = false;

        // ── Header ────────────────────────────────────────────────────────────
        var header = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 52,
            BackColor = BgHeader,
            Padding   = new Padding(16, 0, 12, 0)
        };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(Separator);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        _dispatcherDot   = MakeDot();
        _dispatcherLabel = MakeServiceLabel("Dispatcher  ·  checking…");
        _bridgeDot       = MakeDot();
        _bridgeLabel     = MakeServiceLabel("Bridge  ·  checking…");
        _daeanneD        = MakeDot();
        _daeanneLabel    = MakeServiceLabel("Daeanne  ·  checking…");

        var refreshBtn = new Button
        {
            Text      = "↻  Refresh",
            Dock      = DockStyle.Right,
            Width     = 90,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(48, 48, 56),
            ForeColor = Color.FromArgb(200, 200, 210),
            Font      = FontUi,
            Cursor    = Cursors.Hand
        };
        refreshBtn.FlatAppearance.BorderSize = 0;
        refreshBtn.Click += async (_, _) => await RefreshAsync();

        var headerFlow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
        };
        headerFlow.Controls.Add(_dispatcherDot);
        headerFlow.Controls.Add(_dispatcherLabel);
        headerFlow.Controls.Add(MakeSep());
        headerFlow.Controls.Add(_bridgeDot);
        headerFlow.Controls.Add(_bridgeLabel);
        headerFlow.Controls.Add(MakeSep());
        headerFlow.Controls.Add(_daeanneD);
        headerFlow.Controls.Add(_daeanneLabel);
        header.Controls.Add(refreshBtn);
        header.Controls.Add(headerFlow);

        // ── Outer split: sidebar | main ───────────────────────────────────────
        // IsSplitterFixed = true means the user can never drag it, so Panel1MinSize /
        // Panel2MinSize are not needed and actually cause layout-time exceptions —
        // WinForms validates SplitterDistance against them during the initial layout
        // pass before our BeginInvoke callback fires. Leave them at default (25).
        var split = new SplitContainer
        {
            Dock            = DockStyle.Fill,
            Orientation     = Orientation.Vertical,
            SplitterWidth   = 1,
            FixedPanel      = FixedPanel.Panel1,
            BackColor       = Separator,
            IsSplitterFixed = true
        };

        // ── SIDEBAR ───────────────────────────────────────────────────────────
        var sidebar = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = BgSidebar,
            Padding   = new Padding(14, 14, 14, 8)
        };
        split.Panel1.Controls.Add(sidebar);

        // Stats section
        var statsSection = MakeSectionHeader("SYSTEM");
        _statRunning     = MakeStatLabel("—");
        _statToday       = MakeStatLabel("—");
        _statSuccessRate = MakeStatLabel("—");
        _statusBar       = new Panel
        {
            Height    = 8,
            Dock      = DockStyle.Top,
            BackColor = Color.FromArgb(40, 40, 46),
            Margin    = new Padding(0, 4, 0, 8),
            Cursor    = Cursors.Default
        };
        _statusBar.Paint += PaintStatusBar;

        var statGrid = new TableLayoutPanel
        {
            Dock        = DockStyle.Top,
            ColumnCount = 2,
            AutoSize    = true,
            Margin      = new Padding(0, 2, 0, 0)
        };
        statGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        statGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        statGrid.Controls.Add(MakeStatCaption("Running"), 0, 0);
        statGrid.Controls.Add(MakeStatCaption("Today"),   1, 0);
        statGrid.Controls.Add(_statRunning,               0, 1);
        statGrid.Controls.Add(_statToday,                 1, 1);
        statGrid.Controls.Add(MakeStatCaption("7-day rate"), 0, 2);
        statGrid.Controls.Add(_statSuccessRate,              1, 2);

        // Schedule section
        var schedSection = MakeSectionHeader("SCHEDULE");
        _cronList = new ListView
        {
            View        = View.Details,
            FullRowSelect = true,
            GridLines   = false,
            OwnerDraw   = true,
            BackColor   = BgSidebar,
            ForeColor   = TextPrimary,
            BorderStyle = BorderStyle.None,
            Font        = FontSmall,
            Dock        = DockStyle.Top,
            Height      = 160
        };
        _cronList.Columns.Add("",       10);   // active dot
        _cronList.Columns.Add("Name",   90);
        _cronList.Columns.Add("Next",   75);
        _cronList.Columns.Add("Type",   60);

        _cronList.DrawColumnHeader += (_, e) =>
        {
            using var bg = new SolidBrush(Color.FromArgb(36, 36, 42));
            e.Graphics.FillRectangle(bg, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", FontSmall,
                Rectangle.Inflate(e.Bounds, -2, 0),
                TextMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };
        _cronList.DrawItem += (_, e) => { e.DrawDefault = false; };
        _cronList.DrawSubItem += DrawCronSubItem;

        // Assemble sidebar (bottom to top for Dock.Top stacking)
        var schedHeaderWrapper = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = BgSidebar };
        schedHeaderWrapper.Controls.Add(schedSection);
        var statsHeaderWrapper = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = BgSidebar };
        statsHeaderWrapper.Controls.Add(statsSection);

        sidebar.Controls.Add(_cronList);
        sidebar.Controls.Add(schedHeaderWrapper);
        sidebar.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10, BackColor = BgSidebar }); // spacer
        sidebar.Controls.Add(_statusBar);
        sidebar.Controls.Add(statGrid);
        sidebar.Controls.Add(statsHeaderWrapper);

        // ── MAIN PANEL ────────────────────────────────────────────────────────
        var mainSplit = new SplitContainer
        {
            Dock          = DockStyle.Fill,
            Orientation   = Orientation.Horizontal,
            SplitterWidth = 1,
            BackColor     = Separator,
            Panel2MinSize = 70
        };
        split.Panel2.Controls.Add(mainSplit);

        // Task list
        _taskList = new ListView
        {
            Dock          = DockStyle.Fill,
            View          = View.Details,
            FullRowSelect = true,
            GridLines     = false,
            OwnerDraw     = true,
            BackColor     = BgPanel,
            ForeColor     = TextPrimary,
            BorderStyle   = BorderStyle.None,
            Font          = FontUi
        };
        _taskList.Columns.Add("Type",    140);
        _taskList.Columns.Add("Status",   96);
        _taskList.Columns.Add("Started", 130);
        _taskList.Columns.Add("Duration", 76);
        _taskList.Columns.Add("Note",    260);

        _taskList.DrawColumnHeader += DrawTaskColumnHeader;
        _taskList.DrawItem         += (_, e) => { e.DrawDefault = false; };
        _taskList.DrawSubItem      += DrawTaskSubItem;

        AttachTaskContextMenu();

        // Tooltip on Note column
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
        mainSplit.Panel1.Controls.Add(_taskList);

        // Detail panel
        _detailBox = new TextBox
        {
            Dock        = DockStyle.Fill,
            Multiline   = true,
            ReadOnly    = true,
            ScrollBars  = ScrollBars.Vertical,
            BackColor   = BgDetail,
            ForeColor   = Color.FromArgb(190, 190, 200),
            BorderStyle = BorderStyle.None,
            Font        = FontUi,
            Padding     = new Padding(8)
        };
        _lastUpdated = new Label
        {
            Dock      = DockStyle.Bottom,
            Height    = 20,
            Text      = "",
            ForeColor = TextMuted,
            Font      = FontSmall,
            TextAlign = ContentAlignment.MiddleRight,
            Padding   = new Padding(0, 0, 8, 0),
            BackColor = BgDetail
        };
        mainSplit.Panel2.Controls.Add(_detailBox);
        mainSplit.Panel2.Controls.Add(_lastUpdated);

        Controls.Add(split);
        Controls.Add(header);

        Shown += (_, _) =>
        {
            // BeginInvoke defers one pump cycle so layout is fully committed
            // before we touch SplitterDistance (which validates against real size).
            BeginInvoke(() =>
            {
                try { split.SplitterDistance     = 260; } catch { /* use default */ }
                try { mainSplit.SplitterDistance = 440; } catch { /* use default */ }
                _ = RefreshAsync();
            });
        };
    }

    // ── Paint helpers ─────────────────────────────────────────────────────────

    private void PaintStatusBar(object? sender, PaintEventArgs e)
    {
        if (_cntTotal == 0) return;
        var g = e.Graphics;
        var w = _statusBar.Width;
        var h = _statusBar.Height;

        (int count, Color color)[] segments =
        [
            (_cntRunning,   Color.FromArgb(80,  160, 240)),
            (_cntPending,   Color.FromArgb(200, 160,  60)),
            (_cntSucceeded, Color.FromArgb(70,  200,  90)),
            (_cntFailed,    Color.FromArgb(220,  80,  70))
        ];

        int x = 0;
        foreach (var (count, color) in segments)
        {
            if (count == 0) continue;
            int segW = (int)Math.Round((double)count / _cntTotal * w);
            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, x, 0, segW, h);
            x += segW;
        }
    }

    private static void DrawTaskColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var bg = new SolidBrush(Color.FromArgb(40, 40, 46));
        e.Graphics.FillRectangle(bg, e.Bounds);
        using var pen = new Pen(Separator);
        e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", new Font("Segoe UI", 8.5f),
            Rectangle.Inflate(e.Bounds, -4, 0),
            Color.FromArgb(170, 170, 180),
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    private static void DrawTaskSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        bool selected = e.Item.Selected;
        bool alt      = e.Item.Index % 2 == 1;
        var bg = selected
            ? Color.FromArgb(38, 82, 148)
            : alt ? Color.FromArgb(34, 34, 40) : BgPanel;
        using var bgBrush = new SolidBrush(bg);
        e.Graphics.FillRectangle(bgBrush, e.Bounds);

        var fg = selected ? Color.White
            : (e.SubItem?.ForeColor is { } c && c != Color.Empty && c != e.Item?.ForeColor
                ? c : e.Item?.ForeColor ?? TextPrimary);
        TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", new Font("Segoe UI", 9f),
            Rectangle.Inflate(e.Bounds, -4, 0), fg,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    private void DrawCronSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        bool selected = e.Item.Selected;
        var bg = selected ? Color.FromArgb(38, 82, 148) : BgSidebar;
        using var bgBrush = new SolidBrush(bg);
        e.Graphics.FillRectangle(bgBrush, e.Bounds);

        if (e.ColumnIndex == 0 && e.Item?.Tag is CronJob job)
        {
            // Draw active dot
            var dot = job.IsActive ? Color.FromArgb(70, 200, 90) : Color.FromArgb(130, 130, 140);
            using var dBrush = new SolidBrush(dot);
            var cx = e.Bounds.Left + e.Bounds.Width / 2;
            var cy = e.Bounds.Top  + e.Bounds.Height / 2;
            e.Graphics.FillEllipse(dBrush, cx - 4, cy - 4, 8, 8);
            return;
        }

        var fg = selected ? Color.White : (e.SubItem?.ForeColor ?? TextPrimary);
        TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", FontSmall,
            Rectangle.Inflate(e.Bounds, -2, 0), fg,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    // ── Section / label factories ─────────────────────────────────────────────

    private static Label MakeSectionHeader(string text) => new()
    {
        Text      = text,
        Dock      = DockStyle.Fill,
        ForeColor = Color.FromArgb(100, 140, 200),
        Font      = new Font("Segoe UI", 7.5f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft,
        BackColor = Color.Transparent
    };

    private static Label MakeStatCaption(string text) => new()
    {
        Text      = text,
        ForeColor = TextMuted,
        Font      = new Font("Segoe UI", 7.5f),
        AutoSize  = false,
        Height    = 16,
        Dock      = DockStyle.Fill,
        TextAlign = ContentAlignment.BottomLeft
    };

    private static Label MakeStatLabel(string text) => new()
    {
        Text      = text,
        ForeColor = TextPrimary,
        Font      = new Font("Segoe UI Semibold", 14f),
        AutoSize  = false,
        Height    = 28,
        Dock      = DockStyle.Fill,
        TextAlign = ContentAlignment.TopLeft
    };

    private static Label MakeDot() => new()
    {
        Width     = 10,
        Height    = 10,
        BackColor = Color.Gray,
        Text      = "",
        Margin    = new Padding(0, 21, 6, 0)
    };

    private static Label MakeServiceLabel(string text) => new()
    {
        Text      = text,
        ForeColor = TextPrimary,
        AutoSize  = true,
        Font      = new Font("Segoe UI", 9.5f),
        Margin    = new Padding(0, 17, 22, 0)
    };

    private static Label MakeSep() => new()
    {
        Text      = "│",
        ForeColor = Color.FromArgb(60, 60, 68),
        Width     = 18,
        TextAlign = ContentAlignment.MiddleCenter,
        AutoSize  = false,
        Font      = new Font("Segoe UI", 13f),
        Margin    = new Padding(0, 14, 0, 0)
    };

    // ── Context menu (unchanged logic, rewired to new task list) ──────────────

    private void AttachTaskContextMenu()
    {
        var rowMenu = new ContextMenuStrip
        {
            BackColor  = Color.FromArgb(40, 40, 46),
            ForeColor  = TextPrimary,
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
            troubleshootItem.Enabled = t?.Status is "Failed" or "TimedOut" or "Succeeded" or "Partial";
        };

        _taskList.ContextMenuStrip = rowMenu;
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    public async Task RefreshAsync()
    {
        await Task.WhenAll(
            RefreshServicesAsync(),
            RefreshTasksAsync(),
            RefreshScheduleAsync());
        _lastUpdated.Text = $"Updated {DateTime.Now:HH:mm:ss}  ";
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
            var tasks = JsonSerializer.Deserialize<List<TaskSummary>>(json, JsonOpts) ?? [];
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
            var tasks = JsonSerializer.Deserialize<List<TaskSummary>>(json, JsonOpts) ?? [];

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

            // Populate task list
            _taskList.BeginUpdate();
            _taskList.Items.Clear();

            foreach (var t in tasks)
            {
                var duration = t.CompletedAt.HasValue && t.StartedAt.HasValue
                    ? FormatDuration(t.CompletedAt.Value - t.StartedAt.Value)
                    : t.StartedAt.HasValue ? "running…" : "—";

                var note = string.IsNullOrWhiteSpace(t.Error) ? t.AgentResponse : t.Error;

                var statusText = t.AgentReported && t.Status == "Succeeded"
                    ? "Succeeded ✔" : t.Status ?? "—";

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
                    _                                => TextPrimary
                };

                item.SubItems[4].ForeColor = string.IsNullOrWhiteSpace(t.Error)
                    ? Color.FromArgb(150, 150, 160)
                    : Color.FromArgb(255, 130, 110);

                _taskList.Items.Add(item);
            }

            _taskList.EndUpdate();
        }
        catch { /* Dispatcher unreachable — leave as-is */ }
    }

    private async Task RefreshScheduleAsync()
    {
        try
        {
            var json = await _http.GetStringAsync("http://127.0.0.1:47777/scheduler/crons");
            var jobs = JsonSerializer.Deserialize<List<CronJob>>(json, JsonOpts) ?? [];

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
                        : TextMuted;
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
        catch { /* Dispatcher unreachable */ }
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
            var all  = JsonSerializer.Deserialize<List<TaskSummary>>(json, JsonOpts) ?? [];
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

    private static string FormatDuration(TimeSpan ts) =>
        ts.TotalHours >= 1  ? $"{ts.Hours}h {ts.Minutes}m" :
        ts.TotalMinutes >= 1 ? $"{(int)ts.TotalMinutes}m {ts.Seconds}s" :
                               $"{ts.Seconds}s";

    private static string FormatRelative(DateTime? utc)
    {
        if (utc is null) return "—";
        var diff = utc.Value - DateTime.UtcNow;
        if (diff.TotalSeconds < 0)  return "overdue";
        if (diff.TotalMinutes < 60) return $"in {(int)diff.TotalMinutes}m";
        if (diff.TotalHours   < 24) return $"in {(int)diff.TotalHours}h";
        return $"in {(int)diff.TotalDays}d";
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // ── Work dir resolution (unchanged) ──────────────────────────────────────

    private static string? ResolveWorkDir(TaskSummary? t)
    {
        if (t?.WorkDir is { } dir && Directory.Exists(dir)) return dir;
        if (t?.Id is not { Length: > 0 } idStr || !Guid.TryParse(idStr, out var id)) return null;
        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".daeanne", "tasks");
        return FindTaskDirLocal(baseDir, id);
    }

    private static string? FindTaskDirLocal(string baseDir, Guid id)
    {
        string[] candidates =
        [
            Path.Combine(baseDir, "active",              id.ToString()),
            Path.Combine(baseDir, "complete",            id.ToString()),
            Path.Combine(baseDir, "failed",              id.ToString()),
            Path.Combine(baseDir, "complete", "archive", id.ToString()),
            Path.Combine(baseDir, "scheduled", "active",   id.ToString()),
            Path.Combine(baseDir, "scheduled", "complete", id.ToString()),
            Path.Combine(baseDir, "scheduled", "failed",   id.ToString()),
        ];
        return candidates.FirstOrDefault(Directory.Exists);
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    private sealed class TaskSummary
    {
        public string?   Id            { get; set; }
        public string?   Type          { get; set; }
        public string?   Status        { get; set; }
        public string?   Prompt        { get; set; }
        public bool      AgentReported { get; set; }
        public DateTime? StartedAt     { get; set; }
        public DateTime? CompletedAt   { get; set; }
        public DateTime? CreatedAt     { get; set; }
        public string?   Error         { get; set; }
        public string?   WorkDir       { get; set; }
        public string?   ResultJson    { get; set; }

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
                    if (r.TryGetProperty("response", out var v)) _agentResponse = v.GetString();
                }
                catch { }
                return _agentResponse;
            }
        }
    }

    private sealed class CronJob
    {
        public string?   Id        { get; set; }
        public string?   Name      { get; set; }
        public string?   JobType   { get; set; }
        public string?   TaskType  { get; set; }
        public string?   TimeOfDay { get; set; }
        public string?   DayOfWeek { get; set; }
        public DateTime? NextRunAt { get; set; }
        public DateTime? LastFiredAt { get; set; }
        public bool      IsActive  { get; set; }
    }
}
