using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ProcessEntity = AcceptanceSpecSystem.Data.Entities.Process;

namespace AcceptanceSpecSystem.Api.Tests;

public class MasterDataPaginationContractTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public MasterDataPaginationContractTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task MasterDataEndpoints_ShouldExposeAll251ItemsAcrossTwoPages()
    {
        var prefix = $"pagination-{Guid.NewGuid():N}";
        await SeedMasterDataAsync(prefix);

        await AssertTwoPageContractAsync("/api/customers", prefix);
        await AssertTwoPageContractAsync("/api/processes", prefix);
        await AssertTwoPageContractAsync("/api/machine-models", prefix);
    }

    private async Task SeedMasterDataAsync(string prefix)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        dbContext.Customers.AddRange(Enumerable.Range(1, 251).Select(index => new Customer
        {
            Name = $"{prefix}-customer-{index:D3}",
            CreatedAt = now.AddTicks(index)
        }));
        dbContext.Processes.AddRange(Enumerable.Range(1, 251).Select(index => new ProcessEntity
        {
            Name = $"{prefix}-process-{index:D3}",
            CreatedAt = now.AddTicks(index)
        }));
        dbContext.MachineModels.AddRange(Enumerable.Range(1, 251).Select(index => new MachineModel
        {
            Name = $"{prefix}-machine-{index:D3}",
            CreatedAt = now.AddTicks(index)
        }));

        await dbContext.SaveChangesAsync();
    }

    private async Task AssertTwoPageContractAsync(string endpoint, string keyword)
    {
        var escapedKeyword = Uri.EscapeDataString(keyword);
        var first = await GetPageAsync($"{endpoint}?page=1&pageSize=200&keyword={escapedKeyword}");
        var second = await GetPageAsync($"{endpoint}?page=2&pageSize=200&keyword={escapedKeyword}");

        first.Total.Should().Be(251);
        first.TotalPages.Should().Be(2);
        first.Page.Should().Be(1);
        first.PageSize.Should().Be(200);
        first.Items.Should().HaveCount(200);

        second.Total.Should().Be(251);
        second.TotalPages.Should().Be(2);
        second.Page.Should().Be(2);
        second.PageSize.Should().Be(200);
        second.Items.Should().HaveCount(51);

        first.Items
            .Concat(second.Items)
            .Select(item => item.GetProperty("id").GetInt32())
            .Should().OnlyHaveUniqueItems().And.HaveCount(251);
    }

    private async Task<PaginationPage<JsonElement>> GetPageAsync(string requestUri)
    {
        using var response = await _client.GetAsync(requestUri);
        response.EnsureSuccessStatusCode();

        var payload = await response.ReadAsAsync<ApiResponse<PaginationPage<JsonElement>>>();
        payload.Code.Should().Be(0);
        payload.Data.Should().NotBeNull();
        return payload.Data!;
    }

    private sealed class PaginationPage<T>
    {
        public List<T> Items { get; set; } = [];
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }
}
