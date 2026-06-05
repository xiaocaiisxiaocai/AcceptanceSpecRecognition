using AcceptanceSpecSystem.Core.Diagnostics;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class SensitiveLogFormatterTests
{
    [Fact]
    public void DescribePayload_ShouldReturnLengthAndHashWithoutRawContent()
    {
        var raw = "项目A/规格B/验收标准C token=secret";

        var summary = SensitiveLogFormatter.DescribePayload(raw);

        summary.Should().Contain("length=");
        summary.Should().Contain("sha256=");
        summary.Should().NotContain("项目A");
        summary.Should().NotContain("规格B");
        summary.Should().NotContain("token=secret");
    }

    [Fact]
    public void SanitizeMessage_WhenMessageContainsSensitiveMarkers_ShouldReturnFallback()
    {
        var message = "server=db;password=secret;token=abc";

        var sanitized = SensitiveLogFormatter.SanitizeMessage(message, "已脱敏");

        sanitized.Should().Be("已脱敏");
    }
}
