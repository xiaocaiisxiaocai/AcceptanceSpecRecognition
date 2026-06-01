using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

public class SystemUserRepositoryTests : TestBase
{
    private readonly SystemUserRepository _repository;

    public SystemUserRepositoryTests()
    {
        _repository = new SystemUserRepository(Context);
    }

    [Fact]
    public async Task GetByUsernameWithAccessAsync_ShouldLoadOnlyCurrentlyEffectiveAccessRelations()
    {
        var now = DateTime.UtcNow;
        var company = new OrgCompany { Code = "c1", Name = "公司1" };
        var user = new SystemUser
        {
            Company = company,
            Username = "alice",
            Nickname = "Alice",
            PasswordHash = "hash"
        };
        var activeRole = new AuthRole { Company = company, Code = "admin", Name = "管理员" };
        var expiredRole = new AuthRole { Company = company, Code = "common", Name = "普通用户" };
        var permission = new AuthPermission
        {
            Code = "dashboard.view",
            Name = "看板",
            PermissionType = PermissionType.Page,
            Resource = "dashboard",
            Action = "view"
        };
        var activeOrg = new OrgUnit { Company = company, Code = "dept-a", Name = "部门A", Path = "/dept-a" };
        var expiredOrg = new OrgUnit { Company = company, Code = "dept-b", Name = "部门B", Path = "/dept-b" };

        Context.AddRange(company, user, activeRole, expiredRole, permission, activeOrg, expiredOrg);
        await Context.SaveChangesAsync();

        Context.AuthRolePermissions.Add(new AuthRolePermission
        {
            RoleId = activeRole.Id,
            PermissionId = permission.Id
        });
        Context.AuthUserRoles.AddRange(
            new AuthUserRole
            {
                UserId = user.Id,
                RoleId = activeRole.Id,
                StartAt = now.AddDays(-1),
                EndAt = now.AddDays(1)
            },
            new AuthUserRole
            {
                UserId = user.Id,
                RoleId = expiredRole.Id,
                StartAt = now.AddDays(-5),
                EndAt = now.AddDays(-1)
            });
        Context.AuthUserOrgUnits.AddRange(
            new AuthUserOrgUnit
            {
                UserId = user.Id,
                OrgUnitId = activeOrg.Id,
                IsPrimary = true,
                StartAt = now.AddDays(-1),
                EndAt = now.AddDays(1)
            },
            new AuthUserOrgUnit
            {
                UserId = user.Id,
                OrgUnitId = expiredOrg.Id,
                StartAt = now.AddDays(-5),
                EndAt = now.AddDays(-1)
            });
        await Context.SaveChangesAsync();

        Context.ChangeTracker.Clear();

        var result = await _repository.GetByUsernameWithAccessAsync("alice");

        result.Should().NotBeNull();
        result!.UserRoles.Should().ContainSingle();
        result.UserRoles.Single().Role.Code.Should().Be("admin");
        result.UserRoles.Single().Role.RolePermissions.Should().ContainSingle();
        result.UserOrgUnits.Should().ContainSingle();
        result.UserOrgUnits.Single().OrgUnit.Code.Should().Be("dept-a");
    }
}
