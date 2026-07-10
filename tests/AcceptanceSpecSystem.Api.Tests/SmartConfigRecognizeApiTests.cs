using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.Documents.Intelligence;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcceptanceSpecSystem.Api.Tests;

public class SmartConfigRecognizeApiTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SmartConfigRecognizeApiTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WithExcel_ShouldReturnFlatTables()
    {
        var customerId = await CreateCustomerAsync("智能识别-客户A");
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));

        var responseText = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, responseText);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(0);
        body.Data.GetProperty("fileId").GetInt32().Should().Be(fileId);
        body.Data.GetProperty("tables").ValueKind.Should().Be(JsonValueKind.Array);

        var table = body.Data.GetProperty("tables").EnumerateArray().Single();
        table.GetProperty("tableIndex").GetInt32().Should().Be(0);
        table.GetProperty("tableName").GetString().Should().Be("验收表");
        table.GetProperty("headers").EnumerateArray().Select(item => item.GetString())
            .Should().Contain(new[] { "项目", "规格内容", "验收结果", "备注" });
        table.GetProperty("headerRowIndex").GetInt32().Should().Be(0);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(1);
        table.GetProperty("projectColumnIndex").GetInt32().Should().Be(0);
        table.GetProperty("specificationColumnIndex").GetInt32().Should().Be(1);
        table.GetProperty("acceptanceColumnIndex").GetInt32().Should().Be(2);
        table.GetProperty("decision").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Recognize_WithExcelTitleBeforeTraditionalHeaders_ShouldDetectHeaderRowAndColumns()
    {
        var customerId = await CreateCustomerAsync("智能识别-繁体表头客户");
        var fileId = await UploadExcelAsync(
            CreateExcelWithTraditionalHeadersAfterTitleBytes(),
            "smart-recognize-traditional-title.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));

        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(0);

        var table = body.Data.GetProperty("tables").EnumerateArray().Single();
        table.GetProperty("headers").EnumerateArray().Select(item => item.GetString())
            .Should().Contain(new[] { "驗收項目", "驗收規格", "驗收方法", "設備商確認", "備註" });
        table.GetProperty("headerRowIndex").GetInt32().Should().Be(1);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(2);
        table.GetProperty("projectColumnIndex").GetInt32().Should().Be(1);
        table.GetProperty("specificationColumnIndex").GetInt32().Should().Be(3);
        table.GetProperty("acceptanceColumnIndex").GetInt32().Should().Be(5);
        table.GetProperty("remarkColumnIndex").GetInt32().Should().Be(6);
    }

    [Fact]
    public async Task Recognize_WhenCustomerTemplateExists_ShouldUseTemplate()
    {
        var customerId = await CreateCustomerAsync("智能识别-客户B");
        await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            customerId,
            templateName = "模板命中",
            headers = new[] { "项目", "规格内容", "验收结果", "备注" },
            projectColumnIndex = 0,
            specificationColumnIndex = 1,
            acceptanceColumnIndex = 2,
            remarkColumnIndex = 3,
            headerRowIndex = 0,
            headerRowCount = 1,
            dataStartRowIndex = 1,
            isSpecificationOnly = false,
            learnedColumns = Array.Empty<object>()
        }));
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-template.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("source").GetString().Should().Be("Template");
        table.GetProperty("decision").GetString().Should().Be("AutoApply");
        table.GetProperty("confidence").GetDouble().Should().Be(1.0);
    }

    [Fact]
    public async Task Recognize_WhenCustomerTemplateMissesRequiredColumns_ShouldNeedConfirmAndClampRowRange()
    {
        var customerId = await CreateCustomerAsync("智能识别-模板健康检查客户");
        await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            customerId,
            templateName = "缺列模板",
            headers = new[] { "项目", "规格内容", "验收结果", "备注" },
            projectColumnIndex = 0,
            specificationColumnIndex = 1,
            acceptanceColumnIndex = (int?)null,
            remarkColumnIndex = (int?)null,
            headerRowIndex = 0,
            headerRowCount = 1,
            dataStartRowIndex = 1,
            dataEndRowIndex = 999,
            isSpecificationOnly = false,
            learnedColumns = Array.Empty<object>()
        }));
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-template-health.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("source").GetString().Should().Be("Template");
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
        table.GetProperty("dataEndRowIndex").GetInt32().Should().Be(1);
        table.GetProperty("acceptanceColumnIndex").ValueKind.Should().Be(JsonValueKind.Null);
        table.GetProperty("remarkColumnIndex").ValueKind.Should().Be(JsonValueKind.Null);
        table.GetProperty("issues").EnumerateArray()
            .Should()
            .Contain(issue =>
                issue.GetProperty("code").GetString() == "MissingRemarkColumn" &&
                issue.GetProperty("severity").GetString() == "Info" &&
                issue.GetProperty("field").GetString() == "Remark");
    }

    [Fact]
    public async Task Recognize_WhenColumnIsInferredFromSamples_ShouldExposePerFieldConfidence()
    {
        var fileId = await UploadExcelAsync(CreateExcelWithSampleInferredAcceptanceBytes(), "smart-recognize-field-confidence.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();
        var fields = table.GetProperty("fields").EnumerateArray().ToDictionary(
            field => field.GetProperty("field").GetString()!,
            field => field);

        fields["Project"].GetProperty("confidence").GetDouble().Should().BeApproximately(0.99, 0.001);
        fields["Specification"].GetProperty("confidence").GetDouble().Should().BeApproximately(0.99, 0.001);
        fields["Acceptance"].GetProperty("confidence").GetDouble().Should().BeApproximately(0.75, 0.001);
    }

    [Fact]
    public async Task Recognize_WithMixedExcelSheets_ShouldReturnAdaptiveRoutingMetadata()
    {
        var customerId = await CreateCustomerAsync("智能识别-混合工作簿客户");
        await CreateRoutingRuleAsync("报价规则", "Quotation", "Skip", "TableName", "Contains", "報價", 100);
        await CreateRoutingRuleAsync("Layout规则", "Layout", "Skip", "TableName", "Contains", "Layout", 100);
        await CreateRoutingRuleAsync("Utility规则", "Utility", "Skip", "TableName", "Contains", "Utility", 100);
        await CreateRoutingRuleAsync("备品规则", "BomOrSpareParts", "Skip", "TableName", "Contains", "备品", 100);
        var fileId = await UploadExcelAsync(
            CreateMixedWorkbookBytes(),
            "smart-recognize-mixed-routing.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));
        var responseText = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var tables = body.Data.GetProperty("tables").EnumerateArray().ToList();
        tables.Should().HaveCount(5);

        var acceptance = tables.Single(table => table.GetProperty("tableName").GetString() == "验收表");
        acceptance.GetProperty("tableKind").GetString().Should().Be("AcceptanceSpec");
        acceptance.GetProperty("recommendation").GetString().Should().BeOneOf("Recommended", "NeedConfirm");
        acceptance.GetProperty("rankingScore").GetDouble().Should().BeGreaterThan(0.7);
        acceptance.GetProperty("issues").ValueKind.Should().Be(JsonValueKind.Array);

        foreach (var tableName in new[] { "報價單", "Layout", "Utility", "备品清单" })
        {
            var table = tables.Single(item => item.GetProperty("tableName").GetString() == tableName);
            table.GetProperty("recommendation").GetString().Should().Be("Skip", tableName);
            table.GetProperty("rankingScore").GetDouble().Should().BeLessThan(0.5, tableName);
            table.GetProperty("skipReason").GetString().Should().NotBeNullOrWhiteSpace(tableName);
            table.GetProperty("issues").EnumerateArray()
                .Select(item => item.GetProperty("message").GetString())
                .Should()
                .Contain(message => !string.IsNullOrWhiteSpace(message), tableName);
        }
    }

    [Fact]
    public async Task Recognize_WithoutRoutingRules_ShouldNotSkipByHardcodedBusinessWords()
    {
        var customerId = await CreateCustomerAsync("智能识别-无路由规则客户");
        var fileId = await UploadExcelAsync(
            CreateQuotationOnlyWorkbookBytes(),
            "smart-recognize-no-routing-rules.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));
        var responseText = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("tableName").GetString().Should().Be("報價單");
        table.GetProperty("recommendation").GetString().Should().NotBe("Skip");
        table.GetProperty("tableKind").GetString().Should().Be("Unknown");
    }

    [Fact]
    public async Task Confirm_WhenExcelSheetNameIsSpecific_ShouldNotCreateCustomerScopedLearnedRoutingRule()
    {
        var customerId = await CreateCustomerAsync("智能识别-路由学习收敛客户");

        var response = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            customerId,
            templateName = "客户专用验收主表",
            headers = new[] { "项目", "规格内容", "验收结果", "备注" },
            projectColumnIndex = 0,
            specificationColumnIndex = 1,
            acceptanceColumnIndex = 2,
            remarkColumnIndex = 3,
            headerRowIndex = 0,
            headerRowCount = 1,
            dataStartRowIndex = 1,
            isSpecificationOnly = false,
            tableKind = "AcceptanceSpec",
            recommendation = "Recommended",
            learnedColumns = Array.Empty<object>()
        }));
        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var learnedRuleExists = await db.SmartStructureRoutingRules.AnyAsync(rule =>
            rule.CustomerId == customerId &&
            rule.Source == SmartStructureRoutingRuleSource.Learned &&
            rule.MatchScope == SmartStructureRoutingMatchScope.TableName &&
            rule.Pattern == "客户专用验收主表");

        learnedRuleExists.Should().BeFalse();
        (await db.DocumentTemplates.AnyAsync(template =>
            template.CustomerId == customerId &&
            template.TemplateName == "客户专用验收主表")).Should().BeTrue();
    }

    [Fact]
    public async Task Confirm_WhenWordTableTitleLooksSpecific_ShouldNotCreateTableNameRoutingRule()
    {
        var customerId = await CreateCustomerAsync("智能识别-Word结构学习客户");

        var response = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            customerId,
            templateName = "第3表 安全验收项目",
            headers = new[] { "检查项目", "规格要求", "判定标准" },
            projectColumnIndex = 0,
            specificationColumnIndex = 1,
            acceptanceColumnIndex = 2,
            headerRowIndex = 0,
            headerRowCount = 1,
            dataStartRowIndex = 1,
            isSpecificationOnly = false,
            tableKind = "SafetySpec",
            recommendation = "NeedConfirm",
            learnedColumns = Array.Empty<object>()
        }));
        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var learnedTableNameRuleExists = await db.SmartStructureRoutingRules.AnyAsync(rule =>
            rule.CustomerId == customerId &&
            rule.Source == SmartStructureRoutingRuleSource.Learned &&
            rule.MatchScope == SmartStructureRoutingMatchScope.TableName);

        learnedTableNameRuleExists.Should().BeFalse();
        (await db.DocumentTemplates.AnyAsync(template =>
            template.CustomerId == customerId &&
            template.TemplateName == "第3表 安全验收项目")).Should().BeTrue();
    }

    private async Task<int> CreateCustomerAsync(string name)
    {
        var response = await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("id").GetInt32();
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);

        var json = JsonSerializer.Deserialize<ApiResponse<JsonElement>>(
            responseText,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private async Task CreateRoutingRuleAsync(
        string name,
        string tableKind,
        string recommendation,
        string matchScope,
        string matchMode,
        string pattern,
        int priority)
    {
        var response = await _client.PostAsync("/api/smart-structure-routing-rules", ApiClientJson.ToJsonContent(new
        {
            name,
            tableKind,
            recommendation,
            matchScope,
            matchMode,
            pattern,
            priority,
            weight = 1.0,
            enabled = true,
            source = "Manual"
        }));
        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格内容";
        worksheet.Cell(1, 3).Value = "验收结果";
        worksheet.Cell(1, 4).Value = "备注";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(2, 3).Value = "目视 OK";
        worksheet.Cell(2, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateExcelWithTraditionalHeadersAfterTitleBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Tray盤式投收板機驗收規格");
        worksheet.Cell(1, 1).Value = "Tray盤式投收板機驗收規格 單位：MSAP";
        worksheet.Range(1, 1, 1, 7).Merge();
        worksheet.Cell(2, 1).Value = "項次";
        worksheet.Cell(2, 2).Value = "驗收項目";
        worksheet.Cell(2, 4).Value = "驗收規格";
        worksheet.Cell(2, 5).Value = "驗收方法";
        worksheet.Cell(2, 6).Value = "設備商確認";
        worksheet.Cell(2, 7).Value = "備註";
        worksheet.Cell(3, 1).Value = "1";
        worksheet.Cell(3, 2).Value = "投收板機設備制程能力";
        worksheet.Cell(3, 3).Value = "設備流向";
        worksheet.Cell(3, 4).Value = "依主設備流向";
        worksheet.Cell(3, 5).Value = "裝機時檢查";
        worksheet.Cell(3, 6).Value = "■OK   □NG";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateExcelWithSampleInferredAcceptanceBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "回复栏";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(2, 3).Value = "OK";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateMixedWorkbookBytes()
    {
        using var workbook = new XLWorkbook();

        var acceptance = workbook.AddWorksheet("验收表");
        acceptance.Cell(1, 1).Value = "序号";
        acceptance.Cell(1, 2).Value = "项目";
        acceptance.Cell(1, 3).Value = "技术要求";
        acceptance.Cell(1, 4).Value = "供方能力";
        acceptance.Cell(1, 5).Value = "备注";
        acceptance.Cell(2, 1).Value = "1";
        acceptance.Cell(2, 2).Value = "外观";
        acceptance.Cell(2, 3).Value = "表面不得有明显划伤";
        acceptance.Cell(2, 4).Value = "OK";
        acceptance.Cell(2, 5).Value = "抽检";

        var quotation = workbook.AddWorksheet("報價單");
        quotation.Cell(1, 1).Value = "品名";
        quotation.Cell(1, 2).Value = "单价";
        quotation.Cell(1, 3).Value = "数量";
        quotation.Cell(1, 4).Value = "金额";
        quotation.Cell(2, 1).Value = "投收板机";
        quotation.Cell(2, 2).Value = "100";
        quotation.Cell(2, 3).Value = "1";
        quotation.Cell(2, 4).Value = "100";

        var layout = workbook.AddWorksheet("Layout");
        layout.Cell(1, 1).Value = "X";
        layout.Cell(1, 2).Value = "Y";
        layout.Cell(1, 3).Value = "设备位置";
        layout.Cell(2, 1).Value = "100";
        layout.Cell(2, 2).Value = "200";
        layout.Cell(2, 3).Value = "上料区";

        var utility = workbook.AddWorksheet("Utility");
        utility.Cell(1, 1).Value = "设备名称";
        utility.Cell(1, 2).Value = "电力需求";
        utility.Cell(1, 3).Value = "空压";
        utility.Cell(1, 4).Value = "排废";
        utility.Cell(2, 1).Value = "投收板机";
        utility.Cell(2, 2).Value = "220V";
        utility.Cell(2, 3).Value = "0.5MPa";
        utility.Cell(2, 4).Value = "无";

        var spareParts = workbook.AddWorksheet("备品清单");
        spareParts.Cell(1, 1).Value = "配件名称";
        spareParts.Cell(1, 2).Value = "品牌";
        spareParts.Cell(1, 3).Value = "规格型号";
        spareParts.Cell(1, 4).Value = "数量";
        spareParts.Cell(1, 5).Value = "备注";
        spareParts.Cell(2, 1).Value = "皮带";
        spareParts.Cell(2, 2).Value = "厂商A";
        spareParts.Cell(2, 3).Value = "B-100";
        spareParts.Cell(2, 4).Value = "2";
        spareParts.Cell(2, 5).Value = "随机";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateQuotationOnlyWorkbookBytes()
    {
        using var workbook = new XLWorkbook();
        var quotation = workbook.AddWorksheet("報價單");
        quotation.Cell(1, 1).Value = "品名";
        quotation.Cell(1, 2).Value = "单价";
        quotation.Cell(1, 3).Value = "数量";
        quotation.Cell(1, 4).Value = "金额";
        quotation.Cell(2, 1).Value = "投收板机";
        quotation.Cell(2, 2).Value = "100";
        quotation.Cell(2, 3).Value = "1";
        quotation.Cell(2, 4).Value = "100";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class SmartConfigRecognizeHealthCheckApiTests : IClassFixture<MissingSpecificationColumnIntelligenceApiFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeHealthCheckApiTests(MissingSpecificationColumnIntelligenceApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenHighConfidenceResultMissesSpecificationColumn_ShouldNeedConfirm()
    {
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-missing-spec.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));
        var responseText = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("confidence").GetDouble().Should().Be(0.96);
        table.GetProperty("specificationColumnIndex").ValueKind.Should().Be(JsonValueKind.Null);
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);

        var json = JsonSerializer.Deserialize<ApiResponse<JsonElement>>(
            responseText,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "验收结果";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "目视 OK";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class SmartConfigRecognizeLowConfidenceApiTests : IClassFixture<LowConfidenceCompleteMappingApiFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeLowConfidenceApiTests(LowConfidenceCompleteMappingApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenCompleteMappingHasLowConfidenceAndLlmBudgetIsZero_ShouldNeedConfirm()
    {
        ZeroBudgetCountingStructureAdjudicationService.Reset();
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-low-confidence.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));
        var responseText = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("source").GetString().Should().Be("RuleBased");
        table.GetProperty("confidence").GetDouble().Should().Be(0.6);
        table.GetProperty("projectColumnIndex").GetInt32().Should().Be(0);
        table.GetProperty("specificationColumnIndex").GetInt32().Should().Be(1);
        table.GetProperty("acceptanceColumnIndex").GetInt32().Should().Be(2);
        table.GetProperty("remarkColumnIndex").GetInt32().Should().Be(3);
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
        ZeroBudgetCountingStructureAdjudicationService.CallCount.Should().Be(0);
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "验收结果";
        worksheet.Cell(1, 4).Value = "备注";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(2, 3).Value = "OK";
        worksheet.Cell(2, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class SmartConfigRecognizeLlmFusionApiTests : IClassFixture<LlmFillsMissingSpecificationApiFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeLlmFusionApiTests(LlmFillsMissingSpecificationApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenRuleMissesSpecificationAndLlmFillsIt_ShouldReturnFusedAutoApply()
    {
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-llm-fusion.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));
        var responseText = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, responseText);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("source").GetString().Should().Be("Fused");
        table.GetProperty("specificationColumnIndex").GetInt32().Should().Be(1);
        table.GetProperty("decision").GetString().Should().Be("AutoApply");
    }

    [Fact]
    public async Task Recognize_WhenSemanticRecallRunsBeforeLlmFusion_ShouldKeepRecallSuggestions()
    {
        using var factory = new LlmFillsMissingSpecificationWithSemanticRecallApiFactory();
        var client = factory.CreateClient();
        var fileId = await UploadExcelAsync(client, CreateExcelBytes(), "smart-recognize-llm-fusion-recall.xlsx");

        var response = await client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();
        var suggestions = table.TryGetProperty("semanticRecallSuggestions", out var suggestionsElement) &&
                          suggestionsElement.ValueKind == JsonValueKind.Array
            ? suggestionsElement.EnumerateArray().ToList()
            : [];

        table.GetProperty("source").GetString().Should().Be("Fused");
        table.GetProperty("specificationColumnIndex").GetInt32().Should().Be(1);
        suggestions.Should().ContainSingle();
        suggestions.Single().GetProperty("targetField").GetString().Should().Be("Specification");
    }

    private Task<int> UploadExcelAsync(byte[] bytes, string fileName) =>
        UploadExcelAsync(_client, bytes, fileName);

    private static async Task<int> UploadExcelAsync(HttpClient client, byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "验收结果";
        worksheet.Cell(1, 4).Value = "备注";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(2, 3).Value = "目视 OK";
        worksheet.Cell(2, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class SmartConfigRecognizeLlmHeaderAdjudicationApiTests : IClassFixture<LlmCorrectsHeaderStructureApiFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeLlmHeaderAdjudicationApiTests(LlmCorrectsHeaderStructureApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenLlmReturnsValidHeaderStructure_ShouldReextractTableWithLlmHeader()
    {
        HeaderCorrectionStructureAdjudicationService.Reset();
        var fileId = await UploadExcelAsync(
            CreateExcelWithLeadingDescriptionBytes(),
            "smart-recognize-llm-header-adjudication.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));
        var responseText = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, responseText);
        HeaderCorrectionStructureAdjudicationService.CallCount.Should().Be(1);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("source").GetString().Should().Be("Fused");
        table.GetProperty("headerRowIndex").GetInt32().Should().Be(1);
        table.GetProperty("headerRowCount").GetInt32().Should().Be(1);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(2);
        table.GetProperty("headers").EnumerateArray()
            .Select(item => item.GetString())
            .Should()
            .Equal("项目", "规格", "验收标准", "备注");
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
    }

    [Fact]
    public async Task Recognize_WhenWordNeedsLlmHeaderStructure_ShouldReextractTableWithLlmHeader()
    {
        HeaderCorrectionStructureAdjudicationService.Reset();
        var fileId = await UploadWordAsync(
            CreateWordBytes([
                ["客户A", "机种X", "版本B", "量产"],
                ["项目", "规格", "验收标准", "备注"],
                ["外观", "表面不得有明显划伤", "目视 OK", "抽检"]
            ]),
            "smart-recognize-word-llm-header-adjudication.docx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));
        var responseText = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, responseText);
        HeaderCorrectionStructureAdjudicationService.CallCount.Should().Be(1);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("source").GetString().Should().Be("Fused");
        table.GetProperty("headerRowIndex").GetInt32().Should().Be(1);
        table.GetProperty("headerRowCount").GetInt32().Should().Be(1);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(2);
        table.GetProperty("headers").EnumerateArray()
            .Select(item => item.GetString())
            .Should()
            .Equal("项目", "规格", "验收标准", "备注");
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private async Task<int> UploadWordAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelWithLeadingDescriptionBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "客户A";
        worksheet.Cell(1, 2).Value = "机种X";
        worksheet.Cell(1, 3).Value = "版本B";
        worksheet.Cell(1, 4).Value = "量产";
        worksheet.Cell(2, 1).Value = "项目";
        worksheet.Cell(2, 2).Value = "规格";
        worksheet.Cell(2, 3).Value = "验收标准";
        worksheet.Cell(2, 4).Value = "备注";
        worksheet.Cell(3, 1).Value = "外观";
        worksheet.Cell(3, 2).Value = "表面不得有明显划伤";
        worksheet.Cell(3, 3).Value = "目视 OK";
        worksheet.Cell(3, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateWordBytes(string[][] rows)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body());

            var table = new Table();
            table.AppendChild(new TableProperties(new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));

            foreach (var row in rows)
            {
                var tableRow = new TableRow();
                foreach (var cell in row)
                {
                    tableRow.AppendChild(new TableCell(
                        new Paragraph(new Run(new Text(cell ?? string.Empty)))));
                }

                table.AppendChild(tableRow);
            }

            main.Document.Body!.Append(table);
            main.Document.Save();
        }

        return stream.ToArray();
    }
}

public class SmartConfigRecognizeInvalidLlmHeaderAdjudicationApiTests : IClassFixture<LlmInvalidHeaderStructureApiFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeInvalidLlmHeaderAdjudicationApiTests(LlmInvalidHeaderStructureApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenLlmReturnsInvalidHeaderStructure_ShouldKeepRuleResult()
    {
        InvalidHeaderStructureAdjudicationService.Reset();
        var fileId = await UploadExcelAsync(
            CreateExcelWithLeadingDescriptionBytes(),
            "smart-recognize-llm-invalid-header-adjudication.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));
        var responseText = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, responseText);
        InvalidHeaderStructureAdjudicationService.CallCount.Should().Be(1);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("source").GetString().Should().Be("RuleBased");
        table.GetProperty("headerRowIndex").GetInt32().Should().Be(0);
        table.GetProperty("headers").EnumerateArray()
            .Select(item => item.GetString())
            .Should()
            .NotContain("项目");
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelWithLeadingDescriptionBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "客户A";
        worksheet.Cell(1, 2).Value = "机种X";
        worksheet.Cell(1, 3).Value = "版本B";
        worksheet.Cell(1, 4).Value = "量产";
        worksheet.Cell(2, 1).Value = "项目";
        worksheet.Cell(2, 2).Value = "规格";
        worksheet.Cell(2, 3).Value = "验收标准";
        worksheet.Cell(2, 4).Value = "备注";
        worksheet.Cell(3, 1).Value = "外观";
        worksheet.Cell(3, 2).Value = "表面不得有明显划伤";
        worksheet.Cell(3, 3).Value = "目视 OK";
        worksheet.Cell(3, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

}

public class SmartConfigRecognizeHistoryFewShotApiTests : IClassFixture<LlmRecordingHistoryFewShotApiFactory>
{
    private readonly HttpClient _client;
    private readonly LlmRecordingHistoryFewShotApiFactory _factory;

    public SmartConfigRecognizeHistoryFewShotApiTests(LlmRecordingHistoryFewShotApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenLlmAdjudicates_ShouldPassCustomerHistoryTemplatesAsReferenceCases()
    {
        RecordingStructureAdjudicationService.Reset();
        var customerId = await CreateCustomerAsync("历史案例客户");
        await ConfirmTemplateAsync(customerId);
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-history-fewshot.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        RecordingStructureAdjudicationService.LastRequest.Should().NotBeNull();
        var referenceCase = RecordingStructureAdjudicationService.LastRequest!.ReferenceCases.Should().ContainSingle().Subject;
        referenceCase.TemplateName.Should().Be("历史确认模板");
        referenceCase.Headers.Should().Equal("检查对象", "管制条件", "供应商回复", "补充说明");
        referenceCase.Mapping.SpecificationColumnIndex.Should().Be(1);
        referenceCase.Mapping.AcceptanceColumnIndex.Should().Be(2);
        referenceCase.Mapping.RemarkColumnIndex.Should().Be(3);
        referenceCase.Similarity.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Recognize_WhenNoCustomerHistory_ShouldPassEmptyReferenceCases()
    {
        RecordingStructureAdjudicationService.Reset();
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-no-history-fewshot.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        RecordingStructureAdjudicationService.LastRequest.Should().NotBeNull();
        RecordingStructureAdjudicationService.LastRequest!.ReferenceCases.Should().BeEmpty();
    }

    [Fact]
    public async Task Recognize_WhenLlmAdjudicates_ShouldPassExplicitOriginalRowCoordinates()
    {
        RecordingStructureAdjudicationService.Reset();
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-llm-row-coordinates.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        RecordingStructureAdjudicationService.LastRequest.Should().NotBeNull();

        using var document = JsonDocument.Parse(RecordingStructureAdjudicationService.LastRequest!.DocumentTablesJson);
        var table = document.RootElement.EnumerateArray().Single();
        table.GetProperty("rowCoordinateSystem").GetString().Should().Be("zeroBasedOriginalTableRowIndex");
        table.GetProperty("totalRowCount").GetInt32().Should().Be(2);

        var headerRow = table.GetProperty("headerRows").EnumerateArray().Single();
        headerRow.GetProperty("rowIndex").GetInt32().Should().Be(0);
        headerRow.GetProperty("cells").EnumerateArray().Select(cell => cell.GetString())
            .Should().Equal("检查对象", "管制条件", "供应商确认", "补充说明");

        var sampleRow = table.GetProperty("sampleRows").EnumerateArray().Single();
        sampleRow.GetProperty("rowIndex").GetInt32().Should().Be(1);
        sampleRow.GetProperty("cells").EnumerateArray().Select(cell => cell.GetString())
            .Should().Equal("外观", "无划伤", "OK", "抽检");
    }

    [Fact]
    public async Task FindReferenceCases_ShouldPrioritizeSimilarityBeforeUsageCount()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var templateService = scope.ServiceProvider.GetRequiredService<DocumentTemplateAppService>();
        var customerId = 90307;

        db.Customers.Add(new Customer
        {
            Id = customerId,
            Name = "历史案例排序客户",
            CreatedAt = DateTime.UtcNow
        });

        for (var i = 0; i < 20; i++)
        {
            db.DocumentTemplates.Add(CreateTemplate(
                customerId,
                $"高频低相似模板{i}",
                ["字段A", "字段B", $"字段{i}", "字段D"],
                usageCount: 100 + i,
                updatedAt: DateTime.UtcNow.AddMinutes(i)));
        }

        db.DocumentTemplates.Add(CreateTemplate(
            customerId,
            "低频高相似模板",
            ["检查对象", "管制条件", "供应商确认", "补充说明"],
            usageCount: 1,
            updatedAt: DateTime.UtcNow.AddDays(-1)));
        await db.SaveChangesAsync();

        var cases = await templateService.FindReferenceCasesAsync(
            customerId,
            ["检查对象", "管制条件", "供应商确认", "补充说明"],
            maxCount: 1);

        cases.Should().ContainSingle();
        cases[0].TemplateName.Should().Be("低频高相似模板");
        cases[0].Similarity.Should().Be(1);
    }

    [Fact]
    public async Task FindReferenceCases_WhenTableNameProvided_ShouldApplyRecentUsageAndCorrectionWeight()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var templateService = scope.ServiceProvider.GetRequiredService<DocumentTemplateAppService>();
        var customerId = 90308;

        db.Customers.Add(new Customer
        {
            Id = customerId,
            Name = "历史案例权重客户",
            CreatedAt = DateTime.UtcNow
        });
        var oldModifiedTemplate = CreateTemplate(
            customerId,
            "无关旧模板",
            ["项目", "规格", "验收结果", "备注"],
            usageCount: 200,
            updatedAt: DateTime.UtcNow.AddDays(-365));
        oldModifiedTemplate.ConfirmedAt = DateTime.UtcNow.AddDays(-365);
        oldModifiedTemplate.UserModifiedStructure = true;
        db.DocumentTemplates.Add(oldModifiedTemplate);
        db.DocumentTemplates.Add(CreateTemplate(
            customerId,
            "验收表",
            ["项目", "规格", "验收", "备注"],
            usageCount: 1,
            updatedAt: DateTime.UtcNow));
        await db.SaveChangesAsync();

        var cases = await templateService.FindReferenceCasesAsync(
            customerId,
            ["项目", "规格", "验收", "备注"],
            maxCount: 1,
            tableName: "验收表");

        cases.Should().ContainSingle();
        cases[0].TemplateName.Should().Be("验收表");
        cases[0].Similarity.Should().BeGreaterThan(0.9);
    }


    [Fact]
    public async Task Confirm_WhenRoutingMetadataProvided_ShouldPersistStructureCaseMetadata()
    {
        var customerId = await CreateCustomerAsync("历史案例元数据客户");

        var response = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            customerId,
            templateName = "带路由元数据模板",
            headers = new[] { "项目", "规格", "验收", "备注" },
            projectColumnIndex = 0,
            specificationColumnIndex = 1,
            acceptanceColumnIndex = 2,
            remarkColumnIndex = 3,
            headerRowIndex = 0,
            headerRowCount = 1,
            dataStartRowIndex = 1,
            isSpecificationOnly = false,
            tableKind = "AcceptanceSpec",
            recommendation = "Recommended",
            userModifiedStructure = true,
            learnedColumns = Array.Empty<object>()
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var template = db.DocumentTemplates
            .Where(item => item.CustomerId == customerId && item.TemplateName == "带路由元数据模板")
            .Single();

        template.TableKind.Should().Be("AcceptanceSpec");
        template.Recommendation.Should().Be("Recommended");
        template.UserModifiedStructure.Should().BeTrue();
        template.ConfirmedAt.Should().NotBeNull();
    }

    private async Task<int> CreateCustomerAsync(string name)
    {
        var response = await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("id").GetInt32();
    }

    private async Task ConfirmTemplateAsync(int customerId)
    {
        var response = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            customerId,
            templateName = "历史确认模板",
            headers = new[] { "检查对象", "管制条件", "供应商回复", "补充说明" },
            projectColumnIndex = 0,
            specificationColumnIndex = 1,
            acceptanceColumnIndex = 2,
            remarkColumnIndex = 3,
            headerRowIndex = 0,
            headerRowCount = 1,
            dataStartRowIndex = 1,
            isSpecificationOnly = false,
            learnedColumns = Array.Empty<object>()
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "检查对象";
        worksheet.Cell(1, 2).Value = "管制条件";
        worksheet.Cell(1, 3).Value = "供应商确认";
        worksheet.Cell(1, 4).Value = "补充说明";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(2, 3).Value = "OK";
        worksheet.Cell(2, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static DocumentTemplate CreateTemplate(
        int customerId,
        string name,
        IReadOnlyList<string> headers,
        int usageCount,
        DateTime updatedAt)
    {
        return new DocumentTemplate
        {
            CustomerId = customerId,
            TemplateName = name,
            HeadersFingerprint = string.Join("|", headers.Select(header => header.Trim().ToLowerInvariant())),
            HeadersJson = JsonSerializer.Serialize(headers),
            ProjectColumnIndex = 0,
            SpecificationColumnIndex = 1,
            AcceptanceColumnIndex = 2,
            RemarkColumnIndex = 3,
            HeaderRowIndex = 0,
            HeaderRowCount = 1,
            DataStartRowIndex = 1,
            UsageCount = usageCount,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt
        };
    }
}

public class SmartConfigRecognizeOffsetRowCoordinateApiTests : IClassFixture<LlmOffsetHeaderRecordingApiFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeOffsetRowCoordinateApiTests(LlmOffsetHeaderRecordingApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenLlmAdjudicatesAfterLeadingRows_ShouldPassOriginalRowCoordinates()
    {
        OffsetHeaderRecordingStructureAdjudicationService.Reset();
        var fileId = await UploadExcelAsync(
            CreateExcelWithLeadingDescriptionBytes(),
            "smart-recognize-llm-offset-row-coordinates.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        OffsetHeaderRecordingStructureAdjudicationService.LastRequest.Should().NotBeNull();

        using var document = JsonDocument.Parse(OffsetHeaderRecordingStructureAdjudicationService.LastRequest!.DocumentTablesJson);
        var table = document.RootElement.EnumerateArray().Single();
        table.GetProperty("rowCoordinateSystem").GetString().Should().Be("zeroBasedOriginalTableRowIndex");
        table.GetProperty("totalRowCount").GetInt32().Should().Be(3);

        var headerRow = table.GetProperty("headerRows").EnumerateArray().Single();
        headerRow.GetProperty("rowIndex").GetInt32().Should().Be(1);
        headerRow.GetProperty("cells").EnumerateArray().Select(cell => cell.GetString())
            .Should().Equal("检查对象", "管制条件", "供应商确认", "补充说明");

        var sampleRow = table.GetProperty("sampleRows").EnumerateArray().Single();
        sampleRow.GetProperty("rowIndex").GetInt32().Should().Be(2);
        sampleRow.GetProperty("cells").EnumerateArray().Select(cell => cell.GetString())
            .Should().Equal("外观", "无划伤", "OK", "抽检");
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelWithLeadingDescriptionBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "客户A";
        worksheet.Cell(1, 2).Value = "机种X";
        worksheet.Cell(1, 3).Value = "版本B";
        worksheet.Cell(1, 4).Value = "量产";
        worksheet.Cell(2, 1).Value = "检查对象";
        worksheet.Cell(2, 2).Value = "管制条件";
        worksheet.Cell(2, 3).Value = "供应商确认";
        worksheet.Cell(2, 4).Value = "补充说明";
        worksheet.Cell(3, 1).Value = "外观";
        worksheet.Cell(3, 2).Value = "无划伤";
        worksheet.Cell(3, 3).Value = "OK";
        worksheet.Cell(3, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class SmartConfigRecognizeSpecificationOnlyApiTests : IClassFixture<SpecificationOnlyIntelligenceApiFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeSpecificationOnlyApiTests(SpecificationOnlyIntelligenceApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenHighConfidenceResultIsSpecificationOnly_ShouldReturnAutoApply()
    {
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-specification-only.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("projectColumnIndex").ValueKind.Should().Be(JsonValueKind.Null);
        table.GetProperty("specificationColumnIndex").GetInt32().Should().Be(0);
        table.GetProperty("acceptanceColumnIndex").GetInt32().Should().Be(1);
        table.GetProperty("remarkColumnIndex").GetInt32().Should().Be(2);
        table.GetProperty("isSpecificationOnly").GetBoolean().Should().BeTrue();
        table.GetProperty("decision").GetString().Should().Be("AutoApply");
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "规格";
        worksheet.Cell(1, 2).Value = "验收标准";
        worksheet.Cell(1, 3).Value = "备注";
        worksheet.Cell(2, 1).Value = "无划伤";
        worksheet.Cell(2, 2).Value = "目视 OK";
        worksheet.Cell(2, 3).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

}

public class SmartConfigRecognizeLearningRuleApiTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeLearningRuleApiTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WithCustomerLearnedColumnRules_ShouldUseThemInRuleRecognition()
    {
        var customerId = await CreateCustomerAsync("识别学习词客户");
        await CreateColumnRuleAsync(customerId, "检查对象", targetField: 1);
        await CreateColumnRuleAsync(customerId, "管制条件", targetField: 2);
        await CreateColumnRuleAsync(customerId, "供应商回复", targetField: 3);
        await CreateColumnRuleAsync(customerId, "补充说明", targetField: 4);
        var fileId = await UploadExcelAsync(CreateLearningRuleExcelBytes(), "smart-recognize-learning-rules.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("source").GetString().Should().Be("RuleBased");
        table.GetProperty("projectColumnIndex").GetInt32().Should().Be(0);
        table.GetProperty("specificationColumnIndex").GetInt32().Should().Be(1);
        table.GetProperty("acceptanceColumnIndex").GetInt32().Should().Be(2);
        table.GetProperty("remarkColumnIndex").GetInt32().Should().Be(3);
        table.GetProperty("decision").GetString().Should().Be("AutoApply");
    }

    [Fact]
    public async Task Recognize_WithEqualsRule_ShouldNotMatchLongerHeader()
    {
        var customerId = await CreateCustomerAsync("精确规则语义客户");
        await CreateColumnRuleAsync(customerId, "检查对象", targetField: 1, matchMode: 2);
        await CreateColumnRuleAsync(customerId, "管制条件", targetField: 2, matchMode: 2);
        await CreateColumnRuleAsync(customerId, "供应商回复", targetField: 3, matchMode: 2);
        await CreateColumnRuleAsync(customerId, "补充说明", targetField: 4, matchMode: 2);
        var fileId = await UploadExcelAsync(
            CreateLearningRuleExcelBytes("检查对象说明"),
            "smart-recognize-equals-rule.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("projectColumnIndex").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Recognize_WithRegexRule_ShouldMatchRegularExpression()
    {
        var customerId = await CreateCustomerAsync("正则规则语义客户");
        await CreateColumnRuleAsync(customerId, "检查.*对象", targetField: 1, matchMode: 3);
        await CreateColumnRuleAsync(customerId, "管制条件", targetField: 2, matchMode: 2);
        await CreateColumnRuleAsync(customerId, "供应商回复", targetField: 3, matchMode: 2);
        await CreateColumnRuleAsync(customerId, "补充说明", targetField: 4, matchMode: 2);
        var fileId = await UploadExcelAsync(
            CreateLearningRuleExcelBytes("检查设备对象"),
            "smart-recognize-regex-rule.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("projectColumnIndex").GetInt32().Should().Be(0);
    }

    private async Task<int> CreateCustomerAsync(string name)
    {
        var response = await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("id").GetInt32();
    }

    private async Task CreateColumnRuleAsync(
        int customerId,
        string pattern,
        int targetField,
        int matchMode = 2)
    {
        var response = await _client.PostAsync("/api/column-mapping-rules", ApiClientJson.ToJsonContent(new
        {
            pattern,
            targetField,
            matchMode,
            priority = 100,
            enabled = true,
            source = 3,
            customerId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateLearningRuleExcelBytes(string projectHeader = "检查对象")
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = projectHeader;
        worksheet.Cell(1, 2).Value = "管制条件";
        worksheet.Cell(1, 3).Value = "供应商回复";
        worksheet.Cell(1, 4).Value = "补充说明";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "表面不得有明显划伤";
        worksheet.Cell(2, 3).Value = "OK";
        worksheet.Cell(2, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

}

public class SmartConfigRecognizeMissingProjectApiTests : IClassFixture<MissingProjectColumnIntelligenceApiFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeMissingProjectApiTests(MissingProjectColumnIntelligenceApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenProjectColumnIsMissingButHeaderContainsProject_ShouldNeedConfirm()
    {
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-missing-project.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("projectColumnIndex").ValueKind.Should().Be(JsonValueKind.Null);
        table.GetProperty("isSpecificationOnly").GetBoolean().Should().BeFalse();
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
    }

    [Fact]
    public async Task Recognize_WhenUnmappedColumnHasProjectLikeSamples_ShouldNotAutoMarkSpecificationOnly()
    {
        var fileId = await UploadExcelAsync(
            CreateExcelWithUnmappedProjectSamplesBytes(),
            "smart-recognize-unmapped-project-samples.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("projectColumnIndex").ValueKind.Should().Be(JsonValueKind.Null);
        table.GetProperty("isSpecificationOnly").GetBoolean().Should().BeFalse();
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "验收标准";
        worksheet.Cell(1, 4).Value = "备注";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(2, 3).Value = "目视 OK";
        worksheet.Cell(2, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateExcelWithUnmappedProjectSamplesBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "分类";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "验收标准";
        worksheet.Cell(1, 4).Value = "备注";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "表面不得有明显划伤";
        worksheet.Cell(2, 3).Value = "目视 OK";
        worksheet.Cell(2, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class SmartConfigRecognizeLlmRequiredColumnsApiTests : IClassFixture<LlmFillsMissingRequiredColumnsApiFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeLlmRequiredColumnsApiTests(LlmFillsMissingRequiredColumnsApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenRuleMissesAcceptanceAndRemarkAndLlmFillsThem_ShouldReturnFusedAutoApply()
    {
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-llm-required-columns.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("source").GetString().Should().Be("Fused");
        table.GetProperty("projectColumnIndex").GetInt32().Should().Be(0);
        table.GetProperty("specificationColumnIndex").GetInt32().Should().Be(1);
        table.GetProperty("acceptanceColumnIndex").GetInt32().Should().Be(2);
        table.GetProperty("remarkColumnIndex").GetInt32().Should().Be(3);
        table.GetProperty("decision").GetString().Should().Be("AutoApply");
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "验收标准";
        worksheet.Cell(1, 4).Value = "备注";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(2, 3).Value = "目视 OK";
        worksheet.Cell(2, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class SmartConfigRecognizeLlmIncompleteApiTests : IClassFixture<LlmIncompleteRequiredColumnsApiFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeLlmIncompleteApiTests(LlmIncompleteRequiredColumnsApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenLlmStillMissesRequiredColumns_ShouldReturnFusedNeedConfirm()
    {
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-llm-incomplete.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("source").GetString().Should().Be("Fused");
        table.GetProperty("acceptanceColumnIndex").ValueKind.Should().Be(JsonValueKind.Null);
        table.GetProperty("remarkColumnIndex").ValueKind.Should().Be(JsonValueKind.Null);
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "验收标准";
        worksheet.Cell(1, 4).Value = "备注";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(2, 3).Value = "目视 OK";
        worksheet.Cell(2, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class SmartConfigRecognizeLlmTimeoutApiTests : IClassFixture<LlmStructureTimeoutApiFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeLlmTimeoutApiTests(LlmStructureTimeoutApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenLlmStructureAdjudicationHangs_ShouldReturnRuleResultAfterBusinessTimeout()
    {
        BlockingStructureAdjudicationService.Reset();
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-llm-timeout.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("source").GetString().Should().Be("RuleBased");
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
        BlockingStructureAdjudicationService.WasCancelled.Should().BeTrue("业务超时应取消挂起的 LLM 调用");
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "验收结果";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "目视 OK";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class SmartConfigRecognizeMultiHeaderApiTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeMultiHeaderApiTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenExcelHasTwoHeaderRows_ShouldReturnHeaderRowCountTwoAndDataStartAfterHeaders()
    {
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-multi-header.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("headerRowIndex").GetInt32().Should().Be(0);
        table.GetProperty("headerRowCount").GetInt32().Should().Be(2);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(2);
        table.GetProperty("specificationColumnIndex").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Recognize_WhenHeaderStartsAfterLeadingDescriptionRows_ShouldDetectFullHeaderBlock()
    {
        var fileId = await UploadExcelAsync(
            CreateLateMultiHeaderExcelBytes(),
            "smart-recognize-late-multi-header.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("headerRowIndex").GetInt32().Should().Be(4);
        table.GetProperty("headerRowCount").GetInt32().Should().Be(3);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(7);
        table.GetProperty("projectColumnIndex").GetInt32().Should().Be(0);
        table.GetProperty("specificationColumnIndex").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Recognize_WhenShortBusinessDescriptionPrecedesHeader_ShouldNotIncludeDescriptionAsHeader()
    {
        var fileId = await UploadExcelAsync(
            CreateShortBusinessDescriptionExcelBytes(),
            "smart-recognize-short-description-before-header.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("headerRowIndex").GetInt32().Should().Be(1);
        table.GetProperty("headerRowCount").GetInt32().Should().Be(1);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(2);
        table.GetProperty("headers").EnumerateArray()
            .Select(item => item.GetString())
            .Should()
            .NotContain(header => header != null && header.Contains("客户A"));
    }

    [Fact]
    public async Task Recognize_WhenAdditionalHeaderUsesCustomerDomainWords_ShouldIncludeItAsHeader()
    {
        var fileId = await UploadExcelAsync(
            CreateCustomerDomainMultiHeaderExcelBytes(),
            "smart-recognize-customer-domain-multi-header.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("headerRowIndex").GetInt32().Should().Be(0);
        table.GetProperty("headerRowCount").GetInt32().Should().Be(2);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(2);
        table.GetProperty("headers").EnumerateArray()
            .Select(item => item.GetString())
            .Should()
            .Contain(header => header == "基本信息 / 检查对象")
            .And.Contain(header => header == "判定依据 / 管制条件");
    }

    [Fact]
    public async Task Recognize_WhenAdditionalHeaderOnlyMatchesCustomerLearningWords_ShouldIncludeItAsHeader()
    {
        var customerId = await CreateCustomerAsync("表头学习词客户");
        await CreateColumnRuleAsync(customerId, "验货范围", targetField: 1);
        await CreateColumnRuleAsync(customerId, "承认条件", targetField: 2);
        await CreateColumnRuleAsync(customerId, "厂商回覆", targetField: 3);
        await CreateColumnRuleAsync(customerId, "附注", targetField: 4);
        var fileId = await UploadExcelAsync(
            CreateLearnedWordsMultiHeaderExcelBytes(),
            "smart-recognize-learned-words-multi-header.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("headerRowIndex").GetInt32().Should().Be(0);
        table.GetProperty("headerRowCount").GetInt32().Should().Be(2);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(2);
        table.GetProperty("headers").EnumerateArray()
            .Select(item => item.GetString())
            .Should()
            .Contain(header => header == "基本信息 / 验货范围")
            .And.Contain(header => header == "判定依据 / 承认条件");
    }

    [Fact]
    public async Task Recognize_WhenMultiHeaderExcelHasNoDataRows_ShouldReturnNullEndRow()
    {
        var fileId = await UploadExcelAsync(
            CreateMultiHeaderNoDataRowExcelBytes(),
            "smart-recognize-multi-header-no-data.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        var dataStartRowIndex = table.GetProperty("dataStartRowIndex").GetInt32();
        dataStartRowIndex.Should().BeGreaterThan(0);
        table.GetProperty("dataEndRowIndex").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private async Task<int> CreateCustomerAsync(string name)
    {
        var response = await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("id").GetInt32();
    }

    private async Task CreateColumnRuleAsync(int customerId, string pattern, int targetField)
    {
        var response = await _client.PostAsync("/api/column-mapping-rules", ApiClientJson.ToJsonContent(new
        {
            pattern,
            targetField,
            matchMode = 2,
            priority = 100,
            enabled = true,
            source = 3,
            customerId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "基本信息";
        worksheet.Cell(1, 2).Value = "判定依据";
        worksheet.Cell(1, 3).Value = "验收信息";
        worksheet.Cell(1, 4).Value = "验收信息";
        worksheet.Cell(2, 1).Value = "项目";
        worksheet.Cell(2, 2).Value = "规格";
        worksheet.Cell(2, 3).Value = "验收标准";
        worksheet.Cell(2, 4).Value = "备注";
        worksheet.Cell(3, 1).Value = "外观";
        worksheet.Cell(3, 2).Value = "无划伤";
        worksheet.Cell(3, 3).Value = "目视 OK";
        worksheet.Cell(3, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateShortBusinessDescriptionExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "客户A";
        worksheet.Cell(1, 2).Value = "机种X";
        worksheet.Cell(1, 3).Value = "版本B";
        worksheet.Cell(1, 4).Value = "量产";
        worksheet.Cell(2, 1).Value = "项目";
        worksheet.Cell(2, 2).Value = "规格";
        worksheet.Cell(2, 3).Value = "验收标准";
        worksheet.Cell(2, 4).Value = "备注";
        worksheet.Cell(3, 1).Value = "外观";
        worksheet.Cell(3, 2).Value = "无划伤";
        worksheet.Cell(3, 3).Value = "目视 OK";
        worksheet.Cell(3, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateCustomerDomainMultiHeaderExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "基本信息";
        worksheet.Cell(1, 2).Value = "判定依据";
        worksheet.Cell(1, 3).Value = "回复信息";
        worksheet.Cell(1, 4).Value = "回复信息";
        worksheet.Cell(2, 1).Value = "检查对象";
        worksheet.Cell(2, 2).Value = "管制条件";
        worksheet.Cell(2, 3).Value = "供应商确认";
        worksheet.Cell(2, 4).Value = "补充说明";
        worksheet.Cell(3, 1).Value = "外观";
        worksheet.Cell(3, 2).Value = "表面不得有明显划伤";
        worksheet.Cell(3, 3).Value = "OK";
        worksheet.Cell(3, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateLearnedWordsMultiHeaderExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "基本信息";
        worksheet.Cell(1, 2).Value = "判定依据";
        worksheet.Cell(1, 3).Value = "回复信息";
        worksheet.Cell(1, 4).Value = "回复信息";
        worksheet.Cell(2, 1).Value = "验货范围";
        worksheet.Cell(2, 2).Value = "承认条件";
        worksheet.Cell(2, 3).Value = "厂商回覆";
        worksheet.Cell(2, 4).Value = "附注";
        worksheet.Cell(3, 1).Value = "外观";
        worksheet.Cell(3, 2).Value = "表面不得有明显划伤";
        worksheet.Cell(3, 3).Value = "OK";
        worksheet.Cell(3, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateMultiHeaderNoDataRowExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Utility");
        worksheet.Cell(1, 1).Value = "设备类型";
        worksheet.Cell(1, 2).Value = "电力需求";
        worksheet.Cell(1, 3).Value = "电力需求";
        worksheet.Cell(1, 4).Value = "其它需求";
        worksheet.Cell(2, 1).Value = "设备名称";
        worksheet.Cell(2, 2).Value = "电压";
        worksheet.Cell(2, 3).Value = "紧急电功率";
        worksheet.Cell(2, 4).Value = "备注";
        worksheet.Cell(3, 1).Value = "设备名称";
        worksheet.Cell(3, 2).Value = "电压";
        worksheet.Cell(3, 3).Value = "紧急电功率";
        worksheet.Cell(3, 4).Value = "备注";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateLateMultiHeaderExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "客户：A";
        worksheet.Cell(2, 1).Value = "文件编号：QA-001";
        worksheet.Cell(3, 1).Value = "以下为验收规格";
        worksheet.Cell(4, 1).Value = "请按实际项目确认";
        worksheet.Cell(5, 1).Value = "基本信息";
        worksheet.Cell(5, 2).Value = "规格信息";
        worksheet.Cell(5, 3).Value = "验收信息";
        worksheet.Cell(5, 4).Value = "验收信息";
        worksheet.Cell(6, 1).Value = "分类";
        worksheet.Cell(6, 2).Value = "判定依据";
        worksheet.Cell(6, 3).Value = "执行方式";
        worksheet.Cell(6, 4).Value = "补充说明";
        worksheet.Cell(7, 1).Value = "项目";
        worksheet.Cell(7, 2).Value = "规格";
        worksheet.Cell(7, 3).Value = "验收标准";
        worksheet.Cell(7, 4).Value = "备注";
        worksheet.Cell(8, 1).Value = "外观";
        worksheet.Cell(8, 2).Value = "无划伤";
        worksheet.Cell(8, 3).Value = "目视 OK";
        worksheet.Cell(8, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class SmartConfigRecognizeWordHeaderApiTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeWordHeaderApiTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenWordHasCustomerDomainMultiHeader_ShouldReturnJoinedHeaders()
    {
        var fileId = await UploadWordAsync(
            CreateWordBytes([
                ["基本信息", "判定依据", "回复信息", "回复信息"],
                ["检查对象", "管制条件", "供应商确认", "补充说明"],
                ["外观", "表面不得有明显划伤", "OK", "抽检"]
            ]),
            "smart-recognize-word-customer-domain-multi-header.docx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));
        var responseText = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, responseText);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("headerRowIndex").GetInt32().Should().Be(0);
        table.GetProperty("headerRowCount").GetInt32().Should().Be(2);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(2);
        table.GetProperty("headers").EnumerateArray()
            .Select(item => item.GetString())
            .Should()
            .Contain(header => header == "基本信息 / 检查对象")
            .And.Contain(header => header == "判定依据 / 管制条件");
    }

    private async Task<int> UploadWordAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateWordBytes(string[][] rows)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body());

            var table = new Table();
            table.AppendChild(new TableProperties(new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));

            foreach (var row in rows)
            {
                var tableRow = new TableRow();
                foreach (var cell in row)
                {
                    tableRow.AppendChild(new TableCell(
                        new Paragraph(new Run(new Text(cell ?? string.Empty)))));
                }

                table.AppendChild(tableRow);
            }

            main.Document.Body!.Append(table);
            main.Document.Save();
        }

        return stream.ToArray();
    }
}

public class SmartConfigRecognizeLlmBudgetApiTests : IClassFixture<LlmStructureBudgetApiFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeLlmBudgetApiTests(LlmStructureBudgetApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenStructureAdjudicationBudgetIsOne_ShouldCallLlmAtMostOnce()
    {
        CountingStructureAdjudicationService.Reset();
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-llm-budget.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Data.GetProperty("tables").EnumerateArray().Should().HaveCount(2);
        CountingStructureAdjudicationService.CallCount.Should().Be(1);
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        for (var sheet = 1; sheet <= 2; sheet++)
        {
            var worksheet = workbook.AddWorksheet($"验收表{sheet}");
            worksheet.Cell(1, 1).Value = "项目";
            worksheet.Cell(1, 2).Value = "验收标准";
            worksheet.Cell(2, 1).Value = $"外观{sheet}";
            worksheet.Cell(2, 2).Value = "目视 OK";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class SmartConfigRecognizeLlmStructureCacheApiTests : IClassFixture<LlmStructureCacheApiFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeLlmStructureCacheApiTests(LlmStructureCacheApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenRepeatedHeaderShapesNeedStructureAdjudication_ShouldReuseStructureResultWithinDocument()
    {
        StructureCacheCountingStructureAdjudicationService.Reset();
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-llm-structure-cache.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Data.GetProperty("tables").EnumerateArray().Should().HaveCount(3);
        StructureCacheCountingStructureAdjudicationService.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Recognize_WhenStructureCacheReusesDifferentRowCounts_ShouldUseCurrentTableDataEnd()
    {
        using var factory = new LlmStructureCacheFusedRangeApiFactory();
        var client = factory.CreateClient();
        StructureCacheFusedRangeAdjudicationService.Reset();
        var fileId = await UploadExcelAsync(
            client,
            CreateRepeatedHeaderDifferentRowCountExcelBytes(),
            "smart-recognize-llm-structure-cache-range.xlsx");

        var response = await client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var tables = body.Data.GetProperty("tables").EnumerateArray().ToList();

        tables.Should().HaveCount(2);
        StructureCacheFusedRangeAdjudicationService.CallCount.Should().Be(1);
        tables[0].GetProperty("dataEndRowIndex").GetInt32().Should().Be(1);
        tables[1].GetProperty("dataEndRowIndex").GetInt32().Should().Be(3);
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        return await UploadExcelAsync(_client, bytes, fileName);
    }

    private static async Task<int> UploadExcelAsync(HttpClient client, byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        for (var sheet = 1; sheet <= 3; sheet++)
        {
            var worksheet = workbook.AddWorksheet($"验收表{sheet}");
            worksheet.Cell(1, 1).Value = "项目";
            worksheet.Cell(1, 2).Value = "验收标准";
            worksheet.Cell(2, 1).Value = $"外观{sheet}";
            worksheet.Cell(2, 2).Value = "目视 OK";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateRepeatedHeaderDifferentRowCountExcelBytes()
    {
        using var workbook = new XLWorkbook();
        for (var sheet = 1; sheet <= 2; sheet++)
        {
            var worksheet = workbook.AddWorksheet($"验收表{sheet}");
            worksheet.Cell(1, 1).Value = "项目";
            worksheet.Cell(1, 2).Value = "管控要求";
            worksheet.Cell(1, 3).Value = "验收结果";
            worksheet.Cell(1, 4).Value = "备注";
            var dataRows = sheet == 1 ? 1 : 3;
            for (var row = 0; row < dataRows; row++)
            {
                worksheet.Cell(row + 2, 1).Value = $"外观{sheet}-{row + 1}";
                worksheet.Cell(row + 2, 2).Value = "表面不得有明显划伤";
                worksheet.Cell(row + 2, 3).Value = "OK";
                worksheet.Cell(row + 2, 4).Value = "抽检";
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class SmartConfigRecognizeLlmSharedBudgetApiTests
{
    [Fact]
    public async Task Recognize_WhenGlobalLlmBudgetIsOne_ShouldShareAcrossRecallAndStructure()
    {
        using var factory = new LlmSharedBudgetApiFactory();
        var client = factory.CreateClient();
        SharedBudgetCountingColumnSemanticRecallService.Reset();
        SharedBudgetCountingStructureAdjudicationService.Reset();
        var fileId = await UploadExcelAsync(client, CreateSemanticAliasExcelBytes(), "smart-recognize-llm-shared-budget.xlsx");

        var response = await client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var totalLlmCalls =
            SharedBudgetCountingColumnSemanticRecallService.CallCount +
            SharedBudgetCountingStructureAdjudicationService.CallCount;
        SharedBudgetCountingColumnSemanticRecallService.CallCount.Should().Be(1);
        SharedBudgetCountingStructureAdjudicationService.CallCount.Should().Be(0);
        totalLlmCalls.Should().Be(1);
    }

    private static async Task<int> UploadExcelAsync(HttpClient client, byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateSemanticAliasExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "管控要求";
        worksheet.Cell(1, 3).Value = "验收结果";
        worksheet.Cell(1, 4).Value = "备注";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "表面不得有明显划伤";
        worksheet.Cell(2, 3).Value = "OK";
        worksheet.Cell(2, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class SmartConfigRecognizeLlmRoutingBudgetApiTests : IClassFixture<LlmRoutingBudgetApiFactory>
{
    private readonly HttpClient _client;

    public SmartConfigRecognizeLlmRoutingBudgetApiTests(LlmRoutingBudgetApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenFirstSheetIsQuotation_ShouldSpendLlmBudgetOnAcceptanceSheet()
    {
        RoutingBudgetRecordingStructureAdjudicationService.Reset();
        await CreateRoutingRuleAsync("报价预算测试规则", "Quotation", "Skip", "TableName", "Contains", "報價", 100);
        var fileId = await UploadExcelAsync(
            CreateQuotationThenAcceptanceExcelBytes(),
            "smart-recognize-llm-routing-budget.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        RoutingBudgetRecordingStructureAdjudicationService.LastRequest.Should().NotBeNull();
        var llmTables = JsonDocument.Parse(RoutingBudgetRecordingStructureAdjudicationService.LastRequest!.DocumentTablesJson)
            .RootElement
            .EnumerateArray()
            .ToList();
        llmTables.Should().ContainSingle();
        llmTables[0].GetProperty("tableName").GetString().Should().Be("验收表");
    }

    [Fact]
    public async Task Recognize_WhenFirstSheetIsLowValueUnknown_ShouldSpendLlmBudgetOnRecoverableAcceptanceSheet()
    {
        RoutingBudgetRecordingStructureAdjudicationService.Reset();
        var fileId = await UploadExcelAsync(
            CreateLowValueUnknownThenAcceptanceExcelBytes(),
            "smart-recognize-llm-low-value-budget.xlsx");

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        RoutingBudgetRecordingStructureAdjudicationService.LastRequest.Should().NotBeNull();
        var llmTables = JsonDocument.Parse(RoutingBudgetRecordingStructureAdjudicationService.LastRequest!.DocumentTablesJson)
            .RootElement
            .EnumerateArray()
            .ToList();
        llmTables.Should().ContainSingle();
        llmTables[0].GetProperty("tableName").GetString().Should().Be("验收表");
    }

    private async Task CreateRoutingRuleAsync(
        string name,
        string tableKind,
        string recommendation,
        string matchScope,
        string matchMode,
        string pattern,
        int priority)
    {
        var response = await _client.PostAsync("/api/smart-structure-routing-rules", ApiClientJson.ToJsonContent(new
        {
            name,
            tableKind,
            recommendation,
            matchScope,
            matchMode,
            pattern,
            priority,
            weight = 1.0,
            enabled = true,
            source = "Manual"
        }));
        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);
    }

    private async Task<int> UploadExcelAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync("/api/documents/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateQuotationThenAcceptanceExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var quotation = workbook.AddWorksheet("報價單");
        quotation.Cell(1, 1).Value = "品名";
        quotation.Cell(1, 2).Value = "单价";
        quotation.Cell(1, 3).Value = "数量";
        quotation.Cell(2, 1).Value = "投收板机";
        quotation.Cell(2, 2).Value = "100";
        quotation.Cell(2, 3).Value = "1";

        var acceptance = workbook.AddWorksheet("验收表");
        acceptance.Cell(1, 1).Value = "项目";
        acceptance.Cell(1, 2).Value = "验收标准";
        acceptance.Cell(2, 1).Value = "外观";
        acceptance.Cell(2, 2).Value = "目视 OK";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateLowValueUnknownThenAcceptanceExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var description = workbook.AddWorksheet("说明页");
        description.Cell(1, 1).Value = "版本";
        description.Cell(1, 2).Value = "负责人";
        description.Cell(2, 1).Value = "A";
        description.Cell(2, 2).Value = "张三";

        var acceptance = workbook.AddWorksheet("验收表");
        acceptance.Cell(1, 1).Value = "项目";
        acceptance.Cell(1, 2).Value = "验收标准";
        acceptance.Cell(2, 1).Value = "外观";
        acceptance.Cell(2, 2).Value = "目视 OK";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public class SmartConfigRecognizeColumnSemanticRecallApiTests
{
    [Fact]
    public async Task Recognize_WhenRuleMappingIsComplete_ShouldNotCallColumnSemanticRecall()
    {
        using var factory = new ColumnSemanticRecallCountingApiFactory();
        var client = factory.CreateClient();
        CountingColumnSemanticRecallService.Reset();
        var fileId = await UploadExcelAsync(client, CreateExcelBytes(), "smart-recognize-column-recall-complete.xlsx");

        var response = await client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();
        CountingColumnSemanticRecallService.CallCount.Should().Be(0);
        GetSemanticRecallSuggestions(table).Should().BeEmpty();
    }

    [Fact]
    public async Task Recognize_WhenSpecificationHeaderOnlyHasSemanticAlias_ShouldReturnSuggestionWithoutChangingRuleColumns()
    {
        using var factory = new ColumnSemanticRecallMissingSpecificationApiFactory();
        var client = factory.CreateClient();
        var fileId = await UploadExcelAsync(client, CreateSemanticAliasExcelBytes(), "smart-recognize-column-recall-spec.xlsx");

        var response = await client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();
        var suggestions = GetSemanticRecallSuggestions(table);

        table.GetProperty("specificationColumnIndex").ValueKind.Should().Be(JsonValueKind.Null);
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
        suggestions.Should().ContainSingle();
        var suggestion = suggestions.Single();
        suggestion.GetProperty("columnIndex").GetInt32().Should().Be(1);
        suggestion.GetProperty("header").GetString().Should().Be("管控要求");
        suggestion.GetProperty("targetField").GetString().Should().Be("Specification");
        suggestion.GetProperty("source").GetString().Should().Be("SemanticRecall");
        suggestion.GetProperty("confidence").GetDouble().Should().BeApproximately(0.88, 0.001);
    }

    [Fact]
    public async Task Recognize_WhenAcceptanceMethodAndConfirmationHeadersExist_ShouldNotSuggestMethodAsAcceptance()
    {
        using var factory = new ColumnSemanticRecallMissingAcceptanceApiFactory();
        var client = factory.CreateClient();
        var fileId = await UploadExcelAsync(client, CreateAcceptanceMethodExcelBytes(), "smart-recognize-column-recall-acceptance.xlsx");

        var response = await client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();
        var suggestions = GetSemanticRecallSuggestions(table);

        suggestions.Should().ContainSingle();
        suggestions.Single().GetProperty("header").GetString().Should().Be("确认结果");
        suggestions.Single().GetProperty("targetField").GetString().Should().Be("Acceptance");
        suggestions.Should().NotContain(item => item.GetProperty("header").GetString() == "验收方式");
        table.GetProperty("acceptanceColumnIndex").ValueKind.Should().Be(JsonValueKind.Null);
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
    }

    [Fact]
    public async Task Recognize_WhenColumnSemanticRecallFailsOrReturnsInvalidField_ShouldKeepRuleResult()
    {
        using var factory = new ColumnSemanticRecallInvalidResultApiFactory();
        var client = factory.CreateClient();
        var fileId = await UploadExcelAsync(client, CreateSemanticAliasExcelBytes(), "smart-recognize-column-recall-invalid.xlsx");

        var response = await client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("specificationColumnIndex").ValueKind.Should().Be(JsonValueKind.Null);
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
        GetSemanticRecallSuggestions(table).Should().BeEmpty();
    }

    [Fact]
    public async Task Recognize_WhenRepeatedHeaderShapesNeedSemanticRecall_ShouldReuseRecallResultWithinDocument()
    {
        using var factory = new ColumnSemanticRecallRepeatedHeaderApiFactory();
        var client = factory.CreateClient();
        CountingColumnSemanticRecallService.Reset();
        var fileId = await UploadExcelAsync(
            client,
            CreateRepeatedSemanticAliasExcelBytes(),
            "smart-recognize-column-recall-repeated.xlsx");

        var response = await client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Data.GetProperty("tables").EnumerateArray().Should().HaveCount(3);
        CountingColumnSemanticRecallService.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Recognize_WhenRepeatedHeaderSemanticRecallFails_ShouldNotRetrySameHeaderShape()
    {
        using var factory = new ColumnSemanticRecallFailingRepeatedHeaderApiFactory();
        var client = factory.CreateClient();
        FailingColumnSemanticRecallService.Reset();
        var fileId = await UploadExcelAsync(
            client,
            CreateRepeatedSemanticAliasExcelBytes(),
            "smart-recognize-column-recall-repeated-fail.xlsx");

        var response = await client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Data.GetProperty("tables").EnumerateArray().Should().HaveCount(3);
        FailingColumnSemanticRecallService.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Recognize_WhenColumnSemanticRecallHangs_ShouldUseIndependentRecallTimeout()
    {
        using var factory = new ColumnSemanticRecallTimeoutApiFactory();
        var client = factory.CreateClient();
        BlockingColumnSemanticRecallService.Reset();
        var fileId = await UploadExcelAsync(
            client,
            CreateSemanticAliasExcelBytes(),
            "smart-recognize-column-recall-timeout.xlsx");

        var response = await client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        BlockingColumnSemanticRecallService.WasCancelled.Should().BeTrue();
        BlockingColumnSemanticRecallService.CancelledAfter.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    private static IReadOnlyList<JsonElement> GetSemanticRecallSuggestions(JsonElement table)
    {
        return table.TryGetProperty("semanticRecallSuggestions", out var suggestions) &&
               suggestions.ValueKind == JsonValueKind.Array
            ? suggestions.EnumerateArray().ToList()
            : [];
    }

    private static async Task<int> UploadExcelAsync(HttpClient client, byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);

        var response = await client.PostAsync("/api/documents/upload", content);
        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);

        var json = JsonSerializer.Deserialize<ApiResponse<JsonElement>>(
            responseText,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return json.Data.GetProperty("fileId").GetInt32();
    }

    private static byte[] CreateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格内容";
        worksheet.Cell(1, 3).Value = "验收结果";
        worksheet.Cell(1, 4).Value = "备注";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(2, 3).Value = "OK";
        worksheet.Cell(2, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateSemanticAliasExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "管控要求";
        worksheet.Cell(1, 3).Value = "验收结果";
        worksheet.Cell(1, 4).Value = "备注";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "表面不得有明显划伤";
        worksheet.Cell(2, 3).Value = "OK";
        worksheet.Cell(2, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateAcceptanceMethodExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格内容";
        worksheet.Cell(1, 3).Value = "验收方式";
        worksheet.Cell(1, 4).Value = "确认结果";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "表面不得有明显划伤";
        worksheet.Cell(2, 3).Value = "目视";
        worksheet.Cell(2, 4).Value = "OK";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateRepeatedSemanticAliasExcelBytes()
    {
        using var workbook = new XLWorkbook();
        for (var sheetIndex = 1; sheetIndex <= 3; sheetIndex++)
        {
            var worksheet = workbook.AddWorksheet($"验收表{sheetIndex}");
            worksheet.Cell(1, 1).Value = "项目";
            worksheet.Cell(1, 2).Value = "管控要求";
            worksheet.Cell(1, 3).Value = "验收结果";
            worksheet.Cell(1, 4).Value = "备注";
            worksheet.Cell(2, 1).Value = $"外观{sheetIndex}";
            worksheet.Cell(2, 2).Value = "表面不得有明显划伤";
            worksheet.Cell(2, 3).Value = "OK";
            worksheet.Cell(2, 4).Value = "抽检";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

public sealed class MissingSpecificationColumnIntelligenceApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.AddScoped<IDocumentIntelligenceService, MissingSpecificationColumnIntelligenceService>();
        });
    }
}

public sealed class ColumnSemanticRecallCountingApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(ILlmColumnSemanticRecallService));
            services.AddScoped<ILlmColumnSemanticRecallService, CountingColumnSemanticRecallService>();
        });
    }
}

public sealed class ColumnSemanticRecallMissingSpecificationApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.RemoveAll(typeof(ILlmColumnSemanticRecallService));
            services.AddScoped<IDocumentIntelligenceService, MissingSpecificationForSemanticRecallIntelligenceService>();
            services.AddScoped<ILlmColumnSemanticRecallService, SpecificationColumnSemanticRecallService>();
        });
    }
}

public sealed class ColumnSemanticRecallMissingAcceptanceApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.RemoveAll(typeof(ILlmColumnSemanticRecallService));
            services.AddScoped<IDocumentIntelligenceService, MissingAcceptanceForSemanticRecallIntelligenceService>();
            services.AddScoped<ILlmColumnSemanticRecallService, AcceptanceColumnSemanticRecallService>();
        });
    }
}

public sealed class ColumnSemanticRecallInvalidResultApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.RemoveAll(typeof(ILlmColumnSemanticRecallService));
            services.AddScoped<IDocumentIntelligenceService, MissingSpecificationForSemanticRecallIntelligenceService>();
            services.AddScoped<ILlmColumnSemanticRecallService, InvalidColumnSemanticRecallService>();
        });
    }
}

public sealed class ColumnSemanticRecallRepeatedHeaderApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.RemoveAll(typeof(ILlmColumnSemanticRecallService));
            services.AddScoped<IDocumentIntelligenceService, MissingSpecificationForSemanticRecallIntelligenceService>();
            services.AddScoped<ILlmColumnSemanticRecallService, CountingColumnSemanticRecallService>();
        });
    }
}

public sealed class ColumnSemanticRecallFailingRepeatedHeaderApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.RemoveAll(typeof(ILlmColumnSemanticRecallService));
            services.AddScoped<IDocumentIntelligenceService, MissingSpecificationForSemanticRecallIntelligenceService>();
            services.AddScoped<ILlmColumnSemanticRecallService, FailingColumnSemanticRecallService>();
        });
    }
}

public sealed class ColumnSemanticRecallTimeoutApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SmartConfiguration:StructureAdjudicationTimeoutSeconds"] = "3",
                ["SmartConfiguration:ColumnSemanticRecallTimeoutSeconds"] = "1",
                ["SmartConfiguration:MaxStructureAdjudicationCallsPerDocument"] = "0"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.RemoveAll(typeof(ILlmColumnSemanticRecallService));
            services.AddScoped<IDocumentIntelligenceService, MissingSpecificationForSemanticRecallIntelligenceService>();
            services.AddScoped<ILlmColumnSemanticRecallService, BlockingColumnSemanticRecallService>();
        });
    }
}

public sealed class LowConfidenceCompleteMappingApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SmartConfiguration:MaxStructureAdjudicationCallsPerDocument"] = "0"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.AddScoped<IDocumentIntelligenceService, LowConfidenceCompleteMappingIntelligenceService>();
            services.RemoveAll(typeof(ILlmDocumentStructureAdjudicationService));
            services.AddScoped<ILlmDocumentStructureAdjudicationService, ZeroBudgetCountingStructureAdjudicationService>();
        });
    }
}

public sealed class LlmStructureTimeoutApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SmartConfiguration:StructureAdjudicationTimeoutSeconds"] = "1"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.RemoveAll(typeof(ILlmDocumentStructureAdjudicationService));
            services.AddScoped<IDocumentIntelligenceService, MissingSpecificationColumnIntelligenceService>();
            services.AddScoped<ILlmDocumentStructureAdjudicationService, BlockingStructureAdjudicationService>();
        });
    }
}

public sealed class LlmStructureBudgetApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SmartConfiguration:MaxStructureAdjudicationCallsPerDocument"] = "1"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.AddScoped<IDocumentIntelligenceService, MissingSpecificationColumnIntelligenceService>();
            services.RemoveAll(typeof(ILlmDocumentStructureAdjudicationService));
            services.AddScoped<ILlmDocumentStructureAdjudicationService, CountingStructureAdjudicationService>();
        });
    }
}

public sealed class LlmStructureCacheApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.AddScoped<IDocumentIntelligenceService, MissingSpecificationColumnIntelligenceService>();
            services.RemoveAll(typeof(ILlmDocumentStructureAdjudicationService));
            services.AddScoped<ILlmDocumentStructureAdjudicationService, StructureCacheCountingStructureAdjudicationService>();
        });
    }
}

public sealed class LlmStructureCacheFusedRangeApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.AddScoped<IDocumentIntelligenceService, FusableMissingSpecificationColumnIntelligenceService>();
            services.RemoveAll(typeof(ILlmDocumentStructureAdjudicationService));
            services.AddScoped<ILlmDocumentStructureAdjudicationService, StructureCacheFusedRangeAdjudicationService>();
        });
    }
}

public sealed class LlmSharedBudgetApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SmartConfiguration:MaxLlmCallsPerRecognizeDocument"] = "1",
                ["SmartConfiguration:MaxStructureAdjudicationCallsPerDocument"] = "5",
                ["SmartConfiguration:MaxColumnSemanticRecallCallsPerDocument"] = "5"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.RemoveAll(typeof(ILlmDocumentStructureAdjudicationService));
            services.RemoveAll(typeof(ILlmColumnSemanticRecallService));
            services.AddScoped<IDocumentIntelligenceService, MissingSpecificationForSemanticRecallIntelligenceService>();
            services.AddScoped<ILlmDocumentStructureAdjudicationService, SharedBudgetCountingStructureAdjudicationService>();
            services.AddScoped<ILlmColumnSemanticRecallService, SharedBudgetCountingColumnSemanticRecallService>();
        });
    }
}

public sealed class LlmRoutingBudgetApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SmartConfiguration:MaxStructureAdjudicationCallsPerDocument"] = "1"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.AddScoped<IDocumentIntelligenceService, MissingSpecificationColumnIntelligenceService>();
            services.RemoveAll(typeof(ILlmDocumentStructureAdjudicationService));
            services.AddScoped<ILlmDocumentStructureAdjudicationService, RoutingBudgetRecordingStructureAdjudicationService>();
        });
    }
}

public sealed class MissingProjectColumnIntelligenceApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.AddScoped<IDocumentIntelligenceService, MissingProjectColumnIntelligenceService>();
        });
    }
}

public sealed class LlmFillsMissingSpecificationApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.RemoveAll(typeof(ILlmDocumentStructureAdjudicationService));
            services.AddScoped<IDocumentIntelligenceService, FusableMissingSpecificationColumnIntelligenceService>();
            services.AddScoped<ILlmDocumentStructureAdjudicationService, FillSpecificationColumnStructureAdjudicationService>();
        });
    }
}

public sealed class LlmFillsMissingSpecificationWithSemanticRecallApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.RemoveAll(typeof(ILlmDocumentStructureAdjudicationService));
            services.RemoveAll(typeof(ILlmColumnSemanticRecallService));
            services.AddScoped<IDocumentIntelligenceService, FusableMissingSpecificationColumnIntelligenceService>();
            services.AddScoped<ILlmDocumentStructureAdjudicationService, FillSpecificationColumnStructureAdjudicationService>();
            services.AddScoped<ILlmColumnSemanticRecallService, SpecificationColumnSemanticRecallService>();
        });
    }
}

public sealed class LlmCorrectsHeaderStructureApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.RemoveAll(typeof(ILlmDocumentStructureAdjudicationService));
            services.AddScoped<IDocumentIntelligenceService, LowConfidenceWrongHeaderIntelligenceService>();
            services.AddScoped<ILlmDocumentStructureAdjudicationService, HeaderCorrectionStructureAdjudicationService>();
        });
    }
}

public sealed class LlmInvalidHeaderStructureApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.RemoveAll(typeof(ILlmDocumentStructureAdjudicationService));
            services.AddScoped<IDocumentIntelligenceService, LowConfidenceWrongHeaderIntelligenceService>();
            services.AddScoped<ILlmDocumentStructureAdjudicationService, InvalidHeaderStructureAdjudicationService>();
        });
    }
}

public sealed class LlmRecordingHistoryFewShotApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.RemoveAll(typeof(ILlmDocumentStructureAdjudicationService));
            services.AddScoped<IDocumentIntelligenceService, FusableMissingSpecificationColumnIntelligenceService>();
            services.AddScoped<ILlmDocumentStructureAdjudicationService, RecordingStructureAdjudicationService>();
        });
    }
}

public sealed class LlmOffsetHeaderRecordingApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.RemoveAll(typeof(ILlmDocumentStructureAdjudicationService));
            services.AddScoped<IDocumentIntelligenceService, OffsetHeaderMissingSpecificationColumnIntelligenceService>();
            services.AddScoped<ILlmDocumentStructureAdjudicationService, OffsetHeaderRecordingStructureAdjudicationService>();
        });
    }
}

public sealed class SpecificationOnlyIntelligenceApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.AddScoped<IDocumentIntelligenceService, SpecificationOnlyIntelligenceService>();
        });
    }
}

public sealed class LlmFillsMissingRequiredColumnsApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.RemoveAll(typeof(ILlmDocumentStructureAdjudicationService));
            services.AddScoped<IDocumentIntelligenceService, MissingAcceptanceAndRemarkColumnIntelligenceService>();
            services.AddScoped<ILlmDocumentStructureAdjudicationService, FillRequiredColumnsStructureAdjudicationService>();
        });
    }
}

public sealed class LlmIncompleteRequiredColumnsApiFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IDocumentIntelligenceService));
            services.RemoveAll(typeof(ILlmDocumentStructureAdjudicationService));
            services.AddScoped<IDocumentIntelligenceService, MissingAcceptanceAndRemarkColumnIntelligenceService>();
            services.AddScoped<ILlmDocumentStructureAdjudicationService, IncompleteRequiredColumnsStructureAdjudicationService>();
        });
    }
}

public sealed class BlockingStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    public static bool WasCancelled { get; private set; }

    public static void Reset()
    {
        WasCancelled = false;
    }

    public async Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WasCancelled = true;
            throw;
        }
    }
}

public sealed class CountingStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    private static int _callCount;

    public static int CallCount => _callCount;

    public static void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
    }

    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(null);
    }
}

