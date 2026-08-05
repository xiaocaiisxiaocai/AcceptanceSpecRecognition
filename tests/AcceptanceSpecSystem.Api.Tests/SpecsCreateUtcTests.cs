using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class SpecsCreateUtcTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SpecsCreateUtcTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateSpec_ShouldReturnUtcImportedAt_AndPersistManualWordFileAsUtc()
    {
        var customerResponse = await _client.PostAsync(
            "/api/customers",
            ApiClientJson.ToJsonContent(new { name = "UTC-客户" }));
        customerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var customer = await customerResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var customerId = customer.Data.GetProperty("id").GetInt32();

        var processResponse = await _client.PostAsync(
            "/api/processes",
            ApiClientJson.ToJsonContent(new { name = "UTC-制程" }));
        processResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var process = await processResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var processId = process.Data.GetProperty("id").GetInt32();

        var specResponse = await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project = "UTC 项目",
                specification = "UTC 规格",
                acceptance = "UTC 验收",
                remark = "UTC 备注"
            }));

        specResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var spec = await specResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        spec.Data.GetProperty("importedAt").GetString().Should().EndWith("Z");

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manualFile = await dbContext.WordFiles
            .SingleAsync(item =>
                item.FileName == "__MANUAL_ENTRY__" &&
                item.CompanyId == 1 &&
                item.CreatedByUserId == 1);
        manualFile.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task UpdateSpec_ShouldPreserveImportedAt_AndReturnUpdatedAt()
    {
        var customerId = await CreateCustomerAsync(_client, "更新时间-客户");
        var processId = await CreateProcessAsync(_client, "更新时间-制程");
        var businessOrgUnitId = await GetBusinessOrgUnitIdAsync();

        var createResponse = await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                businessOrgUnitId,
                customerId,
                processId,
                project = "首次导入项目",
                specification = "首次导入规格",
                acceptance = "首次导入验收",
                remark = "首次导入备注"
            }));
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var specId = created.Data.GetProperty("id").GetInt32();
        var importedAt = created.Data.GetProperty("importedAt").GetDateTime();
        created.Data.GetProperty("updatedAt").ValueKind.Should().Be(JsonValueKind.Null);

        var updateStartedAt = DateTime.UtcNow;
        var updateResponse = await _client.PutAsync(
            $"/api/specs/{specId}",
            ApiClientJson.ToJsonContent(new
            {
                project = "更新后项目",
                specification = "更新后规格",
                acceptance = "更新后验收",
                remark = "更新后备注"
            }));
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.ReadAsAsync<ApiResponse<JsonElement>>();

        updated.Data.GetProperty("importedAt").GetDateTime().Should().Be(importedAt);
        updated.Data.GetProperty("updatedAt").GetDateTime().Should().BeOnOrAfter(updateStartedAt);

        var listResponse = await _client.GetAsync(
            $"/api/specs?page=1&pageSize=10&customerId={customerId}&processId={processId}");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listResponse.ReadAsAsync<ApiResponse<PagedData<JsonElement>>>();
        var listedSpec = list.Data!.Items.Single(item => item.GetProperty("id").GetInt32() == specId);
        listedSpec.GetProperty("importedAt").GetString().Should().EndWith("Z");
        listedSpec.GetProperty("updatedAt").GetString().Should().EndWith("Z");
        listedSpec.GetProperty("importedAt").GetDateTime().Should().Be(importedAt);
        listedSpec.GetProperty("updatedAt").GetDateTime().Should().BeOnOrAfter(updateStartedAt);
    }

    [Fact]
    public async Task CreateSpec_WhenDifferentUsersCreateManualSpecs_ShouldUseUserScopedManualWordFile()
    {
        var adminCustomerId = await CreateCustomerAsync(_client, "手工文件-管理员客户");
        var adminProcessId = await CreateProcessAsync(_client, "手工文件-管理员制程");
        await CreateSpecAsync(_client, adminCustomerId, adminProcessId, "管理员项目");

        using var commonRequest = new HttpRequestMessage(HttpMethod.Post, "/api/specs")
        {
            Content = ApiClientJson.ToJsonContent(new
            {
                customerId = adminCustomerId,
                processId = adminProcessId,
                project = "普通用户项目",
                specification = "普通用户规格",
                acceptance = "普通用户验收",
                remark = "普通用户备注"
            })
        };
        commonRequest.Headers.Add("X-Test-Role", "common");
        commonRequest.Headers.Add("X-Test-Permissions", "*:*:*");

        var commonResponse = await _client.SendAsync(commonRequest);
        commonResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var manualFiles = await dbContext.WordFiles
            .Where(file => file.FileName == "__MANUAL_ENTRY__")
            .OrderBy(file => file.CreatedByUserId)
            .ToListAsync();

        manualFiles.Should().HaveCount(2, "手工规格占位文件必须按公司、用户和组织隔离，不能跨用户复用");
        manualFiles.Select(file => file.CreatedByUserId).Should().BeEquivalentTo(new int?[] { 1, 2 });
    }

    private static async Task<int> CreateCustomerAsync(HttpClient client, string name)
    {
        var response = await client.PostAsync(
            "/api/customers",
            ApiClientJson.ToJsonContent(new { name }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var customer = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return customer.Data.GetProperty("id").GetInt32();
    }

    private static async Task<int> CreateProcessAsync(HttpClient client, string name)
    {
        var response = await client.PostAsync(
            "/api/processes",
            ApiClientJson.ToJsonContent(new { name }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var process = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return process.Data.GetProperty("id").GetInt32();
    }

    private static async Task CreateSpecAsync(HttpClient client, int customerId, int processId, string project)
    {
        var response = await client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project,
                specification = $"{project}规格",
                acceptance = $"{project}验收",
                remark = $"{project}备注"
            }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<int> GetBusinessOrgUnitIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.OrgUnits
            .Where(orgUnit =>
                orgUnit.ParentId != null &&
                !dbContext.OrgUnits.Any(child => child.ParentId == orgUnit.Id))
            .Select(orgUnit => orgUnit.Id)
            .FirstAsync();
    }
}
