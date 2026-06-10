using System.Text.Json;

namespace Daeanne.Tray;

internal sealed class TaskSummary
{
    public string?   Id            { get; set; }
    public string?   Type          { get; set; }
    public string?   Status        { get; set; }
    public string?   Prompt        { get; set; }
    public bool      AgentReported { get; set; }
    public DateTime? StartedAt     { get; set; }
    public DateTime? CompletedAt   { get; set; }
    public DateTime? CreatedAt     { get; set; }
    public string?   Error         { get; set; }
    public string?   WorkDir       { get; set; }
    public string?   ResultJson    { get; set; }

    private string? _agentResponse;
    public string? AgentResponse
    {
        get
        {
            if (_agentResponse is not null) return _agentResponse;
            if (string.IsNullOrWhiteSpace(ResultJson)) return null;
            try
            {
                var r = JsonSerializer.Deserialize<JsonElement>(ResultJson);
                if (r.TryGetProperty("response", out var v)) _agentResponse = v.GetString();
            }
            catch { }
            return _agentResponse;
        }
    }
}

internal sealed class CronJob
{
    public string?   Id          { get; set; }
    public string?   Name        { get; set; }
    public string?   JobType     { get; set; }
    public string?   TaskType    { get; set; }
    public string?   TimeOfDay   { get; set; }
    public string?   DayOfWeek   { get; set; }
    public DateTime? NextRunAt   { get; set; }
    public DateTime? LastFiredAt { get; set; }
    public bool      IsActive    { get; set; }
}
