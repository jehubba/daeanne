namespace Daeanne.Bridge;

/// <summary>
/// Lightweight static cache for the current Graph access token.
/// Populated by GraphMailWorker on every successful token refresh;
/// consumed by CalendarEndpoints (and any future Bridge endpoint that
/// needs Graph access without owning the token lifecycle).
/// </summary>
internal static class GraphTokenCache
{
    private static string?  _accessToken;
    private static DateTime _expiresAt = DateTime.MinValue;
    private static readonly object _lock = new();

    /// <summary>Set by GraphMailWorker after every successful refresh.</summary>
    public static void Update(string accessToken, int expiresInSeconds)
    {
        lock (_lock)
        {
            _accessToken = accessToken;
            _expiresAt   = DateTime.UtcNow.AddSeconds(expiresInSeconds);
        }
    }

    /// <summary>
    /// Returns the current access token if valid (with a 2-minute safety margin),
    /// or null if the token is stale/absent.
    /// </summary>
    public static string? Get()
    {
        lock (_lock)
            return _accessToken != null && DateTime.UtcNow < _expiresAt.AddMinutes(-2)
                ? _accessToken
                : null;
    }
}
