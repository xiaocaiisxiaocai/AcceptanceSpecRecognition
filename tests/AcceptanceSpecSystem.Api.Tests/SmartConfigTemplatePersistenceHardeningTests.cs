using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class SmartConfigTemplatePersistenceHardeningTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SmartConfigTemplatePersistenceHardeningTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SaveTemplate_WhenTwoServiceScopesPersistSameStructure_ShouldReturnSingleTemplate()
    {
        var customerId = await CreateCustomerAsync("智能识别-模板并发幂等客户");
        var headers = new[] { "项目", "规格", "验收", "备注" };
        var mapping = new ColumnMapping
        {
            ProjectColumn = 0,
            SpecificationColumn = 1,
            AcceptanceColumn = 2,
            RemarkColumn = 3,
            HeaderRowIndex = 0,
            HeaderRowCount = 1,
            DataStartRowIndex = 1
        };
        await using var firstScope = _factory.Services.CreateAsyncScope();
        await using var secondScope = _factory.Services.CreateAsyncScope();
        var firstService = firstScope.ServiceProvider.GetRequiredService<DocumentTemplateAppService>();
        var secondService = secondScope.ServiceProvider.GetRequiredService<DocumentTemplateAppService>();

        var templates = await Task.WhenAll(
            firstService.SaveTemplateAsync(customerId, "并发模板A", headers, mapping, 2),
            secondService.SaveTemplateAsync(customerId, "并发模板B", headers, mapping, 2));

        templates.Select(template => template.Id).Distinct().Should().ContainSingle();
        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.DocumentTemplates.CountAsync(template => template.CustomerId == customerId))
            .Should().Be(1);
    }

    [Fact]
    public async Task Recognize_WhenOldSingleRegionTemplateMissesShiftedSecondRegion_ShouldNeedConfirm()
    {
        var customerId = await CreateCustomerAsync("智能识别-旧模板移列新增区域客户");
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateShiftedSecondRegionExcelBytes(),
            "smart-recognize-template-shifted-new-region.xlsx");
        var baselineResponse = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));
        var baseline = await baselineResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var headers = baseline.Data.GetProperty("tables").EnumerateArray().Single()
            .GetProperty("headers").EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();
        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            db.DocumentTemplates.Add(new DocumentTemplate
            {
                CustomerId = customerId,
                TemplateName = "旧单区域模板",
                HeadersFingerprint = new string('e', 64),
                HeadersJson = JsonSerializer.Serialize(headers),
                ProjectColumnIndex = 2,
                SpecificationColumnIndex = 3,
                AcceptanceColumnIndex = 8,
                RemarkColumnIndex = 9,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1,
                DataEndRowIndex = 2,
                TableKind = "Acceptance",
                Recommendation = "AutoApply",
                CreatedAt = now,
                UpdatedAt = now,
                Regions =
                [
                    new DocumentTemplateRegion
                    {
                        RegionIndex = 0,
                        HeadersJson = JsonSerializer.Serialize(headers),
                        HeaderRowIndex = 0,
                        HeaderRowCount = 1,
                        DataStartRowIndex = 1,
                        DataEndRowIndex = 2,
                        ProjectColumnIndex = 2,
                        SpecificationColumnIndex = 3,
                        AcceptanceColumnIndex = 8,
                        RemarkColumnIndex = 9
                    }
                ]
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));

        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();
        table.GetProperty("source").GetString().Should().Be("Template");
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
        table.GetProperty("regions").EnumerateArray()
            .SelectMany(region => region.GetProperty("issues").EnumerateArray())
            .Should().Contain(issue =>
                issue.GetProperty("code").GetString() == "UncoveredRegionHeader");
    }

    [Fact]
    public async Task SaveTemplate_WithSameNameAndHeadersButDifferentMultiRegionStructure_ShouldKeepBothVariants()
    {
        var customerId = await CreateCustomerAsync("智能识别-同名结构变体客户");
        var headers = new[] { "项目", "规格", "验收" };
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<DocumentTemplateAppService>();
        var mapping = new ColumnMapping
        {
            ProjectColumn = 0,
            SpecificationColumn = 1,
            AcceptanceColumn = 2,
            HeaderRowIndex = 0,
            HeaderRowCount = 1,
            DataStartRowIndex = 1
        };
        static DocumentTemplateRegionInput Region(int index, int header, int start, int end, string[] regionHeaders) => new()
        {
            RegionIndex = index,
            Headers = regionHeaders,
            HeaderRowIndex = header,
            HeaderRowCount = 1,
            DataStartRowIndex = start,
            DataEndRowIndex = end,
            ProjectColumnIndex = 0,
            SpecificationColumnIndex = 1,
            AcceptanceColumnIndex = 2
        };

        await service.SaveTemplateAsync(
            customerId, "工作表1", headers, mapping, 2, regions:
            [Region(0, 0, 1, 2, headers), Region(1, 5, 6, 7, headers)]);
        await service.SaveTemplateAsync(
            customerId, "工作表1", headers, mapping, 2, regions:
            [Region(0, 0, 1, 2, headers), Region(1, 8, 9, 10, headers)]);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.DocumentTemplates.CountAsync(template => template.CustomerId == customerId)).Should().Be(2);
    }

    [Fact]
    public async Task Recognize_WhenCustomerHasCorruptedTemplateHeadersJson_ShouldSkipTemplate()
    {
        var customerId = await CreateCustomerAsync("智能识别-损坏模板客户");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.DocumentTemplates.Add(new DocumentTemplate
            {
                CustomerId = customerId,
                TemplateName = "损坏模板",
                HeadersFingerprint = new string('d', 64),
                HeadersJson = "not-json",
                ProjectColumnIndex = 0,
                SpecificationColumnIndex = 1,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1,
                TableKind = "Acceptance",
                Recommendation = "NeedConfirm",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateExcelBytes(),
            "smart-recognize-corrupt-template.xlsx");
        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));

        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Data.GetProperty("tables").EnumerateArray().Single()
            .GetProperty("source").GetString().Should().NotBe("Template");
    }

    private async Task<int> CreateCustomerAsync(string name)
    {
        var response = await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return body.Data.GetProperty("id").GetInt32();
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

    private static byte[] CreateShiftedSecondRegionExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 3).Value = "项目";
        worksheet.Cell(1, 4).Value = "规格";
        worksheet.Cell(1, 9).Value = "验收";
        worksheet.Cell(1, 10).Value = "备注";
        worksheet.Cell(1, 11).Value = "备用一";
        worksheet.Cell(1, 12).Value = "备用二";
        for (var row = 2; row <= 3; row++)
        {
            worksheet.Cell(row, 3).Value = row == 2 ? "外观" : "尺寸";
            worksheet.Cell(row, 4).Value = row == 2 ? "无划伤" : "100mm";
            worksheet.Cell(row, 9).Value = "OK";
            worksheet.Cell(row, 10).Value = "抽检";
        }

        worksheet.Cell(6, 5).Value = "项目";
        worksheet.Cell(6, 6).Value = "规格";
        worksheet.Cell(6, 11).Value = "验收";
        worksheet.Cell(6, 12).Value = "备注";
        for (var row = 7; row <= 8; row++)
        {
            worksheet.Cell(row, 5).Value = row == 7 ? "功能" : "安全";
            worksheet.Cell(row, 6).Value = row == 7 ? "运行正常" : "保护有效";
            worksheet.Cell(row, 11).Value = "OK";
            worksheet.Cell(row, 12).Value = "复验";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
