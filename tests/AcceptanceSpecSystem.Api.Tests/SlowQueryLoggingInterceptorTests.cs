using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Api.Services;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class SlowQueryLoggingInterceptorTests
{
    [Fact]
    public void ShouldLog_WhenEnabledAndDurationReachesThreshold()
    {
        var options = new SlowQueryOptions
        {
            Enabled = true,
            ThresholdMilliseconds = 500
        };

        SlowQueryLoggingInterceptor.ShouldLog(options, TimeSpan.FromMilliseconds(500))
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldLog_WhenDisabled_ShouldReturnFalse()
    {
        var options = new SlowQueryOptions
        {
            Enabled = false,
            ThresholdMilliseconds = 500
        };

        SlowQueryLoggingInterceptor.ShouldLog(options, TimeSpan.FromSeconds(10))
            .Should().BeFalse();
    }

    [Fact]
    public void GetThresholdMilliseconds_ShouldClampToAtLeastOne()
    {
        SlowQueryLoggingInterceptor.GetThresholdMilliseconds(new SlowQueryOptions
            {
                ThresholdMilliseconds = 0
            })
            .Should().Be(1);
    }
}
