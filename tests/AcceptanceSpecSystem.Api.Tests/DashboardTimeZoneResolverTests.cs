using AcceptanceSpecSystem.Application.Services;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class DashboardTimeZoneResolverTests
{
    [Fact]
    public void TryResolveFixedOffset_WhenAsiaShanghaiHasHistoricalDst_ShouldKeepUtcEightBusinessTime()
    {
        var resolved = DashboardTimeZoneResolver.TryResolveFixedOffset("Asia/Shanghai", out var timeZone);

        resolved.Should().BeTrue();
        timeZone.GetUtcOffset(new DateTime(1988, 7, 1, 0, 0, 0, DateTimeKind.Utc))
            .Should().Be(TimeSpan.FromHours(8));
        timeZone.GetUtcOffset(DateTime.UtcNow).Should().Be(TimeSpan.FromHours(8));
        timeZone.SupportsDaylightSavingTime.Should().BeFalse();
    }

    [Fact]
    public void TryResolveFixedOffset_WhenIdentifierDoesNotExist_ShouldRejectConfiguration()
    {
        DashboardTimeZoneResolver.TryResolveFixedOffset("Not/A-Real-Time-Zone", out _)
            .Should().BeFalse();
    }

    [Fact]
    public void TryResolveFixedOffset_WhenZoneChangesOffsetInDashboardWindow_ShouldRejectConfiguration()
    {
        var changingZoneId = OperatingSystem.IsWindows() ? "Eastern Standard Time" : "America/New_York";

        DashboardTimeZoneResolver.TryResolveFixedOffset(changingZoneId, out _)
            .Should().BeFalse();
    }
}