public sealed class StructureCacheCountingStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    private static int _callCount;

    public static int CallCount => _callCount;

    public static void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
    }

    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(null);
    }
}

public sealed class StructureCacheFusedRangeAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    private static int _callCount;

    public static int CallCount => _callCount;

    public static void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
    }

    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        var tableIndex = request.RuleCandidates.First().TableIndex;
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(new LlmDocumentStructureAdjudicationResult
        {
            Confidence = 0.92,
            Decision = "autoApply",
            Reason = "测试替身补出规格列并触发融合缓存",
            Tables =
            [
                new DocumentStructureCandidate
                {
                    TableIndex = tableIndex,
                    SpecificationColumnIndex = 1,
                    Confidence = 0.92,
                    Source = DocumentStructureCandidateSource.Llm
                }
            ]
        });
    }
}

public sealed class SharedBudgetCountingStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    private static int _callCount;

    public static int CallCount => _callCount;

    public static void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
    }

    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(null);
    }
}

public sealed class ZeroBudgetCountingStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    private static int _callCount;

    public static int CallCount => _callCount;

    public static void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
    }

    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(null);
    }
}

public sealed class CountingColumnSemanticRecallService : ILlmColumnSemanticRecallService
{
    private static int _callCount;

    public static int CallCount => _callCount;

