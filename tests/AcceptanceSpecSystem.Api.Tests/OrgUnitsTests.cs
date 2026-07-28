using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
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
        int? parentId = null)
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
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.OrgUnits.Add(child);
        await dbContext.SaveChangesAsync();
        child.Path = $"{parent.Path}{child.Id}/";
        await dbContext.SaveChangesAsync();
        return child.Id;
    }
}
