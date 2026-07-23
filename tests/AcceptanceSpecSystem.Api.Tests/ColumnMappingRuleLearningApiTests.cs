using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class ColumnMappingRuleLearningApiTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private const string BaseUrl = "/api/column-mapping-rules";

    public ColumnMappingRuleLearningApiTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Create_ShouldRoundTripSourceAndCustomerId()
    {
        var response = await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = 2,
            matchMode = 2,
            pattern = "客户规格词",
            priority = 30,
            enabled = true,
            source = 3,
            customerId = 3
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();

        json.Code.Should().Be(0);
        json.Data.GetProperty("pattern").GetString().Should().Be("客户规格词");
        json.Data.GetProperty("source").GetInt32().Should().Be(3);
        json.Data.GetProperty("customerId").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task GetEffective_WithCustomerId_ShouldIncludeGlobalAndCustomerRulesOnly_AndPreferCustomerRules()
    {
        await CreateRuleAsync("全局项目词", customerId: null, priority: 100, source: 2);
        await CreateRuleAsync("当前客户项目词", customerId: 3, priority: 1, source: 3);
        await CreateRuleAsync("其他客户项目词", customerId: 4, priority: 1000, source: 3);

        var response = await _client.GetAsync($"{BaseUrl}/effective?customerId=3");
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.Code.Should().Be(0);

        var items = json.Data.EnumerateArray()
            .Where(item => item.GetProperty("targetField").GetInt32() == 1)
            .Select(item => new
            {
                Pattern = item.GetProperty("pattern").GetString(),
                Source = item.GetProperty("source").GetInt32(),
                CustomerId = item.TryGetProperty("customerId", out var customerId) && customerId.ValueKind != JsonValueKind.Null
                    ? customerId.GetInt32()
                    : (int?)null
            })
            .ToList();

        items.Select(item => item.Pattern).Should().Contain(new[] { "当前客户项目词", "全局项目词" });
        items.Select(item => item.Pattern).Should().NotContain("其他客户项目词");
        items.FindIndex(item => item.Pattern == "当前客户项目词")
            .Should().BeLessThan(items.FindIndex(item => item.Pattern == "全局项目词"));
        items.Single(item => item.Pattern == "当前客户项目词").Should().BeEquivalentTo(new
        {
            Pattern = "当前客户项目词",
            Source = 3,
            CustomerId = 3
        });
    }

    private async Task CreateRuleAsync(string pattern, int? customerId, int priority, int source)
    {
        var response = await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = 1,
            matchMode = 2,
            pattern,
            priority,
            enabled = true,
            source,
            customerId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
