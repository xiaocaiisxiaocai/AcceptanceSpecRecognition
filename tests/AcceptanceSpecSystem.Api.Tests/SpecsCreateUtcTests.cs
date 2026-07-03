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
}
