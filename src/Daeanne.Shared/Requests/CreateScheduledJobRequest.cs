using Daeanne.Shared.Models;

namespace Daeanne.Shared.Requests;

public class CreateScheduledJobRequest
{
    public string          Name            { get; set; } = string.Empty;
    public ScheduledJobType JobType        { get; set; }
    public AgentTaskType   TaskType        { get; set; }
    public string          Prompt          { get; set; } = string.Empty;

    /// <summary>once: ISO 8601 datetime. daily/weekly: "HH:mm". interval: ignored.</summary>
    public string? RunAt           { get; set; }
    public string? DayOfWeek       { get; set; }   // weekly only: "Monday".."Sunday"
    public int?    IntervalMinutes { get; set; }    // interval only

    public string? CorrelationIdTemplate { get; set; }

    /// <summary>
    /// Optional stable Copilot session name (maps to --name flag).
    /// When set, the agent accumulates context across separate firings of this job.
    /// </summary>
    public string? SessionName { get; set; }
}
