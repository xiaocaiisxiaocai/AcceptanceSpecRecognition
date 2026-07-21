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

    private Task<int> UploadExcelAsync(byte[] bytes, string fileName) =>
        SmartConfigRecognizeTestFiles.UploadExcelAsync(_client, bytes, fileName);

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
            fileId,
            enableLlmAssistance = true,
            llmServiceId = 321
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
            fileId,
            enableLlmAssistance = true,
            llmServiceId = 321
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
        SmartConfigRecognizeTestFiles.UploadExcelAsync(_client, bytes, fileName);

    private static Task<int> UploadExcelAsync(HttpClient client, byte[] bytes, string fileName) =>
        SmartConfigRecognizeTestFiles.UploadExcelAsync(client, bytes, fileName);

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
            fileId,
            enableLlmAssistance = true,
            llmServiceId = 321
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
            fileId,
            enableLlmAssistance = true,
            llmServiceId = 321
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

    private Task<int> UploadExcelAsync(byte[] bytes, string fileName) =>
        SmartConfigRecognizeTestFiles.UploadExcelAsync(_client, bytes, fileName);

    private Task<int> UploadWordAsync(byte[] bytes, string fileName) =>
        SmartConfigRecognizeTestFiles.UploadWordAsync(_client, bytes, fileName);

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

    private static byte[] CreateWordBytes(string[][] rows) =>
        SmartConfigRecognizeTestFiles.CreateWordBytes(rows);
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
            fileId,
            enableLlmAssistance = true,
            llmServiceId = 321
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

    private Task<int> UploadExcelAsync(byte[] bytes, string fileName) =>
        SmartConfigRecognizeTestFiles.UploadExcelAsync(_client, bytes, fileName);

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
