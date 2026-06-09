namespace Daeanne.Tray;

/// <summary>
/// Loads the three tray icons from embedded PNG resources and converts them
/// to 32x32 Icon objects suitable for NotifyIcon.
/// </summary>
internal static class IconLoader
{
    public static Icon Green  { get; } = Load("icon-green.png");
    public static Icon Orange { get; } = Load("icon-orange.png");
    public static Icon Red    { get; } = Load("icon-red.png");

    private static Icon Load(string fileName)
    {
        var asm  = typeof(IconLoader).Assembly;
        var name = asm.GetManifestResourceNames()
                      .First(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

        using var stream = asm.GetManifestResourceStream(name)!;
        using var bmp    = new Bitmap(stream);
        using var small  = new Bitmap(bmp, 32, 32);
        return Icon.FromHandle(small.GetHicon());
    }
}
