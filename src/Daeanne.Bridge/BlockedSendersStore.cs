using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Daeanne.Bridge;

/// <summary>
/// Manages the dynamic blocked-senders list at %APPDATA%\daeanne\blocked-senders.json.
///
/// Two tiers of filtering:
///   1. Static config (Graph:IgnoredSenders) — developer-set domain/address patterns
///   2. This store — Daeanne-managed or auto-detected senders
///
/// Automatically detects common no-reply patterns on first encounter and persists them
/// to the store so they are filtered on subsequent polls without re-evaluating.
///
/// Daeanne can add entries directly via shell (see daeanne.agent.md "Mail Filtering").
/// </summary>
public sealed class BlockedSendersStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "daeanne", "blocked-senders.json");

    private static readonly string FilterLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "daeanne", "filter-log.jsonl");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        WriteIndented               = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    // Patterns that indicate automated/system mail — auto-block on first match.
    private static readonly string[] NoReplyPrefixes =
    [
        "noreply@", "no-reply@", "donotreply@", "do-not-reply@",
        "notifications@", "notification@", "alerts@", "alert@",
        "mailer@", "mailer-daemon@", "postmaster@", "bounce@",
        "system@", "automated@", "auto@", "robot@"
    ];

    private static readonly string[] NoReplyDomainPrefixes =
    [
        "notifications.", "alerts.", "mailer.", "noreply.", "bounce.", "mail."
    ];

    private List<BlockedSenderEntry> _entries = [];
    private int _pollsSinceReload = 0;
    private readonly int _reloadEveryNPolls;
    private readonly object _lock = new();

    public BlockedSendersStore(int reloadEveryNPolls = 10)
    {
        _reloadEveryNPolls = reloadEveryNPolls;
        Reload();
    }

    /// <summary>
    /// Returns (isBlocked, reason) for the given sender address.
    /// Also handles heuristic auto-detection — if matched by pattern and not yet
    /// in the store, the sender is auto-added and persisted.
    /// </summary>
    public (bool blocked, string reason) Check(string from)
    {
        if (string.IsNullOrWhiteSpace(from)) return (false, "");

        MaybeReload();

        lock (_lock)
        {
            // Exact address match
            var entry = _entries.FirstOrDefault(e =>
                e.Address.Equals(from, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                TouchEntry(entry);
                return (true, entry.Reason);
            }

            // Domain suffix match (e.g. "@accountprotection.microsoft.com")
            entry = _entries.FirstOrDefault(e =>
                e.Address.StartsWith("@") &&
                from.EndsWith(e.Address, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                TouchEntry(entry);
                return (true, entry.Reason);
            }
        }

        // Heuristic auto-detection — not yet in store
        var heuristicReason = DetectNoReplyPattern(from);
        if (heuristicReason != null)
        {
            AutoAdd(from, heuristicReason);
            return (true, heuristicReason);
        }

        return (false, "");
    }

    /// <summary>
    /// Writes a filter event to the append-only filter log for daily summary reporting.
    /// </summary>
    public void LogFiltered(string from, string reason)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilterLogPath)!);
            var entry = JsonSerializer.Serialize(new FilterLogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                From      = from,
                Reason    = reason
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            File.AppendAllText(FilterLogPath, entry + "\n");
        }
        catch { /* non-fatal */ }
    }

    // ─── Private ──────────────────────────────────────────────────────────────

    private void MaybeReload()
    {
        lock (_lock)
        {
            _pollsSinceReload++;
            if (_pollsSinceReload >= _reloadEveryNPolls)
                Reload();
        }
    }

    private void Reload()
    {
        _pollsSinceReload = 0;
        if (!File.Exists(StorePath))
        {
            _entries = [];
            return;
        }

        try
        {
            var json = File.ReadAllText(StorePath);
            _entries = JsonSerializer.Deserialize<List<BlockedSenderEntry>>(json, JsonOpts) ?? [];
        }
        catch { /* keep previous entries on parse error */ }
    }

    private void TouchEntry(BlockedSenderEntry entry)
    {
        entry.MatchCount    = (entry.MatchCount ?? 0) + 1;
        entry.LastMatchedAt = DateTimeOffset.UtcNow;
        SaveUnlocked();
    }

    private void AutoAdd(string from, string reason)
    {
        lock (_lock)
        {
            // Double-check after acquiring lock
            if (_entries.Any(e => e.Address.Equals(from, StringComparison.OrdinalIgnoreCase)))
                return;

            _entries.Add(new BlockedSenderEntry
            {
                Address       = from,
                Reason        = reason,
                BlockedAt     = DateTimeOffset.UtcNow,
                BlockedBy     = "auto-detected",
                MatchCount    = 1,
                LastMatchedAt = DateTimeOffset.UtcNow
            });
            SaveUnlocked();
        }
    }

    private void SaveUnlocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(_entries, JsonOpts));
        }
        catch { /* non-fatal */ }
    }

    private static string? DetectNoReplyPattern(string from)
    {
        var local = from.Contains('@') ? from[..from.IndexOf('@')] : from;
        var domain = from.Contains('@') ? from[(from.IndexOf('@') + 1)..] : "";

        foreach (var prefix in NoReplyPrefixes)
        {
            // Strip the trailing '@' from prefix for local-part matching
            var localPrefix = prefix.TrimEnd('@');
            if (local.Equals(localPrefix, StringComparison.OrdinalIgnoreCase) ||
                local.StartsWith(localPrefix, StringComparison.OrdinalIgnoreCase))
                return $"automated sender ({prefix.TrimEnd('@')} address)";
        }

        foreach (var domainPrefix in NoReplyDomainPrefixes)
        {
            if (domain.StartsWith(domainPrefix, StringComparison.OrdinalIgnoreCase))
                return $"automated sender (domain prefix: {domainPrefix.TrimEnd('.')})";
        }

        return null;
    }
}

public sealed class BlockedSenderEntry
{
    public required string Address       { get; set; }
    public required string Reason        { get; set; }
    public DateTimeOffset  BlockedAt     { get; set; }
    public string?         BlockedBy     { get; set; }   // "auto-detected" | "daeanne" | "user"
    public int?            MatchCount    { get; set; }
    public DateTimeOffset? LastMatchedAt { get; set; }
    public string?         Notes         { get; set; }   // optional free-text from Daeanne
}

internal sealed class FilterLogEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public required string From     { get; set; }
    public required string Reason   { get; set; }
}
