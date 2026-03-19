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
        body.Message.Should().Contain("公司根节点已存在");
    }

    [Fact]
    public async Task Create_WhenParentIsSection_ShouldReject()
    {
        var rootOrgUnitId = await GetRootOrgUnitIdAsync();

        var createSectionResponse = await _client.PostAsync(
            "/api/org-units",
            ApiClientJson.ToJsonContent(new
            {
                parentId = rootOrgUnitId,
                unitType = 3,
                code = $"SEC-{Guid.NewGuid():N}"[..18],
                name = "测试课别",
                sort = 0,
                isActive = true
            }));
        createSectionResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var section = await createSectionResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var sectionId = section.Data!.GetProperty("id").GetInt32();

        var response = await _client.PostAsync(
            "/api/org-units",
            ApiClientJson.ToJsonContent(new
            {
                parentId = sectionId,
                unitType = 2,
                code = $"DEP-{Guid.NewGuid():N}"[..18],
                name = "非法部门",
                sort = 0,
                isActive = true
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("课别节点不允许新增下级组织");
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

    private async Task<int> GetRootOrgUnitIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.OrgUnits
            .Where(orgUnit => orgUnit.ParentId == null && orgUnit.UnitType == OrgUnitType.Company)
            .Select(orgUnit => orgUnit.Id)
            .FirstAsync();
    }
}
