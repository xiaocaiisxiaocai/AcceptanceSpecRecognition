using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Controllers;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class OrgUnitsTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly ApiWebApplicationFactory _factory;

    public OrgUnitsTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public void Move_ShouldDeclareOrganizationMoveAuditOperation()
    {
        var method = typeof(OrgUnitsController).GetMethod(nameof(OrgUnitsController.Move));

        method.Should().NotBeNull();
        method!
            .GetCustomAttributes(typeof(AuditOperationAttribute), inherit: true)
            .OfType<AuditOperationAttribute>()
            .Should()
            .ContainSingle(attribute =>
                attribute.Operation == "move" &&
                attribute.Resource == "org-unit" &&
                attribute.RecordSuccessful);
    }

    [Fact]
    public async Task Create_WithDownwardSkippedLevel_ShouldPersistChildAndReturnCreatedNode()
    {
        var rootId = await GetRootOrgUnitIdAsync();
        var code = $"SEC-{Guid.NewGuid():N}"[..18].ToUpperInvariant();

        var response = await _client.PostAsync(
            "/api/org-units",
            ApiClientJson.ToJsonContent(new
            {
                parentId = rootId,
                unitType = (int)OrgUnitType.Section,
                code,
                name = "直辖科室",
                sort = 3,
                isActive = true
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(0);
        body.Data.GetProperty("parentId").GetInt32().Should().Be(rootId);
        body.Data.GetProperty("unitType").GetInt32().Should().Be((int)OrgUnitType.Section);
        body.Data.GetProperty("code").GetString().Should().Be(code);
        body.Data.GetProperty("depth").GetInt32().Should().Be(1);
        body.Data.GetProperty("path").GetString().Should().MatchRegex($@"^/{rootId}/\d+/$");
    }

    [Fact]
    public async Task Create_WithSameOrHigherLevelThanParent_ShouldReject()
    {
        var divisionId = await SeedChildOrgUnitAsync(OrgUnitType.Division);

        var response = await _client.PostAsync(
            "/api/org-units",
            ApiClientJson.ToJsonContent(new
            {
                parentId = divisionId,
                unitType = (int)OrgUnitType.Division,
                code = $"DIV-{Guid.NewGuid():N}"[..18],
                name = "非法同级子节点",
                sort = 0,
                isActive = true
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("下级");
    }

    [Fact]
    public async Task Update_WhenRootCompanyIsDisabled_ShouldReject()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rootOrgUnit = await dbContext.OrgUnits.FirstAsync(orgUnit =>
            orgUnit.ParentId == null &&
            orgUnit.UnitType == OrgUnitType.Company);

        var response = await _client.PutAsync(
            $"/api/org-units/{rootOrgUnit.Id}",
            ApiClientJson.ToJsonContent(new
            {
                code = rootOrgUnit.Code,
                name = rootOrgUnit.Name,
                sort = rootOrgUnit.Sort,
                isActive = false
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("公司根节点不允许停用");
    }

    [Fact]
    public async Task GetTree_WhenDatabaseContainsChildOrgUnits_ShouldReturnNestedChildren()
    {
        var childId = await SeedChildOrgUnitAsync();

        var response = await _client.GetAsync("/api/org-units/tree");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(0);
        body.Data.ValueKind.Should().Be(JsonValueKind.Array);
        body.Data.GetArrayLength().Should().Be(1);

        var root = body.Data[0];
        root.GetProperty("unitType").GetInt32().Should().Be((int)OrgUnitType.Company);
        root.GetProperty("children").EnumerateArray()
            .Should()
            .Contain(child => child.GetProperty("id").GetInt32() == childId);
    }

    [Fact]
    public async Task Update_WhenTargetIsChildOrgUnit_ShouldPersistChanges()
    {
        var childId = await SeedChildOrgUnitAsync();
        var code = $"DIV-{Guid.NewGuid():N}"[..18].ToUpperInvariant();

        var response = await _client.PutAsync(
            $"/api/org-units/{childId}",
            ApiClientJson.ToJsonContent(new
            {
                code,
                name = "更新后的事业部",
                sort = 9,
                isActive = true
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Data.GetProperty("code").GetString().Should().Be(code);
        body.Data.GetProperty("name").GetString().Should().Be("更新后的事业部");
        body.Data.GetProperty("sort").GetInt32().Should().Be(9);
    }

    [Fact]
    public async Task Delete_WhenTargetIsUnreferencedLeaf_ShouldRemoveNode()
    {
        var childId = await SeedChildOrgUnitAsync();

        var response = await _client.DeleteAsync($"/api/org-units/{childId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await dbContext.OrgUnits.AnyAsync(orgUnit => orgUnit.Id == childId)).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_WhenTargetHasChild_ShouldRejectWithoutCascade()
    {
        var divisionId = await SeedChildOrgUnitAsync(OrgUnitType.Division);
        await SeedChildOrgUnitAsync(OrgUnitType.Department, divisionId);

        var response = await _client.DeleteAsync($"/api/org-units/{divisionId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("下级组织");
    }

    [Fact]
    public async Task Move_WhenDepartmentHasChild_ShouldMoveWholeSubtreeAndPreserveStableReferences()
    {
        var rootId = await GetRootOrgUnitIdAsync();
        var divisionId = await SeedChildOrgUnitAsync(OrgUnitType.Division, rootId);
        var departmentId = await SeedChildOrgUnitAsync(OrgUnitType.Department, rootId);
        var sectionId = await SeedChildOrgUnitAsync(OrgUnitType.Section, departmentId);
        var references = await SeedStableReferencesAsync(departmentId, sectionId, divisionId);
        using (var beforeScope = _factory.Services.CreateScope())
        {
            var scopeService = beforeScope.ServiceProvider.GetRequiredService<IAuthDataScopeService>();
            var beforeMoveScope = await scopeService.GetScopeAsync(references.UserId, 1, "spec");
            beforeMoveScope.Should().NotBeNull();
            beforeMoveScope!.OrgUnitIds.Should().NotContain(departmentId);
        }

        var response = await _client.PutAsync(
            $"/api/org-units/{departmentId}/move",
            ApiClientJson.ToJsonContent(new { newParentId = divisionId }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(0);
        body.Data.GetProperty("parentId").GetInt32().Should().Be(divisionId);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var division = await dbContext.OrgUnits.AsNoTracking().SingleAsync(item => item.Id == divisionId);
        var department = await dbContext.OrgUnits.AsNoTracking().SingleAsync(item => item.Id == departmentId);
        var section = await dbContext.OrgUnits.AsNoTracking().SingleAsync(item => item.Id == sectionId);

        department.ParentId.Should().Be(divisionId);
        department.Path.Should().Be($"{division.Path}{departmentId}/");
        department.Depth.Should().Be(division.Depth + 1);
        department.UpdatedAt.Should().NotBeNull();
        section.Path.Should().Be($"{department.Path}{sectionId}/");
        section.Depth.Should().Be(department.Depth + 1);
        section.UpdatedAt.Should().NotBeNull();

        (await dbContext.AuthUserOrgUnits.AsNoTracking()
            .SingleAsync(link => link.Id == references.UserOrgLinkId))
            .OrgUnitId.Should().Be(departmentId);
        (await dbContext.AuthRoleDataScopeNodes.AsNoTracking()
            .SingleAsync(link => link.Id == references.RoleScopeNodeId))
            .OrgUnitId.Should().Be(divisionId);
        (await dbContext.WordFiles.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(file => file.Id == references.WordFileId))
            .OwnerOrgUnitId.Should().Be(sectionId);
        (await dbContext.AcceptanceSpecs.AsNoTracking()
            .SingleAsync(spec => spec.Id == references.AcceptanceSpecId))
            .OwnerOrgUnitId.Should().Be(departmentId);

        using var afterScope = _factory.Services.CreateScope();
        var afterScopeService = afterScope.ServiceProvider.GetRequiredService<IAuthDataScopeService>();
        var afterMoveScope = await afterScopeService.GetScopeAsync(references.UserId, 1, "spec");
        afterMoveScope.Should().NotBeNull();
        afterMoveScope!.OrgUnitIds.Should().Contain([departmentId, sectionId]);
    }

    [Fact]
    public async Task Move_WhenNewParentIsDescendant_ShouldRejectAndLeaveSubtreeUnchanged()
    {
        var divisionId = await SeedChildOrgUnitAsync(OrgUnitType.Division);
        var departmentId = await SeedChildOrgUnitAsync(OrgUnitType.Department, divisionId);
        var before = await GetOrgUnitStateAsync(divisionId, departmentId);

        var response = await _client.PutAsync(
            $"/api/org-units/{divisionId}/move",
            ApiClientJson.ToJsonContent(new { newParentId = departmentId }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("下级");
        (await GetOrgUnitStateAsync(divisionId, departmentId)).Should().Equal(before);
    }

    [Fact]
    public async Task Move_WhenNewParentIsInactiveOrInvalidType_ShouldReject()
    {
        var rootId = await GetRootOrgUnitIdAsync();
        var inactiveDivisionId = await SeedChildOrgUnitAsync(OrgUnitType.Division, rootId, false);
        var departmentId = await SeedChildOrgUnitAsync(OrgUnitType.Department, rootId);
        var sectionId = await SeedChildOrgUnitAsync(OrgUnitType.Section, rootId);

        var inactiveResponse = await _client.PutAsync(
            $"/api/org-units/{departmentId}/move",
            ApiClientJson.ToJsonContent(new { newParentId = inactiveDivisionId }));
        inactiveResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await inactiveResponse.ReadAsAsync<ApiResponse<JsonElement>>()).Message.Should().Contain("停用");

        var invalidTypeResponse = await _client.PutAsync(
            $"/api/org-units/{departmentId}/move",
            ApiClientJson.ToJsonContent(new { newParentId = sectionId }));
        invalidTypeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalidTypeResponse.ReadAsAsync<ApiResponse<JsonElement>>()).Message.Should().Contain("课别");
    }

    [Fact]
    public async Task Move_WhenTargetIsRootCompany_ShouldReject()
    {
        var rootId = await GetRootOrgUnitIdAsync();
        var divisionId = await SeedChildOrgUnitAsync(OrgUnitType.Division, rootId);

        var response = await _client.PutAsync(
            $"/api/org-units/{rootId}/move",
            ApiClientJson.ToJsonContent(new { newParentId = divisionId }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadAsAsync<ApiResponse<JsonElement>>()).Message.Should().Contain("根节点");
    }

    [Fact]
    public async Task Move_WhenNewParentIsCurrentParent_ShouldBeIdempotent()
    {
        var rootId = await GetRootOrgUnitIdAsync();
        var departmentId = await SeedChildOrgUnitAsync(OrgUnitType.Department, rootId);
        var before = (await GetOrgUnitStateAsync(departmentId)).Single();

        var response = await _client.PutAsync(
            $"/api/org-units/{departmentId}/move",
            ApiClientJson.ToJsonContent(new { newParentId = rootId }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = (await GetOrgUnitStateAsync(departmentId)).Single();
        after.Should().Be(before);
    }

    [Fact]
    public async Task Move_WithoutMovePermission_ShouldReturnForbidden()
    {
        var rootId = await GetRootOrgUnitIdAsync();
        var divisionId = await SeedChildOrgUnitAsync(OrgUnitType.Division, rootId);
        var departmentId = await SeedChildOrgUnitAsync(OrgUnitType.Department, rootId);
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/org-units/{departmentId}/move")
        {
            Content = ApiClientJson.ToJsonContent(new { newParentId = divisionId })
        };
        request.Headers.Add("X-Test-Role", "common");
        request.Headers.Add("X-Test-Permissions", "api:org-unit:read");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Move_WhenDescendantPathIsMalformed_ShouldRejectWithoutPartialMove()
    {
        var rootId = await GetRootOrgUnitIdAsync();
        var divisionId = await SeedChildOrgUnitAsync(OrgUnitType.Division, rootId);
        var departmentId = await SeedChildOrgUnitAsync(OrgUnitType.Department, rootId);
        var sectionId = await SeedChildOrgUnitAsync(OrgUnitType.Section, departmentId);
        var before = await GetOrgUnitStateAsync(departmentId, sectionId);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var section = await dbContext.OrgUnits.SingleAsync(item => item.Id == sectionId);
        var originalPath = section.Path;
        section.Path = "/";
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        try
        {
            var response = await _client.PutAsync(
                $"/api/org-units/{departmentId}/move",
                ApiClientJson.ToJsonContent(new { newParentId = divisionId }));

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.ReadAsAsync<ApiResponse<JsonElement>>()).Message.Should().Contain("路径");
            var after = await GetOrgUnitStateAsync(departmentId, sectionId);
            after.Single(item => item.Id == departmentId).Should()
                .Be(before.Single(item => item.Id == departmentId));
        }
        finally
        {
            var savedSection = await dbContext.OrgUnits.SingleAsync(item => item.Id == sectionId);
            savedSection.Path = originalPath;
            await dbContext.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Move_WhenNewParentBelongsToOtherCompany_ShouldReject()
    {
        var rootId = await GetRootOrgUnitIdAsync();
        var departmentId = await SeedChildOrgUnitAsync(OrgUnitType.Department, rootId);
        var before = (await GetOrgUnitStateAsync(departmentId)).Single();
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var otherCompany = new OrgCompany
        {
            Code = $"OTHER-{Guid.NewGuid():N}"[..32].ToUpperInvariant(),
            Name = "其他公司",
            IsActive = true,
            CreatedAt = now
        };
        dbContext.OrgCompanies.Add(otherCompany);
        await dbContext.SaveChangesAsync();
        var otherRoot = new OrgUnit
        {
            CompanyId = otherCompany.Id,
            UnitType = OrgUnitType.Company,
            Code = "ROOT",
            Name = "其他公司",
            Path = "/",
            Depth = 0,
            IsActive = true,
            CreatedAt = now
        };
        dbContext.OrgUnits.Add(otherRoot);
        await dbContext.SaveChangesAsync();
        otherRoot.Path = $"/{otherRoot.Id}/";
        await dbContext.SaveChangesAsync();

        try
        {
            var response = await _client.PutAsync(
                $"/api/org-units/{departmentId}/move",
                ApiClientJson.ToJsonContent(new { newParentId = otherRoot.Id }));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.ReadAsAsync<ApiResponse<JsonElement>>()).Message.Should().Contain("不存在");
            (await GetOrgUnitStateAsync(departmentId)).Single().Should().Be(before);
        }
        finally
        {
            dbContext.OrgUnits.Remove(otherRoot);
            dbContext.OrgCompanies.Remove(otherCompany);
            await dbContext.SaveChangesAsync();
        }
    }

    private async Task<int> GetRootOrgUnitIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.OrgUnits
            .Where(orgUnit => orgUnit.ParentId == null && orgUnit.UnitType == OrgUnitType.Company)
            .Select(orgUnit => orgUnit.Id)
            .FirstAsync();
    }

    private async Task<int> SeedChildOrgUnitAsync(
        OrgUnitType unitType = OrgUnitType.Division,
        int? parentId = null,
        bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actualParentId = parentId ?? await dbContext.OrgUnits
            .Where(orgUnit => orgUnit.ParentId == null && orgUnit.UnitType == OrgUnitType.Company)
            .Select(orgUnit => orgUnit.Id)
            .FirstAsync();
        var parent = await dbContext.OrgUnits.AsNoTracking().SingleAsync(orgUnit => orgUnit.Id == actualParentId);

        var child = new OrgUnit
        {
            CompanyId = parent.CompanyId,
            ParentId = actualParentId,
            UnitType = unitType,
            Code = $"{unitType.ToString()[..3].ToUpperInvariant()}-{Guid.NewGuid():N}"[..18],
            Name = $"历史{unitType}",
            Path = "/",
            Depth = parent.Depth + 1,
            Sort = 0,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.OrgUnits.Add(child);
        await dbContext.SaveChangesAsync();
        child.Path = $"{parent.Path}{child.Id}/";
        await dbContext.SaveChangesAsync();
        return child.Id;
    }

    private async Task<List<OrgUnitState>> GetOrgUnitStateAsync(params int[] ids)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.OrgUnits.AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .OrderBy(item => item.Id)
            .Select(item => new OrgUnitState(
                item.Id,
                item.ParentId,
                item.Path,
                item.Depth,
                item.UpdatedAt))
            .ToListAsync();
    }

    private async Task<StableReferenceIds> SeedStableReferencesAsync(
        int departmentId,
        int sectionId,
        int roleScopeOrgUnitId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var department = await dbContext.OrgUnits.AsNoTracking().SingleAsync(item => item.Id == departmentId);
        var now = DateTime.UtcNow;
        var user = new SystemUser
        {
            CompanyId = department.CompanyId,
            Username = $"move_{Guid.NewGuid():N}"[..28],
            PasswordHash = "unused",
            Nickname = "组织移动引用用户",
            IsActive = true,
            PermissionVersion = 1,
            CreatedAt = now
        };
        var role = new AuthRole
        {
            CompanyId = department.CompanyId,
            Code = $"MOVE_{Guid.NewGuid():N}"[..28].ToUpperInvariant(),
            Name = "组织移动引用角色",
            Description = "集成测试",
            IsActive = true,
            CreatedAt = now
        };
        var customer = new Customer
        {
            Name = $"组织移动客户-{Guid.NewGuid():N}",
            CreatedAt = now
        };
        dbContext.AddRange(user, role, customer);
        await dbContext.SaveChangesAsync();

        var userOrgLink = new AuthUserOrgUnit
        {
            UserId = user.Id,
            OrgUnitId = departmentId,
            IsPrimary = true,
            CreatedAt = now
        };
        var userRoleLink = new AuthUserRole
        {
            UserId = user.Id,
            RoleId = role.Id,
            CreatedAt = now
        };
        var roleScope = new AuthRoleDataScope
        {
            RoleId = role.Id,
            Resource = "spec",
            ScopeType = DataScopeType.OrgSubtree,
            CreatedAt = now
        };
        roleScope.Nodes.Add(new AuthRoleDataScopeNode { OrgUnitId = roleScopeOrgUnitId });
        var wordFile = new WordFile
        {
            CompanyId = department.CompanyId,
            CreatedByUserId = user.Id,
            OwnerOrgUnitId = sectionId,
            FileName = "org-move-reference.docx",
            FilePath = "org-move-reference.docx",
            FileHash = Guid.NewGuid().ToString("N"),
            FileType = UploadedFileType.WordDocx,
            FileContent = [1],
            UploadedAt = now
        };
        dbContext.AddRange(userOrgLink, userRoleLink, roleScope, wordFile);
        await dbContext.SaveChangesAsync();

        var acceptanceSpec = new AcceptanceSpec
        {
            CustomerId = customer.Id,
            Project = "组织移动引用项目",
            Specification = "引用组织ID必须保持不变",
            WordFileId = wordFile.Id,
            CreatedByUserId = user.Id,
            OwnerOrgUnitId = departmentId,
            ImportedAt = now
        };
        dbContext.AcceptanceSpecs.Add(acceptanceSpec);
        await dbContext.SaveChangesAsync();

        return new StableReferenceIds(
            user.Id,
            userOrgLink.Id,
            roleScope.Nodes.Single().Id,
            wordFile.Id,
            acceptanceSpec.Id);
    }

    private sealed record OrgUnitState(
        int Id,
        int? ParentId,
        string Path,
        int Depth,
        DateTime? UpdatedAt);

    private sealed record StableReferenceIds(
        int UserId,
        int UserOrgLinkId,
        int RoleScopeNodeId,
        int WordFileId,
        int AcceptanceSpecId);
}
