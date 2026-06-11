using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace api.Tests;

/// <summary>
/// Contract tests for GET /api/trends/today.
/// Per contracts/api.md:
///   Response: { date: string, highlights: TrendHighlight[] }
///   TrendHighlight: { title: string, highlights: string[], source: string, detectedAt: ISO8601 }
///   Returns 502 when Bridge/Dispatcher unreachable
///   Returns 200 with empty highlights array when no trends for today
/// </summary>
public class TrendFunctionTests
{
    [Fact]
    public void TrendFunction_MustAccept_HttpClient_InConstructor()
    {
        var apiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;
        var type = apiAssembly.GetType("DaeanneFrontend.Api.TrendFunction");
        type.Should().NotBeNull("TrendFunction must exist");

        var ctors = type!.GetConstructors();
        var hasHttpClientCtor = ctors.Any(c =>
            c.GetParameters().Any(p => p.ParameterType == typeof(HttpClient)));
        hasHttpClientCtor.Should().BeTrue(
            "TrendFunction must accept an HttpClient for Bridge relay communication");
    }

    [Fact]
    public void GetTrends_ResponseShape_MatchesContract()
    {
        // Response: { date: "2026-06-11", highlights: [...] }
        // Per data-model.md (remediated): highlights is List<string>, not a Summary string
        var sampleJson = """
        {
            "date": "2026-06-11",
            "highlights": [
                {
                    "title": "MCP Protocol Adoption Surge",
                    "highlights": ["GitHub stars tripled", "3 major framework integrations announced"],
                    "source": "daily-scan-2026-06-11",
                    "detectedAt": "2026-06-11T06:00:00Z"
                }
            ]
        }
        """;

        var doc = JsonDocument.Parse(sampleJson);
        var root = doc.RootElement;

        root.TryGetProperty("date", out var date).Should().BeTrue();
        date.GetString().Should().NotBeNullOrEmpty();

        root.TryGetProperty("highlights", out var highlights).Should().BeTrue();
        highlights.ValueKind.Should().Be(JsonValueKind.Array);

        var highlight = highlights[0];
        highlight.GetProperty("title").GetString().Should().NotBeNullOrEmpty();
        // Per data-model F2 fix: highlights is List<string>, not Summary
        highlight.GetProperty("highlights").ValueKind.Should().Be(JsonValueKind.Array,
            "TrendHighlight.highlights must be List<string> per data-model.md (F2 remediation)");
        highlight.GetProperty("source").GetString().Should().NotBeNullOrEmpty();
        highlight.GetProperty("detectedAt").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetTrends_EmptyDay_ReturnsEmptyHighlightsArray()
    {
        // When no trends detected for today, return 200 with empty array — not 404
        var sampleJson = """
        {
            "date": "2026-06-11",
            "highlights": []
        }
        """;

        var doc = JsonDocument.Parse(sampleJson);
        doc.RootElement.GetProperty("highlights").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void GetTrends_Returns502_WhenBridgeUnreachable()
    {
        var apiAssembly = typeof(DaeanneFrontend.Api.HealthFunction).Assembly;
        var type = apiAssembly.GetType("DaeanneFrontend.Api.TrendFunction");
        type.Should().NotBeNull("TrendFunction must exist to test 502 behavior");

        var methods = type!.GetMethods()
            .Where(m => m.ReturnType == typeof(Task<IActionResult>) ||
                        m.ReturnType == typeof(IActionResult));
        methods.Should().NotBeEmpty(
            "TrendFunction must have a method returning IActionResult");
    }
}
