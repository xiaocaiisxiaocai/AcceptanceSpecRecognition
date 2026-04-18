using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class ColumnMappingRuleRecoveryTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private const string BaseUrl = "/api/column-mapping-rules";

    public ColumnMappingRuleRecoveryTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ColumnMappingRuleApis_ShouldExposeCrudAndEffectiveEndpoints()
    {
        (await _client.GetAsync(BaseUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync($"{BaseUrl}/effective")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_WithContainsMode_ShouldSucceed()
    {
        var resp = await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = 1,
            matchMode = 1,
            pattern = "项目",
            priority = 10,
            enabled = true
        }));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        json.Data.GetProperty("id").GetInt32().Should().BeGreaterThan(0);
        json.Data.GetProperty("pattern").GetString().Should().Be("项目");
    }

    [Fact]
    public void ColumnMappingRuleDefaultMatchMode_ShouldBeEquals()
    {
        new CreateColumnMappingRuleRequest().MatchMode.Should().Be(ColumnMappingMatchMode.Equals);
        new UpdateColumnMappingRuleRequest().MatchMode.Should().Be(ColumnMappingMatchMode.Equals);
        new ColumnMappingRule().MatchMode.Should().Be(ColumnMappingMatchMode.Equals);
    }

    [Fact]
    public async Task Create_WithInvalidRegex_ShouldFail()
    {
        var resp = await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = 2,
            matchMode = 3,
            pattern = "[invalid",
            priority = 0,
            enabled = true
        }));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await resp.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(400);
        json.Message.Should().Contain("正则表达式无效");
    }

    [Fact]
    public async Task GetEffective_ShouldExcludeDisabledRules_AndSortByPriority()
    {
        await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = 1,
            matchMode = 1,
            pattern = "低优先级",
            priority = 1,
            enabled = true
        }));

        await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = 1,
            matchMode = 1,
            pattern = "高优先级",
            priority = 100,
            enabled = true
        }));

        await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = 1,
            matchMode = 1,
            pattern = "禁用规则",
            priority = 50,
            enabled = false
        }));

        var resp = await _client.GetAsync($"{BaseUrl}/effective");
        var json = await resp.ReadAsAsync<ApiResponse<JsonElement>>();

        json.Code.Should().Be(0);
        var items = json.Data.EnumerateArray().ToList();
        items.Select(item => item.GetProperty("pattern").GetString()).Should().NotContain("禁用规则");

        var enabledProjectRules = items
            .Where(item => item.GetProperty("targetField").GetInt32() == 1)
            .Select(item => item.GetProperty("pattern").GetString())
            .ToList();

        enabledProjectRules.IndexOf("高优先级").Should().BeLessThan(enabledProjectRules.IndexOf("低优先级"));
    }

    [Fact]
    public void ColumnMappingRuleBackendSourceFiles_ShouldExist()
    {
        var repositoryRoot = GetRepositoryRoot();

        File.Exists(Path.Combine(repositoryRoot, "src", "AcceptanceSpecSystem.Api", "Controllers", "ColumnMappingRulesController.cs"))
            .Should().BeTrue();
        File.Exists(Path.Combine(repositoryRoot, "src", "AcceptanceSpecSystem.Api", "DTOs", "ColumnMappingRuleDtos.cs"))
            .Should().BeTrue();
        File.Exists(Path.Combine(repositoryRoot, "src", "AcceptanceSpecSystem.Data", "Entities", "ColumnMappingRule.cs"))
            .Should().BeTrue();
        File.Exists(Path.Combine(repositoryRoot, "src", "AcceptanceSpecSystem.Data", "Repositories", "IColumnMappingRuleRepository.cs"))
            .Should().BeTrue();
        File.Exists(Path.Combine(repositoryRoot, "src", "AcceptanceSpecSystem.Data", "Repositories", "ColumnMappingRuleRepository.cs"))
            .Should().BeTrue();
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src")) &&
                Directory.Exists(Path.Combine(current.FullName, "tests")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("未找到仓库根目录");
    }
}