    public static void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
    }

    public Task<LlmColumnSemanticRecallResult?> RecallAsync(
        LlmColumnSemanticRecallRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult<LlmColumnSemanticRecallResult?>(new LlmColumnSemanticRecallResult());
    }
}

public sealed class SharedBudgetCountingColumnSemanticRecallService : ILlmColumnSemanticRecallService
{
    private static int _callCount;

    public static int CallCount => _callCount;

    public static void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
    }

    public Task<LlmColumnSemanticRecallResult?> RecallAsync(
        LlmColumnSemanticRecallRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult<LlmColumnSemanticRecallResult?>(new LlmColumnSemanticRecallResult());
    }
}

public sealed class FailingColumnSemanticRecallService : ILlmColumnSemanticRecallService
{
    private static int _callCount;

    public static int CallCount => _callCount;

    public static void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
    }

    public Task<LlmColumnSemanticRecallResult?> RecallAsync(
        LlmColumnSemanticRecallRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        throw new InvalidOperationException("测试替身模拟列语义召回失败");
    }
}

public sealed class BlockingColumnSemanticRecallService : ILlmColumnSemanticRecallService
{
    public static bool WasCancelled { get; private set; }

    public static TimeSpan CancelledAfter { get; private set; }

    public static void Reset()
    {
        WasCancelled = false;
        CancelledAfter = TimeSpan.Zero;
    }

