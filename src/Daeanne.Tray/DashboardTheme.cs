namespace Daeanne.Tray;

/// <summary>
/// Design tokens (colors, fonts) and stateless widget factory methods shared
/// across the dashboard. All members are static — no instance required.
/// </summary>
internal static class DashboardTheme
{
    // ── Colors ────────────────────────────────────────────────────────────────
    internal static readonly Color BgBase     = Color.FromArgb(22,  22,  26);
    internal static readonly Color BgPanel    = Color.FromArgb(30,  30,  34);
    internal static readonly Color BgHeader   = Color.FromArgb(38,  38,  44);
    internal static readonly Color BgSidebar  = Color.FromArgb(26,  26,  30);
    internal static readonly Color BgDetail   = Color.FromArgb(18,  18,  20);
    internal static readonly Color Separator  = Color.FromArgb(50,  50,  56);
    internal static readonly Color TextPrimary = Color.FromArgb(220, 220, 228);
    internal static readonly Color TextMuted   = Color.FromArgb(130, 130, 140);

    // ── Fonts ─────────────────────────────────────────────────────────────────
    internal static readonly Font FontUi    = new("Segoe UI", 9f);
    internal static readonly Font FontSmall = new("Segoe UI", 8f);
    internal static readonly Font FontMono  = MakeMonoFont();

    private static Font MakeMonoFont()
    {
        try   { return new Font("Cascadia Mono", 8.5f, FontStyle.Regular); }
        catch { return new Font("Consolas",      8.5f, FontStyle.Regular); }
    }

    // ── Widget factories ──────────────────────────────────────────────────────

    internal static Label MakeSectionHeader(string text) => new()
    {
        Text      = text,
        Dock      = DockStyle.Fill,
        ForeColor = Color.FromArgb(100, 140, 200),
        Font      = new Font("Segoe UI", 7.5f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft,
        BackColor = Color.Transparent
    };

    internal static Label MakeStatCaption(string text) => new()
    {
        Text      = text,
        ForeColor = TextMuted,
        Font      = new Font("Segoe UI", 7.5f),
        AutoSize  = false,
        Height    = 16,
        Dock      = DockStyle.Fill,
        TextAlign = ContentAlignment.BottomLeft
    };

    internal static Label MakeStatLabel(string text) => new()
    {
        Text      = text,
        ForeColor = TextPrimary,
        Font      = new Font("Segoe UI Semibold", 14f),
        AutoSize  = false,
        Height    = 28,
        Dock      = DockStyle.Fill,
        TextAlign = ContentAlignment.TopLeft
    };

    internal static Label MakeDot() => new()
    {
        Width     = 10,
        Height    = 10,
        BackColor = Color.Gray,
        Text      = "",
        Margin    = new Padding(0, 21, 6, 0)
    };

    internal static Label MakeServiceLabel(string text) => new()
    {
        Text      = text,
        ForeColor = TextPrimary,
        AutoSize  = true,
        Font      = new Font("Segoe UI", 9.5f),
        Margin    = new Padding(0, 17, 22, 0)
    };

    internal static Label MakeSep() => new()
    {
        Text      = "│",
        ForeColor = Color.FromArgb(60, 60, 68),
        Width     = 18,
        TextAlign = ContentAlignment.MiddleCenter,
        AutoSize  = false,
        Font      = new Font("Segoe UI", 13f),
        Margin    = new Padding(0, 14, 0, 0)
    };
}
