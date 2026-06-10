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
        var manualFile = await dbContext.WordFiles.SingleAsync(item => item.FileName == "__MANUAL_ENTRY__");
        manualFile.Id.Should().BeGreaterThan(0);
    }
}