    public async Task<LlmColumnSemanticRecallResult?> RecallAsync(
        LlmColumnSemanticRecallRequest request,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WasCancelled = true;
            CancelledAfter = Stopwatch.GetElapsedTime(startedAt);
            throw;
        }
    }
}

public sealed class SpecificationColumnSemanticRecallService : ILlmColumnSemanticRecallService
{
    public Task<LlmColumnSemanticRecallResult?> RecallAsync(
        LlmColumnSemanticRecallRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LlmColumnSemanticRecallResult?>(new LlmColumnSemanticRecallResult
        {
            Suggestions =
            [
                new LlmColumnSemanticRecallSuggestion
                {
                    ColumnIndex = 1,
                    Header = "管控要求",
                    TargetField = "Specification",
                    Confidence = 0.88,
                    Reason = "表头表示规格约束要求",
                    Source = "SemanticRecall"
                },
                new LlmColumnSemanticRecallSuggestion
                {
                    ColumnIndex = 1,
                    Header = "管控要求",
                    TargetField = "Unknown",
                    Confidence = 0.72,
                    Reason = "同列低置信度冲突建议应被丢弃",
                    Source = "SemanticRecall"
                }
            ]
        });
    }
}

public sealed class AcceptanceColumnSemanticRecallService : ILlmColumnSemanticRecallService
{
    public Task<LlmColumnSemanticRecallResult?> RecallAsync(
        LlmColumnSemanticRecallRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LlmColumnSemanticRecallResult?>(new LlmColumnSemanticRecallResult
        {
            Suggestions =
            [
                new LlmColumnSemanticRecallSuggestion
                {
                    ColumnIndex = 2,
                    Header = "验收方式",
                    TargetField = "Acceptance",
                    Confidence = 0.91,
                    Reason = "测试替身故意返回方法列",
                    Source = "SemanticRecall"
                },
                new LlmColumnSemanticRecallSuggestion
                {
                    ColumnIndex = 3,
                    Header = "确认结果",
                    TargetField = "Acceptance",
                    Confidence = 0.89,
                    Reason = "表头表示供应商确认结果",
                    Source = "SemanticRecall"
                }
            ]
        });
    }
}

