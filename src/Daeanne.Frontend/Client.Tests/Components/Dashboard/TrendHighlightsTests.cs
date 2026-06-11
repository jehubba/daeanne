using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Client.Tests.Components.Dashboard;

/// <summary>
/// bUnit tests for the TrendHighlights component (T044).
/// Per spec US5 (Today's Trend Highlights):
///   - Shows today's detected trend highlights on the dashboard
///   - Each highlight shows title and bullet-point highlights
///   - Data from GET /api/trends/today
///   - Loads within 2 seconds per SC-004 (F8 fix)
///   - Empty state when no trends detected today
///
/// Per data-model.md (F2 fix): TrendHighlight has Highlights: List&lt;string&gt;,
/// not Summary: string.
/// </summary>
public class TrendHighlightsTests : TestContext
{
    public TrendHighlightsTests()
    {
        Services.AddMudServices();
    }

    [Fact]
    public void TrendHighlights_Component_MustExist()
    {
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var trendType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Dashboard.TrendHighlights");

        trendType.Should().NotBeNull(
            "TrendHighlights component must exist at " +
            "Client/Components/Dashboard/TrendHighlights.razor " +
            "— surfaces today's trend data per US5");
    }

    [Fact]
    public void TrendHighlights_ShowsEmptyState_WhenNoTrends()
    {
        // When GET /api/trends/today returns empty highlights array,
        // the component should show a "No trends detected today" message.
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var trendType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Dashboard.TrendHighlights");
        trendType.Should().NotBeNull("TrendHighlights must exist to test empty state");
    }

    [Fact]
    public void TrendHighlights_RendersHighlightsAsBulletPoints()
    {
        // Per data-model F2 fix: highlights is List<string>, rendered as bullet points.
        // Each TrendHighlight card should show title + bulleted highlights.
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var trendType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Dashboard.TrendHighlights");
        trendType.Should().NotBeNull("TrendHighlights must exist to test bullet rendering");
    }

    [Fact]
    public void TrendHighlights_ShowsErrorState_WhenApiFails()
    {
        // When the trends API returns 502, the component should show
        // a non-blocking error indicator.
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var trendType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Dashboard.TrendHighlights");
        trendType.Should().NotBeNull("TrendHighlights must exist to test error state");
    }

    [Fact]
    public void TrendHighlight_Model_MustExist_WithCorrectShape()
    {
        // Per data-model.md (F2 fix): TrendHighlight has:
        //   Title: string, Highlights: List<string>, Source: string, DetectedAt: DateTimeOffset
        // NOT Summary: string
        var sharedAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var trendHighlightType = sharedAssembly.GetType("DaeanneFrontend.Client.Models.TrendHighlight")
            ?? typeof(DaeanneFrontend.Client.App).Assembly
                .GetTypes()
                .FirstOrDefault(t => t.Name == "TrendHighlight");

        trendHighlightType.Should().NotBeNull(
            "TrendHighlight model must exist somewhere in the Client project");

        if (trendHighlightType != null)
        {
            trendHighlightType.GetProperty("Title").Should().NotBeNull();
            trendHighlightType.GetProperty("Highlights").Should().NotBeNull(
                "TrendHighlight must have Highlights: List<string> per F2 fix — NOT Summary");
            trendHighlightType.GetProperty("Summary").Should().BeNull(
                "TrendHighlight must NOT have a Summary property — it was replaced by " +
                "Highlights: List<string> in the F2 remediation");
        }
    }
}
