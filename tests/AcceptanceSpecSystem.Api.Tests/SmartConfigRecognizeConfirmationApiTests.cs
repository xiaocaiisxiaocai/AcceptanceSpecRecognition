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
        await CreateColumnRuleAsync(customerId, "甲类专栏", targetField: 1, matchMode: 2);
        await CreateColumnRuleAsync(customerId, "管制条件", targetField: 2, matchMode: 2);
        await CreateColumnRuleAsync(customerId, "供应商回复", targetField: 3, matchMode: 2);
        await CreateColumnRuleAsync(customerId, "补充说明", targetField: 4, matchMode: 2);
        var fileId = await UploadExcelAsync(
            CreateLearningRuleExcelBytes("甲类专栏扩展"),
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

    private Task<int> UploadExcelAsync(byte[] bytes, string fileName) =>
        SmartConfigRecognizeTestFiles.UploadExcelAsync(_client, bytes, fileName);

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

    private Task<int> UploadExcelAsync(byte[] bytes, string fileName) =>
        SmartConfigRecognizeTestFiles.UploadExcelAsync(_client, bytes, fileName);

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
            fileId,
            enableLlmAssistance = true,
            llmServiceId = 321
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

    private Task<int> UploadExcelAsync(byte[] bytes, string fileName) =>
        SmartConfigRecognizeTestFiles.UploadExcelAsync(_client, bytes, fileName);

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
            fileId,
            enableLlmAssistance = true,
            llmServiceId = 321
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("source").GetString().Should().Be("Fused");
        table.GetProperty("acceptanceColumnIndex").ValueKind.Should().Be(JsonValueKind.Null);
        table.GetProperty("remarkColumnIndex").ValueKind.Should().Be(JsonValueKind.Null);
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
    }

    private Task<int> UploadExcelAsync(byte[] bytes, string fileName) =>
        SmartConfigRecognizeTestFiles.UploadExcelAsync(_client, bytes, fileName);

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
            fileId,
            enableLlmAssistance = true,
            llmServiceId = 321
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();

        table.GetProperty("source").GetString().Should().Be("RuleBased");
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
        BlockingStructureAdjudicationService.WasCancelled.Should().BeTrue("业务超时应取消挂起的 LLM 调用");
    }

    private Task<int> UploadExcelAsync(byte[] bytes, string fileName) =>
        SmartConfigRecognizeTestFiles.UploadExcelAsync(_client, bytes, fileName);

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
