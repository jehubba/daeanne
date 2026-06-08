namespace Daeanne.Shared.Models;

public class ScheduledJob
{
    public Guid   Id          { get; set; } = Guid.NewGuid();
    public string Name        { get; set; } = string.Empty;

    /// <summary>once | daily | weekly | interval</summary>
    public ScheduledJobType JobType { get; set; }

    /// <summary>
    /// once: fire at this exact UTC time.
    /// daily/weekly: ignored — time-of-day from TimeOfDay.
    /// interval: ignored — use IntervalMinutes.
    /// </summary>
    public DateTime?    RunAt           { get; set; }
    public TimeOnly?    TimeOfDay       { get; set; }  // daily / weekly
    public DayOfWeek?   DayOfWeek       { get; set; }  // weekly only
    public int?         IntervalMinutes { get; set; }  // interval only

    public AgentTaskType TaskType { get; set; }
    public string        Prompt   { get; set; } = string.Empty;

    /// <summary>
    /// Template for the correlationId. Supports {yyyyMMdd}, {HHmm}, {id} tokens.
    /// If empty, no correlationId is set (duplicates allowed).
    /// </summary>
    public string? CorrelationIdTemplate { get; set; }

    public DateTime  NextRunAt  { get; set; }
    public DateTime? LastFiredAt { get; set; }
    public bool      IsActive   { get; set; } = true;
    public DateTime  CreatedAt  { get; set; } = DateTime.UtcNow;
}

public enum ScheduledJobType
{
    Once,
    Daily,
    Weekly,
    Interval
}
