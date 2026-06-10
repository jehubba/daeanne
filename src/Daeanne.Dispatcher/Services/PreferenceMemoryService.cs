using System.Text.Json;
using Daeanne.Shared.Models;

namespace Daeanne.Dispatcher.Services;

/// <summary>
/// Persists principal preference memory to %APPDATA%\daeanne\preferences.json.
///
/// Write path: Daeanne explicitly calls PATCH /preferences when she observes
/// a clear preference signal from Jeffrey. C# is a dumb store — no inference here.
///
/// Read path: BuildPrincipalPreferencesBlock() is injected into every dispatched
/// task prompt so all agents inherit Jeffrey's communication and working-style prefs.
/// </summary>
public sealed class PreferenceMemoryService(ILogger<PreferenceMemoryService> logger)
{
    private const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly string _preferencesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "daeanne",
        "preferences.json");

    public string PreferencesPath => _preferencesPath;

    public void EnsurePreferencesFileExists()
    {
        lock (_sync)
        {
            if (File.Exists(_preferencesPath))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(_preferencesPath)!);
            var defaults = PreferenceDocument.CreateDefault();
            SaveUnsafe(defaults);
            logger.LogInformation("Created preferences file at {Path}", _preferencesPath);
        }
    }

    public string BuildPrincipalPreferencesBlock()
    {
        var preferences = Load();
        var json = JsonSerializer.Serialize(preferences, JsonOptions);
        return $"""
            ## Principal Preferences
            ```json
            {json}
            ```
            """;
    }

    /// <summary>
    /// Applies explicit preference updates as reported by Daeanne.
    /// Each update merges into the matching category dictionary.
    /// Unknown categories are ignored — only "communication" and "workingStyle" are supported.
    /// </summary>
    public void ApplyExplicit(IEnumerable<PreferenceUpdate> updates)
    {
        var list = updates.ToList();
        if (list.Count == 0) return;

        lock (_sync)
        {
            var document = LoadUnsafe();
            var changed  = false;

            foreach (var u in list)
            {
                var key   = u.Key.Trim();
                var value = u.Value.Trim();
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) continue;

                var map = u.Category.Trim().ToLowerInvariant() switch
                {
                    "communication" => document.Communication,
                    "workingstyle"  => document.WorkingStyle,
                    _               => null
                };

                if (map is null)
                {
                    logger.LogWarning("ApplyExplicit: unknown preference category '{Cat}' — skipped.", u.Category);
                    continue;
                }

                if (!map.TryGetValue(key, out var existing) || existing != value)
                {
                    map[key] = value;
                    changed  = true;
                    logger.LogInformation("Preference updated: [{Cat}] {Key} = {Value}", u.Category, key, value);
                }
            }

            if (!changed) return;
            document.LastUpdated = DateTime.UtcNow;
            SaveUnsafe(document);
        }
    }

    private PreferenceDocument Load()
    {
        lock (_sync) { return LoadUnsafe(); }
    }

    private PreferenceDocument LoadUnsafe()
    {
        EnsurePreferencesFileExists();
        try
        {
            var json   = File.ReadAllText(_preferencesPath);
            var parsed = JsonSerializer.Deserialize<PreferenceDocument>(json, JsonOptions);
            return PreferenceDocument.Normalize(parsed);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse preferences file at {Path}. Recreating defaults.", _preferencesPath);
            var defaults = PreferenceDocument.CreateDefault();
            SaveUnsafe(defaults);
            return defaults;
        }
    }

    private void SaveUnsafe(PreferenceDocument document)
    {
        document.Version     = CurrentVersion;
        document.LastUpdated = DateTime.UtcNow;
        File.WriteAllText(_preferencesPath, JsonSerializer.Serialize(document, JsonOptions));
    }
}

public sealed class PreferenceDocument
{
    public int      Version     { get; set; } = 1;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Communication { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> WorkingStyle  { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static PreferenceDocument CreateDefault() => new()
    {
        Version     = 1,
        LastUpdated = DateTime.UtcNow,
        Communication = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["preferredLength"] = "executive-summary by default, detail on request",
            ["format"]          = "markdown, bullet findings for research, prose for analysis",
            ["tone"]            = "direct, no pleasantries"
        },
        WorkingStyle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["decisionStyle"]          = "prefers options with clear tradeoffs, not open-ended questions",
            ["confirmationPreference"] = "explicit confirm only for irreversible actions",
            ["escalationThreshold"]    = "escalate on ambiguity that would waste >10 min if wrong"
        }
    };

    public static PreferenceDocument Normalize(PreferenceDocument? document)
    {
        if (document is null) return CreateDefault();
        document.Communication ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        document.WorkingStyle  ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return document;
    }
}

public readonly record struct PreferenceUpdate(string Category, string Key, string Value);


