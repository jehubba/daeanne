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
    MorningBriefing,
    InboundSms,
    Diagnostic,
    TrendAnalyzer,
    SitRep,
    RepoBranchScan,
    Test           // Pipeline / integration tests — excluded from functional metrics
}
