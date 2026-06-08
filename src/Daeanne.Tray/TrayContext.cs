namespace Daeanne.Tray;

/// <summary>
/// System tray presence for Daeanne.
///
/// Phase 5/6 TODO:
///   - Poll Dispatcher API (http://127.0.0.1:47777/tasks?status=Running) every few seconds
///   - Animate icon or show badge when a task is in progress
///   - Show balloon tooltip when a task completes ("✅ Research Rivian — done")
///   - Click → open ActivityWindow showing recent tasks + last output
///   - Right-click menu: Open Inbox, View Tasks, Open Dispatcher Log, separator, Exit
/// </summary>
internal class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;

    public TrayContext()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Activity Log", null, OnOpenActivityLog);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, OnExit);

        _trayIcon = new NotifyIcon
        {
            Text    = "Daeanne",
            Icon    = SystemIcons.Application,   // TODO: replace with custom Daeanne icon
            ContextMenuStrip = menu,
            Visible = true
        };

        _trayIcon.DoubleClick += OnOpenActivityLog;

        _trayIcon.ShowBalloonTip(
            timeout:  3000,
            tipTitle: "Daeanne",
            tipText:  "Running in the background.",
            tipIcon:  ToolTipIcon.Info);
    }

    private void OnOpenActivityLog(object? sender, EventArgs e)
    {
        // TODO: open ActivityWindow — a simple ListView showing recent tasks
        // For now, open the tasks folder in Explorer as a placeholder
        var tasksDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".daeanne", "tasks");
        Directory.CreateDirectory(tasksDir);
        System.Diagnostics.Process.Start("explorer.exe", tasksDir);
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _trayIcon.Visible = false;
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _trayIcon.Dispose();
        base.Dispose(disposing);
    }
}
