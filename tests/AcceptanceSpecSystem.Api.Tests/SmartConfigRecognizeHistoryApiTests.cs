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
            customerId,
            enableLlmAssistance = true,
            llmServiceId = 321
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
            fileId,
            enableLlmAssistance = true,
            llmServiceId = 321
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
            fileId,
            enableLlmAssistance = true,
            llmServiceId = 321
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
        var fileId = await UploadExcelAsync(
            CreateExcelBytes(),
            "smart-recognize-history-routing-metadata.xlsx");

        var response = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            fileId,
            tableIndex = 0,
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
        var fileId = await UploadExcelAsync(CreateHistoryTemplateExcelBytes(), $"smart-history-confirm-{Guid.NewGuid():N}.xlsx");
        var response = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            fileId,
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

    private Task<int> UploadExcelAsync(byte[] bytes, string fileName) =>
        SmartConfigRecognizeTestFiles.UploadExcelAsync(_client, bytes, fileName);

    private static byte[] CreateHistoryTemplateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("历史模板");
        worksheet.Cell(1, 1).Value = "检查对象";
        worksheet.Cell(1, 2).Value = "管制条件";
        worksheet.Cell(1, 3).Value = "供应商回复";
        worksheet.Cell(1, 4).Value = "补充说明";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(2, 3).Value = "OK";
        worksheet.Cell(2, 4).Value = "抽检";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
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
            fileId,
            enableLlmAssistance = true,
            llmServiceId = 321
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

    private Task<int> UploadExcelAsync(byte[] bytes, string fileName) =>
        SmartConfigRecognizeTestFiles.UploadExcelAsync(_client, bytes, fileName);

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
