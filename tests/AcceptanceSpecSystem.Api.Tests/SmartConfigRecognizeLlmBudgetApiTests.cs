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

    private Task<int> UploadExcelAsync(byte[] bytes, string fileName) =>
        SmartConfigRecognizeTestFiles.UploadExcelAsync(_client, bytes, fileName);

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

    private Task<int> UploadExcelAsync(byte[] bytes, string fileName) =>
        SmartConfigRecognizeTestFiles.UploadExcelAsync(_client, bytes, fileName);

    private static Task<int> UploadExcelAsync(HttpClient client, byte[] bytes, string fileName) =>
        SmartConfigRecognizeTestFiles.UploadExcelAsync(client, bytes, fileName);

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

    private static Task<int> UploadExcelAsync(HttpClient client, byte[] bytes, string fileName) =>
        SmartConfigRecognizeTestFiles.UploadExcelAsync(client, bytes, fileName);

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

    private Task<int> UploadExcelAsync(byte[] bytes, string fileName) =>
        SmartConfigRecognizeTestFiles.UploadExcelAsync(_client, bytes, fileName);

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
