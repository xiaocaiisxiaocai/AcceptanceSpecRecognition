using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class SmartConfigConfirmLearningTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SmartConfigConfirmLearningTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Confirm_ShouldUpsertTemplateAndWriteCustomerLearnedRules()
    {
        var customerId = await CreateCustomerAsync("确认学习-客户A");

        var response = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            customerId,
            templateName = "客户A-规格模板",
            headers = new[] { "管控项目", "规格要求", "判定标准", "备注" },
            projectColumnIndex = 0,
            specificationColumnIndex = 1,
            acceptanceColumnIndex = 2,
            remarkColumnIndex = 3,
            headerRowIndex = 0,
            headerRowCount = 1,
            dataStartRowIndex = 1,
            dataEndRowIndex = 20,
            isSpecificationOnly = false,
            learnedColumns = new[]
            {
                new { header = "管控项目", targetField = 1 },
                new { header = "规格要求", targetField = 2 }
            }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(0);
        body.Data.GetProperty("templateSaved").GetBoolean().Should().BeTrue();
        body.Data.GetProperty("learnedRuleCount").GetInt32().Should().Be(2);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var template = await db.DocumentTemplates.SingleAsync(t => t.CustomerId == customerId);
        template.TemplateName.Should().Be("客户A-规格模板");
        template.ProjectColumnIndex.Should().Be(0);
        template.SpecificationColumnIndex.Should().Be(1);
        template.AcceptanceColumnIndex.Should().Be(2);
        template.RemarkColumnIndex.Should().Be(3);
        template.DataEndRowIndex.Should().Be(20);
        template.IsSpecificationOnly.Should().BeFalse();

        var learnedRules = await db.ColumnMappingRules
            .Where(rule => rule.CustomerId == customerId && rule.Source == ColumnMappingRuleSource.Learned)
            .OrderBy(rule => rule.TargetField)
            .ToListAsync();

        learnedRules.Select(rule => rule.Pattern).Should().Equal("管控项目", "规格要求");
    }

    [Fact]
    public async Task Confirm_WhenTwoCustomersConfirmSameHeader_ShouldPromoteGlobalLearnedRule()
    {
        var firstCustomerId = await CreateCustomerAsync("确认学习-客户B");
        var secondCustomerId = await CreateCustomerAsync("确认学习-客户C");
        await ConfirmHeaderAsync(customerId: firstCustomerId, header: "管控项目", targetField: 1);
        await ConfirmHeaderAsync(customerId: secondCustomerId, header: "管控项目", targetField: 1);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var globalRule = await db.ColumnMappingRules.SingleOrDefaultAsync(rule =>
            rule.CustomerId == null &&
            rule.Source == ColumnMappingRuleSource.Learned &&
            rule.TargetField == ColumnMappingTargetField.Project &&
            rule.Pattern == "管控项目");

        globalRule.Should().NotBeNull();
        globalRule!.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Confirm_WithSameCustomerAndHeaders_ShouldUpdateExistingTemplate()
    {
        var customerId = await CreateCustomerAsync("确认学习-客户D");

        await ConfirmTemplateAsync(customerId, "旧模板", acceptanceColumnIndex: 2);
        await ConfirmTemplateAsync(customerId, "新模板", acceptanceColumnIndex: 3);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var templates = await db.DocumentTemplates
            .Where(template => template.CustomerId == customerId)
            .ToListAsync();

        templates.Should().ContainSingle();
        templates[0].TemplateName.Should().Be("新模板");
        templates[0].AcceptanceColumnIndex.Should().Be(3);
    }

    [Fact]
    public async Task Confirm_WhenTemplateNameIsGenericSheetName_ShouldNotCreateLearnedRoutingRule()
    {
        var customerId = await CreateCustomerAsync("确认学习-通用表名客户");

        var response = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            customerId,
            templateName = "Sheet1",
            headers = new[] { "项目", "规格", "验收" },
            projectColumnIndex = 0,
            specificationColumnIndex = 1,
            acceptanceColumnIndex = 2,
            headerRowIndex = 0,
            headerRowCount = 1,
            dataStartRowIndex = 1,
            tableKind = "AcceptanceSpec",
            recommendation = "Recommended",
            isSpecificationOnly = false,
            learnedColumns = Array.Empty<object>()
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var learnedRoutingRule = await db.SmartStructureRoutingRules.SingleOrDefaultAsync(rule =>
            rule.CustomerId == customerId &&
            rule.Source == SmartStructureRoutingRuleSource.Learned);

        learnedRoutingRule.Should().BeNull();
    }

    private async Task ConfirmHeaderAsync(int customerId, string header, int targetField)
    {
        var response = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            customerId,
            templateName = $"客户{customerId}-模板",
            headers = new[] { header, "规格" },
            projectColumnIndex = 0,
            specificationColumnIndex = 1,
            headerRowIndex = 0,
            headerRowCount = 1,
            dataStartRowIndex = 1,
            isSpecificationOnly = false,
            learnedColumns = new[]
            {
                new { header, targetField }
            }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task ConfirmTemplateAsync(
        int customerId,
        string templateName,
        int acceptanceColumnIndex)
    {
        var response = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            customerId,
            templateName,
            headers = new[] { "项目", "规格", "验收", "备注" },
            projectColumnIndex = 0,
            specificationColumnIndex = 1,
            acceptanceColumnIndex,
            remarkColumnIndex = 3,
            headerRowIndex = 0,
            headerRowCount = 1,
            dataStartRowIndex = 1,
            isSpecificationOnly = false,
            learnedColumns = Array.Empty<object>()
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<int> CreateCustomerAsync(string name)
    {
        var response = await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("id").GetInt32();
    }
}

public class SmartConfigConfirmLearningConfiguredThresholdTests : IClassFixture<GlobalRulePromotionThresholdApiFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SmartConfigConfirmLearningConfiguredThresholdTests(GlobalRulePromotionThresholdApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Confirm_WhenPromotionThresholdIsThree_ShouldNotPromoteAfterTwoCustomers()
    {
        var firstCustomerId = await CreateCustomerAsync("确认学习-阈值客户A");
        var secondCustomerId = await CreateCustomerAsync("确认学习-阈值客户B");
        await ConfirmHeaderAsync(customerId: firstCustomerId, header: "管控项目", targetField: 1);
        await ConfirmHeaderAsync(customerId: secondCustomerId, header: "管控项目", targetField: 1);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var globalRuleExists = await db.ColumnMappingRules.AnyAsync(rule =>
            rule.CustomerId == null &&
            rule.Source == ColumnMappingRuleSource.Learned &&
            rule.TargetField == ColumnMappingTargetField.Project &&
            rule.Pattern == "管控项目");

        globalRuleExists.Should().BeFalse();
    }

    private async Task ConfirmHeaderAsync(int customerId, string header, int targetField)
    {
        var response = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            customerId,
            templateName = $"客户{customerId}-模板",
            headers = new[] { header, "规格" },
            projectColumnIndex = 0,
            specificationColumnIndex = 1,
            headerRowIndex = 0,
            headerRowCount = 1,
            dataStartRowIndex = 1,
            isSpecificationOnly = false,
            learnedColumns = new[]
            {
                new { header, targetField }
            }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<int> CreateCustomerAsync(string name)
    {
        var response = await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("id").GetInt32();
    }
}

public sealed class GlobalRulePromotionThresholdApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SmartConfiguration:GlobalRulePromotionCustomerThreshold"] = "3"
            });
        });
    }
}
