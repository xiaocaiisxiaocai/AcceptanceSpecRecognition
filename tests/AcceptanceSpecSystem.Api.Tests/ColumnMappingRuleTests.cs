using System.Net;
using System.Text.Json;
using FluentAssertions;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;

namespace AcceptanceSpecSystem.Api.Tests;

/// <summary>
/// 列映射规则 CRUD + 筛选 + 排序 集成测试
/// </summary>
public class ColumnMappingRuleTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private const string BaseUrl = "/api/column-mapping-rules";

    public ColumnMappingRuleTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Create_WithContainsMode_ShouldSucceed()
    {
        var resp = await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = 1, // Project
            matchMode = 1,   // Contains
            pattern = "项目",
            priority = 10,
            enabled = true
        }));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);
        json.Data.GetProperty("id").GetInt32().Should().BeGreaterThan(0);
        json.Data.GetProperty("pattern").GetString().Should().Be("项目");
        json.Data.GetProperty("matchMode").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Create_WithEmptyPattern_ShouldFail()
    {
        // 发送空格模式（被控制器 Trim 后拒绝，或被 [Required] 模型验证拦截）
        var resp = await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = 1,
            matchMode = 1,
            pattern = "   ",
            priority = 0,
            enabled = true
        }));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // 响应可能是 ApiResponse 或 ProblemDetails（模型验证），用原始文本断言
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("匹配词不能为空");
    }

    [Fact]
    public async Task Create_WithInvalidRegex_ShouldFail()
    {
        var resp = await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = 2,  // Specification
            matchMode = 3,    // Regex
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
    public async Task Update_ShouldChangeFields()
    {
        // 先创建
        var createResp = await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = 1,
            matchMode = 1,
            pattern = "原始",
            priority = 5,
            enabled = true
        }));
        var createJson = await createResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var id = createJson.Data.GetProperty("id").GetInt32();

        // 更新
        var updateResp = await _client.PutAsync($"{BaseUrl}/{id}", ApiClientJson.ToJsonContent(new
        {
            targetField = 3,  // Acceptance
            matchMode = 2,    // Equals
            pattern = "更新后",
            priority = 20,
            enabled = false
        }));

        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updateJson = await updateResp.ReadAsAsync<ApiResponse<JsonElement>>();
        updateJson.Code.Should().Be(0);
        updateJson.Data.GetProperty("pattern").GetString().Should().Be("更新后");
        updateJson.Data.GetProperty("priority").GetInt32().Should().Be(20);
        updateJson.Data.GetProperty("enabled").GetBoolean().Should().BeFalse();
        updateJson.Data.GetProperty("matchMode").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Delete_ShouldRemoveRule()
    {
        // 先创建
        var createResp = await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = 4,  // Remark
            matchMode = 1,
            pattern = "待删除",
            priority = 0,
            enabled = true
        }));
        var createJson = await createResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var id = createJson.Data.GetProperty("id").GetInt32();

        // 删除
        var deleteResp = await _client.DeleteAsync($"{BaseUrl}/{id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 验证列表中不再包含该规则
        var listResp = await _client.GetAsync(BaseUrl);
        var listJson = await listResp.ReadAsAsync<ApiResponse<JsonElement>>();
        listJson.Code.Should().Be(0);
        var ids = listJson.Data.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToList();
        ids.Should().NotContain(id);
    }

    [Fact]
    public async Task GetAll_WithEnabledFilter_ShouldFilterCorrectly()
    {
        // 创建启用的规则
        await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = 1,
            matchMode = 1,
            pattern = "FilterTest-启用",
            priority = 0,
            enabled = true
        }));

        // 创建禁用的规则
        await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = 1,
            matchMode = 1,
            pattern = "FilterTest-禁用",
            priority = 0,
            enabled = false
        }));

        // 仅查询启用的
        var enabledResp = await _client.GetAsync($"{BaseUrl}?enabled=true");
        var enabledJson = await enabledResp.ReadAsAsync<ApiResponse<JsonElement>>();
        enabledJson.Code.Should().Be(0);

        var enabledPatterns = enabledJson.Data.EnumerateArray()
            .Select(e => e.GetProperty("pattern").GetString())
            .ToList();
        enabledPatterns.Should().Contain("FilterTest-启用");
        enabledPatterns.Should().NotContain("FilterTest-禁用");
    }

    [Fact]
    public async Task GetEffective_ShouldReturnEnabledSortedByPriority()
    {
        // 创建多条不同优先级和启用状态的规则
        await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = 1,   // Project
            matchMode = 1,
            pattern = "Effective-Low",
            priority = 1,
            enabled = true
        }));

        await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = 1,   // Project（同目标字段、高优先级）
            matchMode = 1,
            pattern = "Effective-High",
            priority = 100,
            enabled = true
        }));

        await _client.PostAsync(BaseUrl, ApiClientJson.ToJsonContent(new
        {
            targetField = 1,
            matchMode = 1,
            pattern = "Effective-Disabled",
            priority = 50,
            enabled = false
        }));

        var resp = await _client.GetAsync($"{BaseUrl}/effective");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(0);

        var patterns = json.Data.EnumerateArray()
            .Select(e => e.GetProperty("pattern").GetString())
            .ToList();

        // 禁用的不应出现
        patterns.Should().NotContain("Effective-Disabled");

        // 同 TargetField 下高优先级排在前面
        var projectRules = json.Data.EnumerateArray()
            .Where(e => e.GetProperty("targetField").GetInt32() == 1)
            .Select(e => e.GetProperty("pattern").GetString())
            .ToList();

        if (projectRules.Contains("Effective-High") && projectRules.Contains("Effective-Low"))
        {
            projectRules.IndexOf("Effective-High").Should().BeLessThan(projectRules.IndexOf("Effective-Low"));
        }
    }
}
