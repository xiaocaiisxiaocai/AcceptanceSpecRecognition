using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

public class AcceptanceSpecQueryOptionsTests
{
    [Theory]
    [InlineData(-10, 1)]
    [InlineData(20, 20)]
    [InlineData(99999, 200)]
    public void PageSize_ShouldBeClampedIntoSupportedRange(int input, int expected)
    {
        var options = new AcceptanceSpecQueryOptions
        {
            PageSize = input
        };

        options.PageSize.Should().Be(expected);
    }
}