public sealed class InvalidColumnSemanticRecallService : ILlmColumnSemanticRecallService
{
    public Task<LlmColumnSemanticRecallResult?> RecallAsync(
        LlmColumnSemanticRecallRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LlmColumnSemanticRecallResult?>(new LlmColumnSemanticRecallResult
        {
            Suggestions =
            [
                new LlmColumnSemanticRecallSuggestion
                {
                    ColumnIndex = 1,
                    Header = "管控要求",
                    TargetField = "MadeUpField",
                    Confidence = 0.95,
                    Reason = "非法字段应被丢弃",
                    Source = "SemanticRecall"
                }
            ]
        });
    }
}

public sealed class HeaderCorrectionStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    private static int _callCount;

    public static int CallCount => _callCount;

    public static void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
    }

    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(new LlmDocumentStructureAdjudicationResult
        {
            Confidence = 0.94,
            Decision = "autoApply",
            Reason = "测试替身修正表头行",
            Tables =
            [
                new DocumentStructureCandidate
                {
                    TableIndex = 0,
                    HeaderRowIndex = 1,
                    HeaderRowCount = 1,
                    DataStartRowIndex = 2,
                    ProjectColumnIndex = 0,
                    SpecificationColumnIndex = 1,
                    AcceptanceColumnIndex = 2,
                    RemarkColumnIndex = 3,
                    Confidence = 0.94,
                    Source = DocumentStructureCandidateSource.Llm
                }
            ]
        });
    }
}

