using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Daeanne.Tray;

/// <summary>
/// Generates clean tray icons programmatically — anti-aliased circles
/// with a highlight, shadow, and border. No external assets needed.
/// </summary>
internal static class IconLoader
{
    public static Icon Green  { get; } = Create(Color.FromArgb(52, 199, 89));
    public static Icon Orange { get; } = Create(Color.FromArgb(255, 159, 10));
    public static Icon Red    { get; } = Create(Color.FromArgb(215, 50, 47));

    private static Icon Create(Color fill)
    {
        using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using var g   = Graphics.FromImage(bmp);

        g.SmoothingMode      = SmoothingMode.AntiAlias;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.Clear(Color.Transparent);

        // Drop shadow
        var shadowRect = new RectangleF(5, 6, 24, 24);
        using (var shadow = new SolidBrush(Color.FromArgb(55, 0, 0, 0)))
            g.FillEllipse(shadow, shadowRect);

        // Main circle
        var circleRect = new RectangleF(3, 3, 24, 24);
        using (var fillBrush = new SolidBrush(fill))
            g.FillEllipse(fillBrush, circleRect);

        // Radial inner glow — slightly lighter center
        var glowRect = new RectangleF(6, 6, 18, 18);
        using (var glow = new PathGradientBrush(EllipsePath(glowRect)))
        {
            glow.CenterColor    = Color.FromArgb(60, 255, 255, 255);
            glow.SurroundColors = [Color.FromArgb(0, 255, 255, 255)];
            g.FillEllipse(glow, glowRect);
        }

        // Gloss highlight — top-left arc
        var glossRect = new RectangleF(6, 5, 13, 8);
        using var glossBrush = new LinearGradientBrush(
            new PointF(8, 5), new PointF(8, 13),
            Color.FromArgb(140, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255));
        g.FillEllipse(glossBrush, glossRect);

        // Thin border
        using var borderPen = new Pen(Color.FromArgb(60, 0, 0, 0), 1f);
        g.DrawEllipse(borderPen, circleRect);

        return Icon.FromHandle(bmp.GetHicon());
    }

    private static GraphicsPath EllipsePath(RectangleF r)
    {
        var path = new GraphicsPath();
        path.AddEllipse(r);
        return path;
    }
}
