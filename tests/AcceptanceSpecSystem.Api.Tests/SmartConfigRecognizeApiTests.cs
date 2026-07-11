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
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-template.xlsx");
        await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            fileId,
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
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-recognize-template-health.xlsx");
        await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            fileId,
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
            dataEndRowIndex = 1,
            isSpecificationOnly = false,
            learnedColumns = Array.Empty<object>()
        }));
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
        var fileId = await UploadExcelAsync(CreateExcelBytes(), "smart-confirm-routing.xlsx");

        var response = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            fileId,
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
        var fileId = await SmartConfigRecognizeTestFiles.UploadWordAsync(
            _client,
            SmartConfigRecognizeTestFiles.CreateWordBytes(
            [
                ["检查项目", "规格要求", "判定标准"],
                ["外观", "无划伤", "OK"]
            ]),
            "smart-confirm-word-routing.docx");

        var response = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            fileId,
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

    private Task<int> UploadExcelAsync(byte[] bytes, string fileName) =>
        SmartConfigRecognizeTestFiles.UploadExcelAsync(_client, bytes, fileName);

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