public sealed class InvalidHeaderStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    private static int _callCount;

    public static int CallCount => _callCount;

    public static void Reset()
    {
        Interlocked.Exchange(ref _callCount, 0);
    }

    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(new LlmDocumentStructureAdjudicationResult
        {
            Confidence = 0.95,
            Decision = "autoApply",
            Reason = "测试替身返回非法表头行",
            Tables =
            [
                new DocumentStructureCandidate
                {
                    TableIndex = 0,
                    HeaderRowIndex = 99,
                    HeaderRowCount = 1,
                    DataStartRowIndex = 100,
                    ProjectColumnIndex = 0,
                    SpecificationColumnIndex = 1,
                    AcceptanceColumnIndex = 2,
                    RemarkColumnIndex = 3,
                    Confidence = 0.95,
                    Source = DocumentStructureCandidateSource.Llm
                }
            ]
        });
    }
}

public sealed class RecordingStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    public static LlmDocumentStructureAdjudicationRequest? LastRequest { get; private set; }

    public static void Reset()
    {
        LastRequest = null;
    }

    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(new LlmDocumentStructureAdjudicationResult
        {
            Confidence = 0.92,
            Decision = "autoApply",
            Reason = "测试替身记录历史案例",
            Tables =
            [
                new DocumentStructureCandidate
                {
                    TableIndex = 0,
                    ProjectColumnIndex = 0,
                    SpecificationColumnIndex = 1,
                    AcceptanceColumnIndex = 2,
                    RemarkColumnIndex = 3,
                    Confidence = 0.92,
                    Source = DocumentStructureCandidateSource.Llm
                }
            ]
        });
    }
}

public sealed class RoutingBudgetRecordingStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    public static LlmDocumentStructureAdjudicationRequest? LastRequest { get; private set; }

    public static void Reset()
    {
        LastRequest = null;
    }

    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(null);
    }
}

public sealed class OffsetHeaderRecordingStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    public static LlmDocumentStructureAdjudicationRequest? LastRequest { get; private set; }

    public static void Reset()
    {
        LastRequest = null;
    }

    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(new LlmDocumentStructureAdjudicationResult
        {
            Confidence = 0.92,
            Decision = "autoApply",
            Reason = "测试替身记录带前导说明行的坐标",
            Tables =
            [
                new DocumentStructureCandidate
                {
                    TableIndex = 0,
                    ProjectColumnIndex = 0,
                    SpecificationColumnIndex = 1,
                    AcceptanceColumnIndex = 2,
                    RemarkColumnIndex = 3,
                    Confidence = 0.92,
                    Source = DocumentStructureCandidateSource.Llm
                }
            ]
        });
    }
}

public sealed class MissingAcceptanceAndRemarkColumnIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TableIdentificationResult
        {
            TableIndex = 0,
            Confidence = 1,
            Reasoning = "测试替身"
        });
    }

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.96,
            Reasoning = "高置信但缺验收列和备注列",
            Mapping = new ColumnMapping
            {
                ProjectColumn = 0,
                SpecificationColumn = 1,
                AcceptanceColumn = null,
                RemarkColumn = null,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            }
        });
    }

    public int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null) => 0;
}

public sealed class LowConfidenceCompleteMappingIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new TableIdentificationResult { TableIndex = 0, Confidence = 1 });

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.6,
            Mapping = new ColumnMapping
            {
                ProjectColumn = 0,
                SpecificationColumn = 1,
                AcceptanceColumn = 2,
                RemarkColumn = 3,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            }
        });
    }

    public int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null) => 0;
}

public sealed class LowConfidenceWrongHeaderIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TableIdentificationResult
        {
            TableIndex = 0,
            Confidence = 0.5
        });
    }

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.4,
            Details =
            [
                new ColumnIdentificationResult { ColumnIndex = 0, ColumnType = ColumnType.Project, Confidence = 0.4 },
                new ColumnIdentificationResult { ColumnIndex = 1, ColumnType = ColumnType.Specification, Confidence = 0.4 },
                new ColumnIdentificationResult { ColumnIndex = 2, ColumnType = ColumnType.Acceptance, Confidence = 0.4 },
                new ColumnIdentificationResult { ColumnIndex = 3, ColumnType = ColumnType.Remark, Confidence = 0.4 }
            ],
            Mapping = new ColumnMapping
            {
                ProjectColumn = 0,
                SpecificationColumn = 1,
                AcceptanceColumn = 2,
                RemarkColumn = 3,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            }
        });
    }

    public int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null) => 0;
}

public sealed class MissingProjectColumnIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new TableIdentificationResult { TableIndex = 0, Confidence = 1 });

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.96,
            Mapping = new ColumnMapping
            {
                SpecificationColumn = 1,
                AcceptanceColumn = 2,
                RemarkColumn = 3,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            }
        });
    }

    public int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null) => 0;
}

public sealed class MissingSpecificationColumnIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TableIdentificationResult
        {
            TableIndex = 0,
            Confidence = 1,
            Reasoning = "测试替身"
        });
    }

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.96,
            Reasoning = "高置信但缺规格列",
            Mapping = new ColumnMapping
            {
                ProjectColumn = 0,
                SpecificationColumn = null,
                AcceptanceColumn = 1,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            }
        });
    }

    public int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null) => 0;
}

public sealed class MissingSpecificationForSemanticRecallIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TableIdentificationResult
        {
            TableIndex = 0,
            Confidence = 1,
            Reasoning = "测试替身"
        });
    }

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.72,
            Reasoning = "缺规格列以触发列语义召回",
            Mapping = new ColumnMapping
            {
                ProjectColumn = 0,
                SpecificationColumn = null,
                AcceptanceColumn = 2,
                RemarkColumn = 3,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            },
            Details =
            [
                new ColumnIdentificationResult { ColumnIndex = 0, HeaderText = "项目", ColumnType = ColumnType.Project, Confidence = 0.95 },
                new ColumnIdentificationResult { ColumnIndex = 1, HeaderText = "管控要求", ColumnType = ColumnType.Unknown, Confidence = 0 },
                new ColumnIdentificationResult { ColumnIndex = 2, HeaderText = "验收结果", ColumnType = ColumnType.Acceptance, Confidence = 0.95 },
                new ColumnIdentificationResult { ColumnIndex = 3, HeaderText = "备注", ColumnType = ColumnType.Remark, Confidence = 0.95 }
            ]
        });
    }

    public int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null) => 0;
}

public sealed class MissingAcceptanceForSemanticRecallIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TableIdentificationResult
        {
            TableIndex = 0,
            Confidence = 1,
            Reasoning = "测试替身"
        });
    }

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.70,
            Reasoning = "缺验收结果列以触发列语义召回",
            Mapping = new ColumnMapping
            {
                ProjectColumn = 0,
                SpecificationColumn = 1,
                AcceptanceColumn = null,
                RemarkColumn = null,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            },
            Details =
            [
                new ColumnIdentificationResult { ColumnIndex = 0, HeaderText = "项目", ColumnType = ColumnType.Project, Confidence = 0.95 },
                new ColumnIdentificationResult { ColumnIndex = 1, HeaderText = "规格内容", ColumnType = ColumnType.Specification, Confidence = 0.95 },
                new ColumnIdentificationResult { ColumnIndex = 2, HeaderText = "验收方式", ColumnType = ColumnType.Unknown, Confidence = 0 },
                new ColumnIdentificationResult { ColumnIndex = 3, HeaderText = "确认结果", ColumnType = ColumnType.Unknown, Confidence = 0 }
            ]
        });
    }

    public int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null) => 0;
}

public sealed class FusableMissingSpecificationColumnIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TableIdentificationResult
        {
            TableIndex = 0,
            Confidence = 1,
            Reasoning = "测试替身"
        });
    }

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.96,
            Reasoning = "高置信但缺规格列，验收列已确定",
            Mapping = new ColumnMapping
            {
                ProjectColumn = 0,
                SpecificationColumn = null,
                AcceptanceColumn = 2,
                RemarkColumn = 3,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            }
        });
    }

    public int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null) => 0;
}

public sealed class OffsetHeaderMissingSpecificationColumnIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TableIdentificationResult
        {
            TableIndex = 0,
            Confidence = 1,
            Reasoning = "测试替身"
        });
    }

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.96,
            Reasoning = "表头存在前导说明行，缺规格列以触发 LLM 裁决",
            Mapping = new ColumnMapping
            {
                ProjectColumn = 0,
                SpecificationColumn = null,
                AcceptanceColumn = 2,
                RemarkColumn = 3,
                HeaderRowIndex = 1,
                HeaderRowCount = 1,
                DataStartRowIndex = 2
            }
        });
    }

    public int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null) => 1;
}

public sealed class SpecificationOnlyIntelligenceService : IDocumentIntelligenceService
{
    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TableIdentificationResult
        {
            TableIndex = 0,
            Confidence = 1,
            Reasoning = "测试替身"
        });
    }

    public Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ColumnMappingResult
        {
            Confidence = 0.96,
            Reasoning = "高置信仅规格结构",
            Mapping = new ColumnMapping
            {
                ProjectColumn = null,
                SpecificationColumn = 0,
                AcceptanceColumn = 1,
                RemarkColumn = 2,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            }
        });
    }

    public int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null) => 0;
}

public sealed class FillSpecificationColumnStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(new LlmDocumentStructureAdjudicationResult
        {
            Confidence = 0.92,
            Decision = "autoApply",
            Reason = "测试替身补出规格列",
            Tables =
            [
                new DocumentStructureCandidate
                {
                    TableIndex = 0,
                    SpecificationColumnIndex = 1,
                    Confidence = 0.92,
                    Source = DocumentStructureCandidateSource.Llm
                }
            ]
        });
    }
}

public sealed class FillRequiredColumnsStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(new LlmDocumentStructureAdjudicationResult
        {
            Confidence = 0.93,
            Decision = "autoApply",
            Reason = "测试替身补齐导入必填列",
            Tables =
            [
                new DocumentStructureCandidate
                {
                    TableIndex = 0,
                    ProjectColumnIndex = 0,
                    SpecificationColumnIndex = 1,
                    AcceptanceColumnIndex = 2,
                    RemarkColumnIndex = 3,
                    Confidence = 0.93,
                    Source = DocumentStructureCandidateSource.Llm
                }
            ]
        });
    }
}

public sealed class IncompleteRequiredColumnsStructureAdjudicationService : ILlmDocumentStructureAdjudicationService
{
    public Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LlmDocumentStructureAdjudicationResult?>(new LlmDocumentStructureAdjudicationResult
        {
            Confidence = 0.91,
            Decision = "needConfirm",
            Reason = "测试替身仍无法确认验收列和备注列",
            Tables =
            [
                new DocumentStructureCandidate
                {
                    TableIndex = 0,
                    ProjectColumnIndex = 0,
                    SpecificationColumnIndex = 1,
                    Confidence = 0.91,
                    Source = DocumentStructureCandidateSource.Llm
                }
            ]
        });
    }
}
