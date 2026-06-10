namespace Daeanne.Tray;

/// <summary>
/// Constructs and owns all WinForms controls for the dashboard window.
/// No event handlers that capture form state — ActivityWindow wires those
/// after receiving the built controls via properties.
/// </summary>
internal sealed class DashboardLayout
{
    // ── Top-level controls (added to Form.Controls in dock order) ─────────────
    internal Panel         Header     { get; }
    internal Panel         StatsStrip { get; }
    internal Panel         FilterBar  { get; }
    internal SplitContainer MainSplit { get; }

    // ── Header internals ──────────────────────────────────────────────────────
    internal Button RefreshButton   { get; }
    internal Label  DispatcherDot   { get; }
    internal Label  DispatcherLabel { get; }
    internal Label  BridgeDot       { get; }
    internal Label  BridgeLabel     { get; }
    internal Label  DaeanneD        { get; }
    internal Label  DaeanneLabel    { get; }

    // ── Stats strip internals ─────────────────────────────────────────────────
    internal Label  StatRunning     { get; }
    internal Label  StatToday       { get; }
    internal Label  StatSuccessRate { get; }
    internal Panel  StatusBar       { get; }
    internal ListView CronList      { get; }

    // ── Filter + task list internals ──────────────────────────────────────────
    internal TextBox  FilterBox   { get; }
    internal ListView TaskList    { get; }
    internal TextBox  DetailBox   { get; }
    internal Label    LastUpdated { get; }

    internal DashboardLayout()
    {
        // ── Header ────────────────────────────────────────────────────────────
        Header = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 52,
            BackColor = DashboardTheme.BgHeader,
            Padding   = new Padding(16, 0, 12, 0)
        };
        Header.Paint += (_, e) =>
        {
            using var pen = new Pen(DashboardTheme.Separator);
            e.Graphics.DrawLine(pen, 0, Header.Height - 1, Header.Width, Header.Height - 1);
        };

        DispatcherDot   = DashboardTheme.MakeDot();
        DispatcherLabel = DashboardTheme.MakeServiceLabel("Dispatcher  ·  checking…");
        BridgeDot       = DashboardTheme.MakeDot();
        BridgeLabel     = DashboardTheme.MakeServiceLabel("Bridge  ·  checking…");
        DaeanneD        = DashboardTheme.MakeDot();
        DaeanneLabel    = DashboardTheme.MakeServiceLabel("Daeanne  ·  checking…");

        RefreshButton = new Button
        {
            Text      = "↻  Refresh",
            Dock      = DockStyle.Right,
            Width     = 90,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(48, 48, 56),
            ForeColor = Color.FromArgb(200, 200, 210),
            Font      = DashboardTheme.FontUi,
            Cursor    = Cursors.Hand
        };
        RefreshButton.FlatAppearance.BorderSize = 0;

