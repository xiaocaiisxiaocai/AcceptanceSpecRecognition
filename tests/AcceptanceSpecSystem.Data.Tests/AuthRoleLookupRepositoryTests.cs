using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

public class AuthRoleLookupRepositoryTests : TestBase
{
    private readonly AuthRoleLookupRepository _repository;

    public AuthRoleLookupRepositoryTests()
    {
        _repository = new AuthRoleLookupRepository(Context);
    }

    [Fact]
    public async Task GetCompanyRolesAsync_ShouldReturnOnlyActiveRolesInCompanyOrderedByCode()
    {
        var company1 = new OrgCompany { Code = "c1", Name = "公司1" };
        var company2 = new OrgCompany { Code = "c2", Name = "公司2" };
        Context.AuthRoles.AddRange(
            new AuthRole { Company = company1, Code = "viewer", Name = "查看者" },
            new AuthRole { Company = company1, Code = "admin", Name = "管理员" },
            new AuthRole { Company = company1, Code = "disabled", Name = "停用角色", IsActive = false },
            new AuthRole { Company = company2, Code = "other", Name = "其他公司角色" });
        await Context.SaveChangesAsync();

        var result = await _repository.GetCompanyRolesAsync(company1.Id);

        result.Select(item => item.Code)
            .Should()
            .Equal("admin", "viewer");
    }

    [Fact]
    public async Task GetRoleCodeMapAsync_ShouldScopeByCompanyAndIgnoreDuplicateIds()
    {
        var company1 = new OrgCompany { Code = "c1", Name = "公司1" };
        var company2 = new OrgCompany { Code = "c2", Name = "公司2" };
        var admin = new AuthRole { Company = company1, Code = "admin", Name = "管理员" };
        var viewer = new AuthRole { Company = company1, Code = "viewer", Name = "查看者" };
        var other = new AuthRole { Company = company2, Code = "other", Name = "其他公司角色" };
        Context.AuthRoles.AddRange(admin, viewer, other);
        await Context.SaveChangesAsync();

        var result = await _repository.GetRoleCodeMapAsync(
            company1.Id,
            [admin.Id, admin.Id, viewer.Id, other.Id]);

        result.Should().HaveCount(2);
        result[admin.Id].Should().Be("admin");
        result[viewer.Id].Should().Be("viewer");
        result.Should().NotContainKey(other.Id);
    }
}
