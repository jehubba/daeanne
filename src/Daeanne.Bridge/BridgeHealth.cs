namespace Daeanne.Bridge;

/// <summary>
/// Shared health state written by workers, read by the /health endpoint.
/// Volatile fields are safe for single-writer / multiple-reader access patterns.
/// </summary>
internal static class BridgeHealth
{
    /// <summary>True until a Graph token refresh fails; restored to true on next success.</summary>
    public static volatile bool GraphTokenOk = true;

    public static string?  GraphTokenError       = null;
    public static DateTime? GraphTokenLastChecked = null;
}
