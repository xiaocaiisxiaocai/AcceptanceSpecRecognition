using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

public class AuthUserOrgUnitSingleOrgPolicyTests
{
    [Fact]
    public void SelectOrgUnitToKeep_WhenHasSinglePrimary_ShouldPreferPrimary()
    {
        var createdAt = new DateTime(2026, 3, 25, 10, 0, 0, DateTimeKind.Local);
        var orgLinks = new[]
        {
            new AuthUserOrgUnit
            {
                Id = 2,
                OrgUnitId = 2,
                IsPrimary = false,
                CreatedAt = createdAt
            },
            new AuthUserOrgUnit
            {
                Id = 1,
                OrgUnitId = 1,
                IsPrimary = true,
                CreatedAt = createdAt.AddMinutes(5)
            }
        };

        var selected = AuthUserOrgUnitSingleOrgPolicy.SelectOrgUnitToKeep(orgLinks);

        selected.Should().NotBeNull();
        selected!.OrgUnitId.Should().Be(1);
    }

    [Fact]
    public void SelectOrgUnitToKeep_WhenNoUniquePrimary_ShouldKeepEarliestRecord()
    {
        var createdAt = new DateTime(2026, 3, 25, 10, 0, 0, DateTimeKind.Local);
        var orgLinks = new[]
        {
            new AuthUserOrgUnit
            {
                Id = 9,
                OrgUnitId = 9,
                IsPrimary = false,
                CreatedAt = createdAt.AddMinutes(10)
            },
            new AuthUserOrgUnit
            {
                Id = 3,
                OrgUnitId = 3,
                IsPrimary = false,
                CreatedAt = createdAt
            }
        };

        var selected = AuthUserOrgUnitSingleOrgPolicy.SelectOrgUnitToKeep(orgLinks);

        selected.Should().NotBeNull();
        selected!.Id.Should().Be(3);
        selected.OrgUnitId.Should().Be(3);
    }

    [Fact]
    public void SelectOrgUnitToKeep_WhenMultiplePrimary_ShouldKeepEarliestRecord()
    {
        var createdAt = new DateTime(2026, 3, 25, 10, 0, 0, DateTimeKind.Local);
        var orgLinks = new[]
        {
            new AuthUserOrgUnit
            {
                Id = 6,
                OrgUnitId = 6,
                IsPrimary = true,
                CreatedAt = createdAt.AddMinutes(10)
            },
            new AuthUserOrgUnit
            {
                Id = 4,
                OrgUnitId = 4,
                IsPrimary = true,
                CreatedAt = createdAt
            }
        };

        var selected = AuthUserOrgUnitSingleOrgPolicy.SelectOrgUnitToKeep(orgLinks);

        selected.Should().NotBeNull();
        selected!.Id.Should().Be(4);
    }

    [Fact]
    public void SelectOrgUnitToKeep_WhenEmpty_ShouldReturnNull()
    {
        var selected = AuthUserOrgUnitSingleOrgPolicy.SelectOrgUnitToKeep([]);

        selected.Should().BeNull();
    }
}
