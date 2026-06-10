using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Api.Tests;

public class AuthDataScopeServiceTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public AuthDataScopeServiceTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task EnsureSeedUsersAsync_ShouldConfigureCommonRoleSpecScope_AsDynamicPrimaryOrgSubtree()
    {
        await AuthUserSeedService.EnsureSeedUsersAsync(_factory.Services, NullLogger.Instance);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var commonRole = await dbContext.AuthRoles
            .Include(role => role.DataScopes)
                .ThenInclude(scopeEntity => scopeEntity.Nodes)
            .SingleAsync(role => role.Code == "common");

        var specScope = commonRole.DataScopes.Single(scopeEntity => scopeEntity.Resource == "spec");
        specScope.ScopeType.Should().Be(DataScopeType.OrgSubtree);
        specScope.Nodes.Should().BeEmpty();
    }

    [Fact]
    public async Task GetScopeAsync_ShouldFollowUsersPrimaryOrgSubtree_WhenCommonRoleHasNoStaticNodes()
    {
        await AuthUserSeedService.EnsureSeedUsersAsync(_factory.Services, NullLogger.Instance);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var authDataScopeService = scope.ServiceProvider.GetRequiredService<IAuthDataScopeService>();

        var rootOrg = await dbContext.OrgUnits
            .AsNoTracking()
            .SingleAsync(org => org.UnitType == OrgUnitType.Company && org.ParentId == null);
        var commonUser = await dbContext.SystemUsers
            .SingleAsync(user => user.Username == AuthUserSeedService.DefaultCommonUsername);

        var division = new OrgUnit
        {
            CompanyId = commonUser.CompanyId,
            ParentId = rootOrg.Id,
            UnitType = OrgUnitType.Division,
            Code = $"DIV-{Guid.NewGuid():N}"[..12],
            Name = "测试事业部",
            Path = rootOrg.Path,
            Depth = rootOrg.Depth + 1,
            Sort = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.OrgUnits.Add(division);
        await dbContext.SaveChangesAsync();

        division.Path = $"{rootOrg.Path}{division.Id}/";

        var department = new OrgUnit
        {
            CompanyId = commonUser.CompanyId,
            ParentId = division.Id,
            UnitType = OrgUnitType.Department,
            Code = $"DEP-{Guid.NewGuid():N}"[..12],
            Name = "测试部门",
            Path = division.Path,
            Depth = division.Depth + 1,
            Sort = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var siblingDivision = new OrgUnit
        {
            CompanyId = commonUser.CompanyId,
            ParentId = rootOrg.Id,
            UnitType = OrgUnitType.Division,
            Code = $"DIV-{Guid.NewGuid():N}"[..12],
            Name = "旁路事业部",
            Path = rootOrg.Path,
            Depth = rootOrg.Depth + 1,
            Sort = 2,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.OrgUnits.AddRange(department, siblingDivision);
        await dbContext.SaveChangesAsync();

        department.Path = $"{division.Path}{department.Id}/";
        siblingDivision.Path = $"{rootOrg.Path}{siblingDivision.Id}/";

        var userOrgLinks = await dbContext.AuthUserOrgUnits
            .Where(link => link.UserId == commonUser.Id)
            .ToListAsync();
        dbContext.AuthUserOrgUnits.RemoveRange(userOrgLinks);
        dbContext.AuthUserOrgUnits.Add(new AuthUserOrgUnit
        {
            UserId = commonUser.Id,
            OrgUnitId = division.Id,
            IsPrimary = true,
            CreatedAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var scopeResult = await authDataScopeService.GetScopeAsync(commonUser.Id, commonUser.CompanyId, "spec");

        scopeResult.Should().NotBeNull();
        scopeResult!.IsAll.Should().BeFalse();
        scopeResult.IncludeSelf.Should().BeFalse();
        scopeResult.OrgUnitId.Should().Be(division.Id);
        scopeResult.OrgUnitIds.Should().Contain(division.Id);
        scopeResult.OrgUnitIds.Should().Contain(department.Id);
        scopeResult.OrgUnitIds.Should().NotContain(rootOrg.Id);
        scopeResult.OrgUnitIds.Should().NotContain(siblingDivision.Id);
    }

    [Fact]
    public async Task GetScopeAsync_WhenUserHasSingleNonPrimaryOrgLink_ShouldUseThatOrgSubtree()
    {
        await AuthUserSeedService.EnsureSeedUsersAsync(_factory.Services, NullLogger.Instance);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var authDataScopeService = scope.ServiceProvider.GetRequiredService<IAuthDataScopeService>();

        var rootOrg = await dbContext.OrgUnits
            .AsNoTracking()
            .SingleAsync(org => org.UnitType == OrgUnitType.Company && org.ParentId == null);
        var commonUser = await dbContext.SystemUsers
            .SingleAsync(user => user.Username == AuthUserSeedService.DefaultCommonUsername);

        var divisionA = new OrgUnit
        {
            CompanyId = commonUser.CompanyId,
            ParentId = rootOrg.Id,
            UnitType = OrgUnitType.Division,
            Code = $"DIVA-{Guid.NewGuid():N}"[..12],
            Name = "范围组织A",
            Path = rootOrg.Path,
            Depth = rootOrg.Depth + 1,
            Sort = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var divisionB = new OrgUnit
        {
            CompanyId = commonUser.CompanyId,
            ParentId = rootOrg.Id,
            UnitType = OrgUnitType.Division,
            Code = $"DIVB-{Guid.NewGuid():N}"[..12],
            Name = "范围组织B",
            Path = rootOrg.Path,
            Depth = rootOrg.Depth + 1,
            Sort = 2,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.OrgUnits.AddRange(divisionA, divisionB);
        await dbContext.SaveChangesAsync();

        divisionA.Path = $"{rootOrg.Path}{divisionA.Id}/";
        divisionB.Path = $"{rootOrg.Path}{divisionB.Id}/";

        var departmentA = new OrgUnit
        {
            CompanyId = commonUser.CompanyId,
            ParentId = divisionA.Id,
            UnitType = OrgUnitType.Department,
            Code = $"DEPA-{Guid.NewGuid():N}"[..12],
            Name = "范围组织A-部门",
            Path = divisionA.Path,
            Depth = divisionA.Depth + 1,
            Sort = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var departmentB = new OrgUnit
        {
            CompanyId = commonUser.CompanyId,
            ParentId = divisionB.Id,
            UnitType = OrgUnitType.Department,
            Code = $"DEPB-{Guid.NewGuid():N}"[..12],
            Name = "范围组织B-部门",
            Path = divisionB.Path,
            Depth = divisionB.Depth + 1,
            Sort = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.OrgUnits.AddRange(departmentA, departmentB);
        await dbContext.SaveChangesAsync();

        departmentA.Path = $"{divisionA.Path}{departmentA.Id}/";
        departmentB.Path = $"{divisionB.Path}{departmentB.Id}/";

        var userOrgLinks = await dbContext.AuthUserOrgUnits
            .Where(link => link.UserId == commonUser.Id)
            .ToListAsync();
        dbContext.AuthUserOrgUnits.RemoveRange(userOrgLinks);
        dbContext.AuthUserOrgUnits.Add(new AuthUserOrgUnit
        {
            UserId = commonUser.Id,
            OrgUnitId = divisionA.Id,
            IsPrimary = false,
            CreatedAt = new DateTime(2026, 3, 25, 10, 0, 0, DateTimeKind.Local)
        });

        await dbContext.SaveChangesAsync();

        var scopeResult = await authDataScopeService.GetScopeAsync(commonUser.Id, commonUser.CompanyId, "spec");

        scopeResult.Should().NotBeNull();
        scopeResult!.OrgUnitId.Should().Be(divisionA.Id);
        scopeResult!.OrgUnitIds.Should().Contain(divisionA.Id);
        scopeResult.OrgUnitIds.Should().Contain(departmentA.Id);
        scopeResult.OrgUnitIds.Should().NotContain(divisionB.Id);
        scopeResult.OrgUnitIds.Should().NotContain(departmentB.Id);
    }
}
