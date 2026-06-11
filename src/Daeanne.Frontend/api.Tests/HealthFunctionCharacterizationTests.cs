using DaeanneFrontend.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace api.Tests;

/// <summary>
/// Characterization tests for the existing HealthFunction.
/// These document current behavior before any replacement occurs.
/// If these break, the old code path was inadvertently changed.
/// </summary>
public class HealthFunctionCharacterizationTests
{
    [Fact]
    public void Health_ReturnsOk_WithStatusAndTimestamp()
    {
        // Arrange
        var function = new HealthFunction();
        var context = new DefaultHttpContext();

        // Act
        var result = function.Run(context.Request);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);

        // The response should have 'status' and 'timestamp' properties
        var value = okResult.Value!;
        var statusProp = value.GetType().GetProperty("status");
        var timestampProp = value.GetType().GetProperty("timestamp");

        statusProp.Should().NotBeNull("existing health endpoint returns a 'status' property");
        timestampProp.Should().NotBeNull("existing health endpoint returns a 'timestamp' property");

        statusProp!.GetValue(value).Should().Be("ok");
        timestampProp!.GetValue(value).Should().BeOfType<DateTimeOffset>();
    }
}
