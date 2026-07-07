using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class SmartStructureRoutingRulesApiTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SmartStructureRoutingRulesApiTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAll_ShouldHideLegacyLearnedRoutingRules()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SmartStructureRoutingRules.Add(new SmartStructureRoutingRule
        {
            Name = "旧上传学习出来的验收表规则",
            TableKind = "AcceptanceSpec",
            Recommendation = "Recommended",
            MatchScope = SmartStructureRoutingMatchScope.TableName,
            MatchMode = SmartStructureRoutingMatchMode.Contains,
            Pattern = "验收规格",
            Enabled = true,
            Source = SmartStructureRoutingRuleSource.Learned
        });
        db.SmartStructureRoutingRules.Add(new SmartStructureRoutingRule
        {
            Name = "人工跳过报价",
            TableKind = "ManualAuxiliary",
            Recommendation = "Skip",
            MatchScope = SmartStructureRoutingMatchScope.Headers,
            MatchMode = SmartStructureRoutingMatchMode.Contains,
            Pattern = "报价",
            Enabled = true,
            Source = SmartStructureRoutingRuleSource.Manual
        });
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/api/smart-structure-routing-rules");
        var responseText = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);
        var body = JsonSerializer.Deserialize<ApiResponse<JsonElement>>(
            responseText,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        body.Code.Should().Be(0);
        var names = body.Data.EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .ToList();
        names.Should().Contain("人工跳过报价");
        names.Should().NotContain("旧上传学习出来的验收表规则");
    }

    [Fact]
    public async Task GetEffective_ShouldIgnoreLegacyLearnedRoutingRules()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SmartStructureRoutingRules.Add(new SmartStructureRoutingRule
        {
            Name = "旧上传学习出来的 Layout 规则",
            TableKind = "Layout",
            Recommendation = "Skip",
            MatchScope = SmartStructureRoutingMatchScope.TableName,
            MatchMode = SmartStructureRoutingMatchMode.Contains,
            Pattern = "Layout",
            Enabled = true,
            Source = SmartStructureRoutingRuleSource.Learned
        });
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/api/smart-structure-routing-rules/effective");
        var responseText = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);
        var body = JsonSerializer.Deserialize<ApiResponse<JsonElement>>(
            responseText,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        body.Code.Should().Be(0);
        body.Data.EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .Should()
            .NotContain("旧上传学习出来的 Layout 规则");
    }
}
