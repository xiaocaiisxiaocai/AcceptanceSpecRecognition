using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

/// <summary>
/// 守护集成测试的业务归属辅助：默认种子必须存在业务部门，
/// 且接口取值与直接写库造数取值一致，否则造出的规格会被部门隔离过滤，测试会出现难以定位的 400 或查重失效。
/// </summary>
public sealed class TestBusinessOrgTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TestBusinessOrgTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task DefaultBusinessOrg_ShouldBeSeededActiveDepartment_AndMatchDatabaseHelper()
    {
        var apiOrgUnitId = await TestBusinessOrg.GetDefaultBusinessOrgUnitIdAsync(_client);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbOrgUnitId = await TestBusinessOrg.GetDefaultBusinessOrgUnitIdAsync(dbContext);
        var orgUnit = await dbContext.OrgUnits.AsNoTracking().SingleAsync(org => org.Id == apiOrgUnitId);

        dbOrgUnitId.Should().Be(apiOrgUnitId);
        orgUnit.IsActive.Should().BeTrue();
        orgUnit.UnitType.Should().NotBe(OrgUnitType.Company);
        orgUnit.Code.Should().Be(AuthUserSeedAppService.DefaultOperationalDepartmentCode);
    }

    [Fact]
    public async Task AdminUpload_WithDefaultBusinessOrgHelper_ShouldPersistFileToThatDepartment()
    {
        var orgUnitId = await TestBusinessOrg.GetDefaultBusinessOrgUnitIdAsync(_client);
        var fileId = await BatchPreviewTestHelper.UploadSourceDocumentAsync(_client, ("辅助项目", "辅助规格"));

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ownerOrgUnitId = await dbContext.WordFiles
            .AsNoTracking()
            .Where(file => file.Id == fileId)
            .Select(file => file.OwnerOrgUnitId)
            .SingleAsync();

        ownerOrgUnitId.Should().Be(orgUnitId);
    }

    [Fact]
    public async Task AdminCreateSpec_WithDefaultBusinessOrgHelper_ShouldPersistSpecToThatDepartment()
    {
        var orgUnitId = await TestBusinessOrg.GetDefaultBusinessOrgUnitIdAsync(_client);
        var customerId = await CreateReferenceDataAsync("customers", $"辅助客户-{Guid.NewGuid():N}");
        var processId = await CreateReferenceDataAsync("processes", $"辅助制程-{Guid.NewGuid():N}");

        using var response = await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                businessOrgUnitId = orgUnitId,
                customerId,
                processId,
                project = "辅助项目",
                specification = "辅助规格",
                acceptance = "OK",
                remark = "R"
            }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var specId = body.Data.GetProperty("id").GetInt32();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ownerOrgUnitId = await dbContext.AcceptanceSpecs
            .AsNoTracking()
            .Where(spec => spec.Id == specId)
            .Select(spec => spec.OwnerOrgUnitId)
            .SingleAsync();

        ownerOrgUnitId.Should().Be(orgUnitId);
    }

    private async Task<int> CreateReferenceDataAsync(string resource, string name)
    {
        using var response = await _client.PostAsync(
            $"/api/{resource}",
            ApiClientJson.ToJsonContent(new { name }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return body.Data.GetProperty("id").GetInt32();
    }
}