        var headerFlow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
        };
        headerFlow.Controls.Add(DispatcherDot);
        headerFlow.Controls.Add(DispatcherLabel);
        headerFlow.Controls.Add(DashboardTheme.MakeSep());
        headerFlow.Controls.Add(BridgeDot);
        headerFlow.Controls.Add(BridgeLabel);
        headerFlow.Controls.Add(DashboardTheme.MakeSep());
        headerFlow.Controls.Add(DaeanneD);
        headerFlow.Controls.Add(DaeanneLabel);
        Header.Controls.Add(RefreshButton);
        Header.Controls.Add(headerFlow);

        // ── Stats strip ───────────────────────────────────────────────────────
        StatsStrip = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 120,
            BackColor = DashboardTheme.BgSidebar
        };
        StatsStrip.Paint += (_, e) =>
        {
            using var pen = new Pen(DashboardTheme.Separator);
            e.Graphics.DrawLine(pen, 0, StatsStrip.Height - 1, StatsStrip.Width, StatsStrip.Height - 1);
        };

        var stripLayout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            RowCount    = 1,
            BackColor   = Color.Transparent
        };
        stripLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270f));
        stripLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        stripLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        StatsStrip.Controls.Add(stripLayout);

        // Left cell: system stats
        var sysPanel = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = DashboardTheme.BgSidebar,
            Padding   = new Padding(14, 8, 12, 6)
        };
        StatRunning     = DashboardTheme.MakeStatLabel("—");
        StatToday       = DashboardTheme.MakeStatLabel("—");
        StatSuccessRate = DashboardTheme.MakeStatLabel("—");
        StatusBar = new Panel
        {
            Height    = 7,
            Dock      = DockStyle.Bottom,
            BackColor = Color.FromArgb(40, 40, 46)
        };

        var statGrid = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 3,
            RowCount    = 2,
            BackColor   = Color.Transparent,
            Margin      = new Padding(0)
        };
        statGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
        statGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
        statGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
        statGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
        statGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        statGrid.Controls.Add(DashboardTheme.MakeStatCaption("Running"),  0, 0);
        statGrid.Controls.Add(DashboardTheme.MakeStatCaption("Today"),    1, 0);
        statGrid.Controls.Add(DashboardTheme.MakeStatCaption("7-day"),    2, 0);
        statGrid.Controls.Add(StatRunning,     0, 1);
        statGrid.Controls.Add(StatToday,       1, 1);
        statGrid.Controls.Add(StatSuccessRate, 2, 1);

        var sysHeader = new Panel { Dock = DockStyle.Top, Height = 22, BackColor = Color.Transparent };
        sysHeader.Controls.Add(DashboardTheme.MakeSectionHeader("SYSTEM"));
        sysPanel.Controls.Add(StatusBar);
        sysPanel.Controls.Add(statGrid);
        sysPanel.Controls.Add(sysHeader);
        stripLayout.Controls.Add(sysPanel, 0, 0);

        // Right cell: schedule
        var schedPanel = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = DashboardTheme.BgSidebar,
            Padding   = new Padding(12, 8, 14, 6)
        };
        CronList = new ListView
        {
            View          = View.Details,
            FullRowSelect = true,
            GridLines     = false,
            OwnerDraw     = true,
            BackColor     = DashboardTheme.BgSidebar,
            ForeColor     = DashboardTheme.TextPrimary,
            BorderStyle   = BorderStyle.None,
            Font          = DashboardTheme.FontSmall,
            Dock          = DockStyle.Fill
        };
        CronList.Columns.Add("",      22);   // active dot
        CronList.Columns.Add("Name", 200);
        CronList.Columns.Add("Next",  90);
        CronList.Columns.Add("Type", 160);

        CronList.DrawColumnHeader += (_, e) =>
        {
            using var bg = new SolidBrush(Color.FromArgb(36, 36, 42));
            e.Graphics.FillRectangle(bg, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", DashboardTheme.FontSmall,
                Rectangle.Inflate(e.Bounds, -2, 0),
                DashboardTheme.TextMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };
        CronList.DrawItem    += (_, e) => { e.DrawDefault = false; };
        CronList.DrawSubItem += DashboardPainter.DrawCronSubItem;

        var schedHeader = new Panel { Dock = DockStyle.Top, Height = 22, BackColor = Color.Transparent };
        schedHeader.Controls.Add(DashboardTheme.MakeSectionHeader("SCHEDULE"));
        schedPanel.Controls.Add(CronList);
        schedPanel.Controls.Add(schedHeader);
        stripLayout.Controls.Add(schedPanel, 1, 0);

        // ── Filter bar ────────────────────────────────────────────────────────
        FilterBar = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 34,
            BackColor = DashboardTheme.BgHeader,
            Padding   = new Padding(10, 6, 10, 6)
        };
        FilterBar.Paint += (_, e) =>
        {
            using var pen = new Pen(DashboardTheme.Separator);
            e.Graphics.DrawLine(pen, 0, FilterBar.Height - 1, FilterBar.Width, FilterBar.Height - 1);
        };
        FilterBox = new TextBox
        {
            Dock            = DockStyle.Right,
            Width           = 200,
            BackColor       = Color.FromArgb(42, 42, 50),
            ForeColor       = DashboardTheme.TextPrimary,
            BorderStyle     = BorderStyle.FixedSingle,
            Font            = DashboardTheme.FontUi,
            PlaceholderText = "filter…"
        };
        FilterBar.Controls.Add(FilterBox);

        // ── Main split (task list | detail) ───────────────────────────────────
        MainSplit = new SplitContainer
        {
            Dock          = DockStyle.Fill,
            Orientation   = Orientation.Horizontal,
            SplitterWidth = 1,
            BackColor     = DashboardTheme.Separator,
            Panel2MinSize = 70
        };

        TaskList = new ListView
        {
            Dock          = DockStyle.Fill,
            View          = View.Details,
            FullRowSelect = true,
            GridLines     = false,
            OwnerDraw     = true,
            BackColor     = DashboardTheme.BgPanel,
            ForeColor     = DashboardTheme.TextPrimary,
            BorderStyle   = BorderStyle.None,
            Font          = DashboardTheme.FontUi
        };
        TaskList.Columns.Add("Type",     130);
        TaskList.Columns.Add("Status",    96);
        TaskList.Columns.Add("Started",  130);
        TaskList.Columns.Add("Duration",  76);
        TaskList.Columns.Add("Note",     400);   // fills on resize

        TaskList.DrawColumnHeader += DashboardPainter.DrawTaskColumnHeader;
        TaskList.DrawItem         += (_, e) => { e.DrawDefault = false; };
        TaskList.DrawSubItem      += DashboardPainter.DrawTaskSubItem;

        MainSplit.Panel1.Controls.Add(TaskList);

        DetailBox = new TextBox
        {
            Dock        = DockStyle.Fill,
            Multiline   = true,
            ReadOnly    = true,
            ScrollBars  = ScrollBars.Vertical,
            BackColor   = DashboardTheme.BgDetail,
            ForeColor   = Color.FromArgb(190, 190, 200),
            BorderStyle = BorderStyle.None,
            Font        = DashboardTheme.FontUi,
            Padding     = new Padding(8)
        };
        LastUpdated = new Label
        {
            Dock      = DockStyle.Bottom,
            Height    = 20,
            Text      = "",
            ForeColor = DashboardTheme.TextMuted,
            Font      = DashboardTheme.FontSmall,
            TextAlign = ContentAlignment.MiddleRight,
            Padding   = new Padding(0, 0, 8, 0),
            BackColor = DashboardTheme.BgDetail
        };
        MainSplit.Panel2.Controls.Add(DetailBox);
        MainSplit.Panel2.Controls.Add(LastUpdated);
    }
}
