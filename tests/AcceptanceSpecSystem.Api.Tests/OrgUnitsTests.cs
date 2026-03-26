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
    public async Task Create_WhenRootCompanyAlreadyExists_ShouldReject()
    {
        var response = await _client.PostAsync(
            "/api/org-units",
            ApiClientJson.ToJsonContent(new
            {
                parentId = (int?)null,
                unitType = 0,
                code = $"ROOT-{Guid.NewGuid():N}"[..18],
                name = "第二公司根",
                sort = 0,
                isActive = true
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("单组织");
    }

    [Fact]
    public async Task Create_WhenParentIsSection_ShouldReject()
    {
        var response = await _client.PostAsync(
            "/api/org-units",
            ApiClientJson.ToJsonContent(new
            {
                parentId = await GetRootOrgUnitIdAsync(),
                unitType = 2,
                code = $"DEP-{Guid.NewGuid():N}"[..18],
                name = "非法新增组织",
                sort = 0,
                isActive = true
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("单组织");
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
    public async Task GetTree_WhenDatabaseContainsChildOrgUnits_ShouldOnlyReturnRootNode()
    {
        await SeedChildOrgUnitAsync();

        var response = await _client.GetAsync("/api/org-units/tree");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(0);
        body.Data.ValueKind.Should().Be(JsonValueKind.Array);
        body.Data.GetArrayLength().Should().Be(1);

        var root = body.Data[0];
        root.GetProperty("unitType").GetInt32().Should().Be((int)OrgUnitType.Company);
        root.GetProperty("children").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Update_WhenTargetIsNotRootCompany_ShouldReject()
    {
        var childId = await SeedChildOrgUnitAsync();

        var response = await _client.PutAsync(
            $"/api/org-units/{childId}",
            ApiClientJson.ToJsonContent(new
            {
                code = "DIV-TEST",
                name = "非法更新节点",
                sort = 0,
                isActive = true
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("根组织");
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

    private async Task<int> SeedChildOrgUnitAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rootOrgUnitId = await dbContext.OrgUnits
            .Where(orgUnit => orgUnit.ParentId == null && orgUnit.UnitType == OrgUnitType.Company)
            .Select(orgUnit => orgUnit.Id)
            .FirstAsync();

        var child = new OrgUnit
        {
            CompanyId = 1,
            ParentId = rootOrgUnitId,
            UnitType = OrgUnitType.Division,
            Code = $"DIV-{Guid.NewGuid():N}"[..18],
            Name = "历史事业部",
            Path = "/",
            Depth = 1,
            Sort = 0,
            IsActive = true,
            CreatedAt = DateTime.Now
        };

        dbContext.OrgUnits.Add(child);
        await dbContext.SaveChangesAsync();
        child.Path = $"/{rootOrgUnitId}/{child.Id}/";
        await dbContext.SaveChangesAsync();
        return child.Id;
    }
}
