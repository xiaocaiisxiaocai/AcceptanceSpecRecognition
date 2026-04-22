using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

public class AcceptanceSpecQueryOptionsTests
{
    [Theory]
    [InlineData(-10, 1)]
    [InlineData(20, 20)]
    [InlineData(99999, 1000)]
    public void PageSize_ShouldBeClampedIntoSupportedRange(int input, int expected)
    {
        var options = new AcceptanceSpecQueryOptions
        {
            PageSize = input
        };

        options.PageSize.Should().Be(expected);
    }

    [Fact]
    public void ImportedRange_ShouldPreserveProvidedValues()
    {
        var from = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc);
        var options = new AcceptanceSpecQueryOptions
        {
            ImportedFrom = from,
            ImportedTo = to
        };

        options.ImportedFrom.Should().Be(from);
        options.ImportedTo.Should().Be(to);
    }
}
