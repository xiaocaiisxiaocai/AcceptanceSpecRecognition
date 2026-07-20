using System.Net;
using System.Reflection;
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
    public void DegradedTemplateSelection_ShouldPreferMatchingRegionStructureOverHistoricalUsage()
    {
        var oldTemplate = new DocumentTemplate
        {
            Id = 1,
            UsageCount = 31,
            UpdatedAt = DateTime.UtcNow.AddDays(-10),
            Regions = [new DocumentTemplateRegion { RegionIndex = 0 }]
        };
        var currentTemplate = new DocumentTemplate
        {
            Id = 2,
            UsageCount = 0,
            UpdatedAt = DateTime.UtcNow,
            Regions =
            [
                new DocumentTemplateRegion { RegionIndex = 0 },
                new DocumentTemplateRegion { RegionIndex = 1 }
            ]
        };
        var oldCandidate = CreateDegradedCandidate(2, 3);
        var currentCandidate = CreateDegradedCandidate(2, 2);
        var candidates = new List<(
            SmartConfigurationRecognizedTable Table,
            DocumentTemplate Template)>
        {
            (oldCandidate, oldTemplate),
            (currentCandidate, currentTemplate)
        };
        var method = typeof(SmartConfigurationAppService).GetMethod(
            "SelectBestDegradedTemplateCandidate",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        var selected = ((
            SmartConfigurationRecognizedTable Table,
            DocumentTemplate Template))method!.Invoke(null, [candidates])!;

        selected.Template.Id.Should().Be(2);
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
    public async Task Recognize_WhenOldTemplateCoversACompositeRepeatedHeader_ShouldRediscoverRegions()
    {
        var customerId = await CreateCustomerAsync("智能识别-旧宽范围模板客户");
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateCompositeRepeatedHeaderExcelBytes(),
            "smart-recognize-template-composite-repeated-header.xlsx");
        var baselineResponse = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));
        var baselineText = await baselineResponse.Content.ReadAsStringAsync();
        baselineResponse.StatusCode.Should().Be(HttpStatusCode.OK, baselineText);
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
                TemplateName = "旧宽范围模板",
                HeadersFingerprint = new string('f', 64),
                HeadersJson = JsonSerializer.Serialize(headers),
                ProjectColumnIndex = 2,
                SpecificationColumnIndex = 3,
                AcceptanceColumnIndex = 8,
                RemarkColumnIndex = 9,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1,
                DataEndRowIndex = 9,
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
                        DataEndRowIndex = 9,
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
        var regions = table.GetProperty("regions").EnumerateArray().ToList();
        table.GetProperty("source").GetString().Should().Be("Template");
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
        regions.Should().HaveCount(2);
        ReadRegionCoordinates(regions[0]).Should().Be((0, 1, 1, 2));
        ReadRegionCoordinates(regions[1]).Should().Be((5, 2, 8, 9));
        regions[0].GetProperty("issues").EnumerateArray()
            .Should().Contain(issue =>
                issue.GetProperty("code").GetString() == "TemplateRegionStructureChanged");
    }

    [Fact]
    public async Task Recognize_WhenConfirmedMultiRegionTemplateStillMatches_ShouldAutoApply()
    {
        var customerId = await CreateCustomerAsync("智能识别-已确认多区域自动采用客户");
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateCompositeRepeatedHeaderExcelBytes(),
            "smart-recognize-confirmed-multi-region-template.xlsx");
        var baselineResponse = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));
        var baselineText = await baselineResponse.Content.ReadAsStringAsync();
        baselineResponse.StatusCode.Should().Be(HttpStatusCode.OK, baselineText);
        var baseline = await baselineResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var baselineTable = baseline.Data.GetProperty("tables").EnumerateArray().Single();
        baselineTable.GetProperty("decision").GetString().Should().Be(
            "NeedConfirm",
            "首次发现尚未学习的多区域结构仍需用户确认一次");
        var headers = baselineTable.GetProperty("headers").EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();
        var baselineRegions = baselineTable.GetProperty("regions").EnumerateArray().ToList();
        baselineRegions.Should().HaveCount(2);

        var mapping = new ColumnMapping
        {
            ProjectColumn = ReadNullableInt(baselineTable, "projectColumnIndex"),
            SpecificationColumn = ReadNullableInt(baselineTable, "specificationColumnIndex"),
            AcceptanceColumn = ReadNullableInt(baselineTable, "acceptanceColumnIndex"),
            RemarkColumn = ReadNullableInt(baselineTable, "remarkColumnIndex"),
            HeaderRowIndex = baselineTable.GetProperty("headerRowIndex").GetInt32(),
            HeaderRowCount = baselineTable.GetProperty("headerRowCount").GetInt32(),
            DataStartRowIndex = baselineTable.GetProperty("dataStartRowIndex").GetInt32()
        };
        var templateRegions = baselineRegions.Select((region, index) => new DocumentTemplateRegionInput
        {
            RegionIndex = index,
            Headers = region.GetProperty("headers").EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty)
                .ToArray(),
            HeaderRowIndex = region.GetProperty("headerRowIndex").GetInt32(),
            HeaderRowCount = region.GetProperty("headerRowCount").GetInt32(),
            DataStartRowIndex = region.GetProperty("dataStartRowIndex").GetInt32(),
            DataEndRowIndex = ReadNullableInt(region, "dataEndRowIndex"),
            ProjectColumnIndex = ReadNullableInt(region, "projectColumnIndex"),
            SpecificationColumnIndex = ReadNullableInt(region, "specificationColumnIndex") ?? -1,
            AcceptanceColumnIndex = ReadNullableInt(region, "acceptanceColumnIndex"),
            RemarkColumnIndex = ReadNullableInt(region, "remarkColumnIndex"),
            IsSpecificationOnly = region.GetProperty("isSpecificationOnly").GetBoolean()
        }).ToList();
        await using (var saveScope = _factory.Services.CreateAsyncScope())
        {
            var service = saveScope.ServiceProvider.GetRequiredService<DocumentTemplateAppService>();
            await service.SaveTemplateAsync(
                customerId,
                "已确认多区域模板",
                headers,
                mapping,
                ReadNullableInt(baselineTable, "dataEndRowIndex"),
                tableKind: "AcceptanceSpec",
                recommendation: "Recommended",
                regions: templateRegions);
        }

        var learnedResponse = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));

        var learnedText = await learnedResponse.Content.ReadAsStringAsync();
        learnedResponse.StatusCode.Should().Be(HttpStatusCode.OK, learnedText);
        var learned = await learnedResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var learnedTable = learned.Data.GetProperty("tables").EnumerateArray().Single();
        learnedTable.GetProperty("source").GetString().Should().Be("Template");
        learnedTable.GetProperty("decision").GetString().Should().Be("AutoApply");
        learnedTable.GetProperty("regions").EnumerateArray().Should().HaveCount(2);
        learnedTable.GetProperty("regions").EnumerateArray().Should().OnlyContain(region =>
            region.GetProperty("decision").GetString() == "AutoApply" &&
            !region.GetProperty("issues").EnumerateArray().Any());
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

    private static byte[] CreateCompositeRepeatedHeaderExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "一、功能规格：";
        worksheet.Cell(1, 2).Value = "功能";
        worksheet.Cell(1, 3).Value = "具体项目";
        worksheet.Cell(1, 4).Value = "规格";
        worksheet.Cell(1, 9).Value = "OK/NG";
        worksheet.Cell(1, 10).Value = "Remark";
        worksheet.Cell(2, 3).Value = "外观";
        worksheet.Cell(2, 4).Value = "无划伤";
        worksheet.Cell(2, 9).Value = "OK";
        worksheet.Cell(3, 3).Value = "尺寸";
        worksheet.Cell(3, 4).Value = "100mm";
        worksheet.Cell(3, 9).Value = "OK";

        worksheet.Cell(6, 1).Value = "三、安装需求：";
        worksheet.Cell(6, 2).Value = "项目";
        worksheet.Cell(6, 3).Value = "细项";
        worksheet.Cell(6, 4).Value = "规格";
        worksheet.Cell(6, 9).Value = "厂商确认";
        worksheet.Cell(7, 9).Value = "OK/NG";
        worksheet.Cell(7, 10).Value = "Remark";
        worksheet.Cell(8, 3).Value = "装机前验机";
        worksheet.Cell(9, 3).Value = "放置地点";
        worksheet.Cell(9, 4).Value = "一楼";
        worksheet.Cell(10, 3).Value = "电力";
        worksheet.Cell(10, 4).Value = "三相380V";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static SmartConfigurationRecognizedTable CreateDegradedCandidate(
        int regionCount,
        int errorCount)
    {
        var issues = Enumerable.Range(0, errorCount)
            .Select(index => new SmartConfigurationRecognitionIssue
            {
                Code = $"Error{index}",
                Severity = "Error",
                Message = $"错误{index}"
            })
            .ToList();

        return new SmartConfigurationRecognizedTable
        {
            Decision = "NeedConfirm",
            Regions = Enumerable.Range(0, regionCount)
                .Select(index => new SmartConfigurationRecognizedRegion
                {
                    RegionIndex = index,
                    Decision = "NeedConfirm",
                    Issues = index == 0 ? issues : []
                })
                .ToList()
        };
    }

    private static (int HeaderRowIndex, int HeaderRowCount, int DataStartRowIndex, int DataEndRowIndex)
        ReadRegionCoordinates(JsonElement region) =>
        (
            region.GetProperty("headerRowIndex").GetInt32(),
            region.GetProperty("headerRowCount").GetInt32(),
            region.GetProperty("dataStartRowIndex").GetInt32(),
            region.GetProperty("dataEndRowIndex").GetInt32()
        );

    private static int? ReadNullableInt(JsonElement element, string propertyName)
    {
        var property = element.GetProperty(propertyName);
        return property.ValueKind == JsonValueKind.Null ? null : property.GetInt32();
    }
}
