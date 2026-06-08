using System.Text.Json;
using System.Text.RegularExpressions;
using Daeanne.Shared.Models;

namespace Daeanne.Dispatcher.Services;

public sealed class PreferenceMemoryService(ILogger<PreferenceMemoryService> logger)
{
    private const int CurrentVersion = 1;
    private const int MaxObservedPatterns = 50;
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

    public void UpdateFromTaskClose(AgentTask task)
    {
        if (string.IsNullOrWhiteSpace(task.Prompt))
            return;

        var updates = ExtractUpdates(task.Prompt).ToList();
        if (updates.Count == 0)
            return;

        lock (_sync)
        {
            var document = LoadUnsafe();
            var changed = false;

            foreach (var update in updates)
            {
                changed |= ApplyUpdate(document, update);
            }

            if (!changed)
                return;

            document.LastUpdated = DateTime.UtcNow;
            Prune(document);
            SaveUnsafe(document);
            logger.LogInformation("Updated preferences from task close hook for task {TaskId}", task.Id);
        }
    }

    private PreferenceDocument Load()
    {
        lock (_sync)
        {
            return LoadUnsafe();
        }
    }

    private PreferenceDocument LoadUnsafe()
    {
        EnsurePreferencesFileExists();
        try
        {
            var json = File.ReadAllText(_preferencesPath);
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
        document.Version = CurrentVersion;
        document.LastUpdated = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(document, JsonOptions);
        File.WriteAllText(_preferencesPath, json);
    }

    private static IEnumerable<PreferenceUpdate> ExtractUpdates(string prompt)
    {
        if (prompt.Contains("just give me the bullets", StringComparison.OrdinalIgnoreCase))
        {
            yield return new PreferenceUpdate(
                "communication",
                "format",
                "markdown, bullet findings for research, prose for analysis",
                false);
        }

        if (prompt.Contains("executive summary", StringComparison.OrdinalIgnoreCase))
        {
            yield return new PreferenceUpdate(
                "communication",
                "preferredLength",
                "executive-summary by default, detail on request",
                false);
        }

        if (prompt.Contains("no pleasantries", StringComparison.OrdinalIgnoreCase))
        {
            yield return new PreferenceUpdate(
                "communication",
                "tone",
                "direct, no pleasantries",
                false);
        }

        if (prompt.Contains("clear tradeoffs", StringComparison.OrdinalIgnoreCase))
        {
            yield return new PreferenceUpdate(
                "workingStyle",
                "decisionStyle",
                "prefers options with clear tradeoffs, not open-ended questions",
                false);
        }

        foreach (Match match in Regex.Matches(prompt,
                     @"\bprefers?\s+(?<preferred>[^.,;\n]{1,80}?)\s+over\s+(?<alternative>[^.,;\n]{1,80}?)([.!,;]|$)",
                     RegexOptions.IgnoreCase))
        {
            var preferred = match.Groups["preferred"].Value.Trim();
            var alternative = match.Groups["alternative"].Value.Trim();
            if (string.IsNullOrWhiteSpace(preferred) || string.IsNullOrWhiteSpace(alternative))
                continue;

            yield return new PreferenceUpdate(
                "topicContext",
                "preference",
                $"prefers {preferred} over {alternative}",
                true);
        }
    }

    private static bool ApplyUpdate(PreferenceDocument document, PreferenceUpdate update)
    {
        var key = update.Key.Trim();
        var value = update.Value.Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            return false;

        var category = update.Category.Trim();
        if (category.Equals("communication", StringComparison.OrdinalIgnoreCase))
            return ApplyExplicitMapUpdate(
                document.Communication,
                category: "communication",
                key,
                value,
                update.Inferred,
                document);

        if (category.Equals("workingStyle", StringComparison.OrdinalIgnoreCase))
            return ApplyExplicitMapUpdate(
                document.WorkingStyle,
                category: "workingStyle",
                key,
                value,
                update.Inferred,
                document);

        if (category.Equals("topicContext", StringComparison.OrdinalIgnoreCase))
            return ApplyTopicContextUpdate(document, key, value, update.Inferred);

        return false;
    }

    private static bool ApplyExplicitMapUpdate(
        Dictionary<string, string> map,
        string category,
        string key,
        string value,
        bool inferred,
        PreferenceDocument document)
    {
        if (inferred)
            return AddObservedPattern(document, category, key, value);

        if (map.TryGetValue(key, out var existing) &&
            string.Equals(existing, value, StringComparison.Ordinal))
        {
            return false;
        }

        map[key] = value;
        return true;
    }

    private static bool ApplyTopicContextUpdate(PreferenceDocument document, string key, string value, bool inferred)
    {
        if (document.TopicContext.TryGetValue(key, out var existing) &&
            string.Equals(existing.Value, value, StringComparison.Ordinal) &&
            existing.Inferred == inferred)
        {
            if (inferred)
                // Keep occurrence counts/timestamps current even when topicContext value is unchanged.
                return AddObservedPattern(document, "topicContext", key, value);
            return false;
        }

        document.TopicContext[key] = new TopicContextPreference
        {
            Value = value,
            Inferred = inferred,
            LastUpdated = DateTime.UtcNow
        };

        if (inferred)
            AddObservedPattern(document, "topicContext", key, value);

        return true;
    }

    private static bool AddObservedPattern(PreferenceDocument document, string category, string key, string value)
    {
        var now = DateTime.UtcNow;
        var pattern = document.ObservedPatterns.FirstOrDefault(p =>
            p.Category.Equals(category, StringComparison.OrdinalIgnoreCase) &&
            p.Key.Equals(key, StringComparison.OrdinalIgnoreCase) &&
            p.Value.Equals(value, StringComparison.OrdinalIgnoreCase));

        if (pattern is null)
        {
            document.ObservedPatterns.Add(new ObservedPattern
            {
                Category = category,
                Key = key,
                Value = value,
                Inferred = true,
                Occurrences = 1,
                FirstObserved = now,
                LastObserved = now
            });
            return true;
        }

        pattern.Occurrences++;
        pattern.LastObserved = now;

        // Never auto-overwrite explicit topic context with inferred patterns.
        if (pattern.Occurrences >= 2 && document.TopicContext.TryGetValue(key, out var topic) && !topic.Inferred)
            return true;

        if (pattern.Occurrences >= 2 && !document.TopicContext.ContainsKey(key))
        {
            document.TopicContext[key] = new TopicContextPreference
            {
                Value = value,
                Inferred = true,
                LastUpdated = now
            };
        }

        return true;
    }

    private static void Prune(PreferenceDocument document)
    {
        if (document.ObservedPatterns.Count <= MaxObservedPatterns)
            return;

        document.ObservedPatterns = document.ObservedPatterns
            .OrderByDescending(p => p.LastObserved)
            .Take(MaxObservedPatterns)
            .ToList();
    }
}

public sealed class PreferenceDocument
{
    public int Version { get; set; } = 1;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Communication { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> WorkingStyle { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, TopicContextPreference> TopicContext { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ObservedPattern> ObservedPatterns { get; set; } = [];

    public static PreferenceDocument CreateDefault()
    {
        var now = DateTime.UtcNow;
        return new PreferenceDocument
        {
            Version = 1,
            LastUpdated = now,
            Communication = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["preferredLength"] = "executive-summary by default, detail on request",
                ["format"] = "markdown, bullet findings for research, prose for analysis",
                ["tone"] = "direct, no pleasantries"
            },
            WorkingStyle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["decisionStyle"] = "prefers options with clear tradeoffs, not open-ended questions",
                ["confirmationPreference"] = "explicit confirm only for irreversible actions",
                ["escalationThreshold"] = "escalate on ambiguity that would waste >10 min if wrong"
            },
            TopicContext = new Dictionary<string, TopicContextPreference>(StringComparer.OrdinalIgnoreCase),
            ObservedPatterns = []
        };
    }

    public static PreferenceDocument Normalize(PreferenceDocument? document)
    {
        if (document is null)
            return CreateDefault();

        document.Communication ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        document.WorkingStyle ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        document.TopicContext ??= new Dictionary<string, TopicContextPreference>(StringComparer.OrdinalIgnoreCase);
        document.ObservedPatterns ??= [];

        return document;
    }
}

public sealed class TopicContextPreference
{
    public string Value { get; set; } = string.Empty;
    public bool Inferred { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public sealed class ObservedPattern
{
    public string Category { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool Inferred { get; set; } = true;
    public int Occurrences { get; set; } = 1;
    public DateTime FirstObserved { get; set; } = DateTime.UtcNow;
    public DateTime LastObserved { get; set; } = DateTime.UtcNow;
}

public readonly record struct PreferenceUpdate(string Category, string Key, string Value, bool Inferred);
