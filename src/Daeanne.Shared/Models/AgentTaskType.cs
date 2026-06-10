namespace Daeanne.Shared.Models;

public enum AgentTaskType
{
    Research,
    Scheduling,
    Code,
    Email,
    Generic,
    DailySummary,
    WeeklyOneOnOne,
    InboundSms,
    Diagnostic,
    TrendAnalyzer,
    SitRep,
    Test           // Pipeline / integration tests — excluded from functional metrics
}
