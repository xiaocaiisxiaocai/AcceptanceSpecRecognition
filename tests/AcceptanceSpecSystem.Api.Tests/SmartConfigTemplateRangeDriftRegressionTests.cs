using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class SmartConfigTemplateRangeDriftRegressionTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SmartConfigTemplateRangeDriftRegressionTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenSingleRegionTemplateRangeNoLongerContainsData_ShouldNeedConfirm()
    {
        var customerId = await CreateCustomerAsync();
        var headers = new[] { "项目", "规格内容", "验收结果", "备注" };
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            db.DocumentTemplates.Add(new DocumentTemplate
            {
                CustomerId = customerId,
                TemplateName = "旧单区域模板",
                HeadersFingerprint = new string('c', 64),
                HeadersJson = JsonSerializer.Serialize(headers),
                ProjectColumnIndex = 0,
                SpecificationColumnIndex = 1,
                AcceptanceColumnIndex = 2,
                RemarkColumnIndex = 3,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1,
                DataEndRowIndex = 5,
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
                        DataEndRowIndex = 5,
                        ProjectColumnIndex = 0,
                        SpecificationColumnIndex = 1,
                        AcceptanceColumnIndex = 2,
                        RemarkColumnIndex = 3
                    }
                ]
            });
            await db.SaveChangesAsync();
        }

        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateExcelWithDataMovedAfterTemplateRangeBytes(),
            "smart-recognize-template-range-drift.xlsx");
        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));

        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();
        table.GetProperty("source").GetString().Should().Be("RuleBased");
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
        table.GetProperty("dataEndRowIndex").GetInt32().Should().Be(11);
        table.GetProperty("issues").EnumerateArray()
            .Should().Contain(issue =>
                issue.GetProperty("code").GetString() == "TemplateRegionStructureChanged");
        table.GetProperty("regions").EnumerateArray().Single()
            .GetProperty("issues").EnumerateArray()
            .Should().Contain(issue =>
                issue.GetProperty("code").GetString() == "UnassignedDataAfterGap");
    }

    private async Task<int> CreateCustomerAsync()
    {
        var response = await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new
        {
            name = $"智能识别-单区域范围漂移客户-{Guid.NewGuid():N}",
            contactPerson = "测试",
            contactPhone = "13800000000"
        }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return body.Data.GetProperty("id").GetInt32();
    }

    private static byte[] CreateExcelWithDataMovedAfterTemplateRangeBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格内容";
        worksheet.Cell(1, 3).Value = "验收结果";
        worksheet.Cell(1, 4).Value = "备注";
        for (var row = 7; row <= 12; row++)
        {
            worksheet.Cell(row, 1).Value = $"项目{row}";
            worksheet.Cell(row, 2).Value = $"规格{row}";
            worksheet.Cell(row, 3).Value = "OK";
            worksheet.Cell(row, 4).Value = "抽检";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
