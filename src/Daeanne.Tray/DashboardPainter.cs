namespace Daeanne.Tray;

/// <summary>
/// GDI+ owner-draw handlers for the dashboard ListView controls and status bar.
/// All methods are static — no form state required at paint time.
/// </summary>
internal static class DashboardPainter
{
    /// <summary>
    /// Paints the segmented status bar (running/pending/succeeded/failed).
    /// Call from the Panel.Paint event, passing the cached task counts.
    /// </summary>
    internal static void PaintStatusBar(
        PaintEventArgs e, int width, int height,
        int running, int pending, int succeeded, int failed, int total)
    {
        if (total == 0) return;

        (int count, Color color)[] segments =
        [
            (running,   Color.FromArgb(80,  160, 240)),
            (pending,   Color.FromArgb(200, 160,  60)),
            (succeeded, Color.FromArgb(70,  200,  90)),
            (failed,    Color.FromArgb(220,  80,  70))
        ];

        int x = 0;
        foreach (var (count, color) in segments)
        {
            if (count == 0) continue;
            int segW = (int)Math.Round((double)count / total * width);
            using var brush = new SolidBrush(color);
            e.Graphics.FillRectangle(brush, x, 0, segW, height);
            x += segW;
        }
    }

    internal static void DrawTaskColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var bg = new SolidBrush(Color.FromArgb(40, 40, 46));
        e.Graphics.FillRectangle(bg, e.Bounds);
        using var pen = new Pen(DashboardTheme.Separator);
        e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", new Font("Segoe UI", 8.5f),
            Rectangle.Inflate(e.Bounds, -4, 0),
            Color.FromArgb(170, 170, 180),
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    internal static void DrawTaskSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        bool selected = e.Item?.Selected ?? false;
        bool alt      = (e.Item?.Index ?? 0) % 2 == 1;
        var bg = selected
            ? Color.FromArgb(38, 82, 148)
            : alt ? Color.FromArgb(34, 34, 40) : DashboardTheme.BgPanel;
        using var bgBrush = new SolidBrush(bg);
        e.Graphics.FillRectangle(bgBrush, e.Bounds);

        var fg = selected ? Color.White
            : (e.SubItem?.ForeColor is { } c && c != Color.Empty && c != e.Item?.ForeColor
                ? c : e.Item?.ForeColor ?? DashboardTheme.TextPrimary);
        TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", new Font("Segoe UI", 9f),
            Rectangle.Inflate(e.Bounds, -4, 0), fg,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    internal static void DrawCronSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        bool selected = e.Item?.Selected ?? false;
        var bg = selected ? Color.FromArgb(38, 82, 148) : DashboardTheme.BgSidebar;
        using var bgBrush = new SolidBrush(bg);
        e.Graphics.FillRectangle(bgBrush, e.Bounds);

        if (e.ColumnIndex == 0 && e.Item?.Tag is CronJob job)
        {
            var dot = job.IsActive ? Color.FromArgb(70, 200, 90) : Color.FromArgb(130, 130, 140);
            using var dBrush = new SolidBrush(dot);
            var cx = e.Bounds.Left + e.Bounds.Width / 2;
            var cy = e.Bounds.Top  + e.Bounds.Height / 2;
            e.Graphics.FillEllipse(dBrush, cx - 4, cy - 4, 8, 8);
            return;
        }

        var fg = selected ? Color.White : (e.SubItem?.ForeColor ?? DashboardTheme.TextPrimary);
        TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", DashboardTheme.FontSmall,
            Rectangle.Inflate(e.Bounds, -2, 0), fg,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}
