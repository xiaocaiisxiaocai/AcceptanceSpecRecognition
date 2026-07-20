using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class ColumnMappingRuleRecoveryTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly ApiWebApplicationFactory _factory;
    private const string BaseUrl = "/api/column-mapping-rules";

    public ColumnMappingRuleRecoveryTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ColumnMappingRuleApis_ShouldExposeCrudAndEffectiveEndpoints()
    {
        (await _client.GetAsync(BaseUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync($"{BaseUrl}/effective")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Startup_ShouldSeedBuiltinColumnMappingDefaults()
    {
        var resp = await _client.GetAsync(BaseUrl);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);

        var items = json.Data.EnumerateArray().ToList();
        items.Should().Contain(item =>
            item.GetProperty("source").GetInt32() == (int)ColumnMappingRuleSource.Builtin &&
            item.GetProperty("customerId").ValueKind == JsonValueKind.Null &&
            item.GetProperty("targetField").GetInt32() == (int)ColumnMappingTargetField.Project &&
            item.GetProperty("matchMode").GetInt32() == (int)ColumnMappingMatchMode.Contains &&
            item.GetProperty("pattern").GetString() == "项目");
        items.Should().Contain(item =>
            item.GetProperty("source").GetInt32() == (int)ColumnMappingRuleSource.Builtin &&
            item.GetProperty("customerId").ValueKind == JsonValueKind.Null &&
            item.GetProperty("targetField").GetInt32() == (int)ColumnMappingTargetField.Specification &&
            item.GetProperty("pattern").GetString() == "规格");
    }

    [Fact]
    public async Task RestoreDefaults_ShouldReplenishBuiltinRulesWithoutTouchingManualRules()
    {
        var manualResp = await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = ColumnMappingTargetField.Project,
            matchMode = ColumnMappingMatchMode.Contains,
            pattern = "人工项目词",
            priority = 77,
            enabled = false,
            source = ColumnMappingRuleSource.Manual
        }));
        manualResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var restoreResp = await _client.PostAsync($"{BaseUrl}/restore-defaults?targetField=Project", null);
        restoreResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var restoreJson = await restoreResp.ReadAsAsync<ApiResponse<JsonElement>>();
        restoreJson.Code.Should().Be(0);

        var listResp = await _client.GetAsync(BaseUrl);
        var listJson = await listResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var items = listJson.Data.EnumerateArray().ToList();

        items.Should().Contain(item =>
            item.GetProperty("source").GetInt32() == (int)ColumnMappingRuleSource.Builtin &&
            item.GetProperty("targetField").GetInt32() == (int)ColumnMappingTargetField.Project &&
            item.GetProperty("pattern").GetString() == "项目");
        items.Should().Contain(item =>
            item.GetProperty("source").GetInt32() == (int)ColumnMappingRuleSource.Manual &&
            item.GetProperty("pattern").GetString() == "人工项目词" &&
            item.GetProperty("enabled").GetBoolean() == false &&
            item.GetProperty("priority").GetInt32() == 77);
    }

    [Fact]
    public async Task RestoreDefaults_WhenBuiltinFieldAlreadyExistsDisabled_ShouldReplenishMissingDefaultWords()
    {
        var initialListResp = await _client.GetAsync(BaseUrl);
        var initialListJson = await initialListResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var existingRemarkBuiltinIds = initialListJson.Data.EnumerateArray()
            .Where(item =>
                item.GetProperty("source").GetInt32() == (int)ColumnMappingRuleSource.Builtin &&
                item.GetProperty("targetField").GetInt32() == (int)ColumnMappingTargetField.Remark)
            .Select(item => item.GetProperty("id").GetInt32())
            .ToList();

        foreach (var id in existingRemarkBuiltinIds)
        {
            (await _client.DeleteAsync($"{BaseUrl}/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var builtinResp = await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = ColumnMappingTargetField.Remark,
            matchMode = ColumnMappingMatchMode.Contains,
            pattern = "禁用内置备注词",
            priority = 0,
            enabled = false,
            source = ColumnMappingRuleSource.Builtin
        }));
        builtinResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var restoreResp = await _client.PostAsync($"{BaseUrl}/restore-defaults?targetField=Remark", null);
        restoreResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var listResp = await _client.GetAsync(BaseUrl);
        var listJson = await listResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var remarkBuiltinPatterns = listJson.Data.EnumerateArray()
            .Where(item =>
                item.GetProperty("source").GetInt32() == (int)ColumnMappingRuleSource.Builtin &&
                item.GetProperty("targetField").GetInt32() == (int)ColumnMappingTargetField.Remark)
            .Select(item => item.GetProperty("pattern").GetString())
            .ToList();

        remarkBuiltinPatterns.Should().Contain("禁用内置备注词");
        remarkBuiltinPatterns.Should().Contain("备注");

        var disabledId = listJson.Data.EnumerateArray()
            .Single(item => item.GetProperty("pattern").GetString() == "禁用内置备注词")
            .GetProperty("id")
            .GetInt32();
        (await _client.DeleteAsync($"{BaseUrl}/{disabledId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var secondRestoreResp = await _client.PostAsync($"{BaseUrl}/restore-defaults?targetField=Remark", null);
        secondRestoreResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondRestoreJson = await secondRestoreResp.ReadAsAsync<ApiResponse<JsonElement>>();
        secondRestoreJson.Data.GetProperty("added").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Create_WithContainsMode_ShouldSucceed()
    {
        var resp = await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = 1,
            matchMode = 1,
            pattern = "接口新增项目词",
            priority = 10,
            enabled = true
        }));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        json.Data.GetProperty("id").GetInt32().Should().BeGreaterThan(0);
        json.Data.GetProperty("pattern").GetString().Should().Be("接口新增项目词");
    }

    [Fact]
    public async Task Create_WhenSameTargetPatternAndScopeExists_ShouldReturnBadRequest()
    {
        var resp = await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = ColumnMappingTargetField.Project,
            matchMode = ColumnMappingMatchMode.Contains,
            pattern = "项目",
            priority = 10,
            enabled = true,
            source = ColumnMappingRuleSource.Manual
        }));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await resp.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(400);
        json.Message.Should().Contain("已存在");
    }

    [Fact]
    public async Task Update_WhenGlobalPatternBelongsToAnotherTarget_ShouldReturnHttp409()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var protectedPattern = $"全局字段冲突-{suffix}";
        var first = await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = ColumnMappingTargetField.Project,
            matchMode = ColumnMappingMatchMode.Equals,
            pattern = protectedPattern,
            priority = 10,
            enabled = true,
            source = ColumnMappingRuleSource.Manual
        }));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = ColumnMappingTargetField.Specification,
            matchMode = ColumnMappingMatchMode.Equals,
            pattern = $"更新前-{suffix}",
            priority = 10,
            enabled = true,
            source = ColumnMappingRuleSource.Manual
        }));
        var secondBody = await second.ReadAsAsync<ApiResponse<JsonElement>>();
        var secondId = secondBody.Data.GetProperty("id").GetInt32();

        var response = await _client.PutAsync($"{BaseUrl}/{secondId}", ApiClientJson.ToJsonContent(new
        {
            targetField = ColumnMappingTargetField.Specification,
            matchMode = ColumnMappingMatchMode.Equals,
            pattern = protectedPattern,
            priority = 10,
            enabled = true,
            source = ColumnMappingRuleSource.Manual
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(409);
    }

    [Fact]
    public async Task Database_WhenNormalizedGlobalRuleAlreadyExists_ShouldRejectDuplicate()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ColumnMappingRules.Add(new ColumnMappingRule
        {
            TargetField = ColumnMappingTargetField.Project,
            MatchMode = ColumnMappingMatchMode.Contains,
            Pattern = "持久化唯一词",
            Source = ColumnMappingRuleSource.Builtin,
            CustomerId = null
        });
        await db.SaveChangesAsync();

        db.ColumnMappingRules.Add(new ColumnMappingRule
        {
            TargetField = ColumnMappingTargetField.Project,
            MatchMode = ColumnMappingMatchMode.Equals,
            Pattern = "  持久化唯一词  ",
            Source = ColumnMappingRuleSource.Manual,
            CustomerId = null
        });

        var saveDuplicate = async () => await db.SaveChangesAsync();
        await saveDuplicate.Should().ThrowAsync<DbUpdateException>();
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
    public async Task Create_WithTooLongPattern_ShouldReturnBadRequest()
    {
        var resp = await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = 1,
            matchMode = 1,
            pattern = new string('项', 201),
            priority = 0,
            enabled = true
        }));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
        File.Exists(Path.Combine(repositoryRoot, "src", "AcceptanceSpecSystem.Application", "Contracts", "ConfigurationDtos.cs"))
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
