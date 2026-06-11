namespace DaeanneFrontend.Client.Models;

public record TrendHighlight(
    string Title,
    List<string> Highlights,
    string Source,
    DateTimeOffset DetectedAt);
