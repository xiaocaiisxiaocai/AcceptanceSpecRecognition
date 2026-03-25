using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

public class AuthUserRoleSingleRolePolicyTests
{
    [Fact]
    public void SelectRoleToKeep_WhenContainsAdmin_ShouldPreferAdmin()
    {
        var createdAt = new DateTime(2026, 3, 25, 10, 0, 0, DateTimeKind.Local);
        var roles = new[]
        {
            new AuthUserRole
            {
                Id = 2,
                RoleId = 2,
                CreatedAt = createdAt,
                Role = new AuthRole { Id = 2, Code = "common", Name = "普通用户" }
            },
            new AuthUserRole
            {
                Id = 1,
                RoleId = 1,
                CreatedAt = createdAt.AddMinutes(5),
                Role = new AuthRole { Id = 1, Code = "admin", Name = "管理员" }
            }
        };

        var selected = AuthUserRoleSingleRolePolicy.SelectRoleToKeep(roles);

        selected.Should().NotBeNull();
        selected!.Role.Code.Should().Be("admin");
    }

    [Fact]
    public void SelectRoleToKeep_WhenNoAdmin_ShouldKeepEarliestRecord()
    {
        var createdAt = new DateTime(2026, 3, 25, 10, 0, 0, DateTimeKind.Local);
        var roles = new[]
        {
            new AuthUserRole
            {
                Id = 9,
                RoleId = 2,
                CreatedAt = createdAt.AddMinutes(10),
                Role = new AuthRole { Id = 2, Code = "common", Name = "普通用户" }
            },
            new AuthUserRole
            {
                Id = 3,
                RoleId = 3,
                CreatedAt = createdAt,
                Role = new AuthRole { Id = 3, Code = "qa", Name = "质检" }
            }
        };

        var selected = AuthUserRoleSingleRolePolicy.SelectRoleToKeep(roles);

        selected.Should().NotBeNull();
        selected!.Id.Should().Be(3);
        selected.Role.Code.Should().Be("qa");
    }

    [Fact]
    public void SelectRoleToKeep_WhenEmpty_ShouldReturnNull()
    {
        var selected = AuthUserRoleSingleRolePolicy.SelectRoleToKeep([]);

        selected.Should().BeNull();
    }
}
