using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public class SmartConfigHeaderCandidateRegressionTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SmartConfigHeaderCandidateRegressionTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Recognize_WhenExcelDataRowLooksLikeRepeatedLeafHeader_ShouldKeepEarlierHeader()
    {
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateRepeatedHeaderWithDisguisedDataExcelBytes(),
            "smart-recognize-disguised-data-header.xlsx");

        var table = await RecognizeSingleTableAsync(fileId);

        table.GetProperty("headerRowIndex").GetInt32().Should().Be(0);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(1);
        table.GetProperty("headers").EnumerateArray().Select(item => item.GetString())
            .Should().ContainInOrder("项目", "规格", "附件", "验收", "备注");
    }

    [Fact]
    public async Task Recognize_WhenSingleRegionContinuesAfterTwoBlankRows_ShouldPreserveTailAndNeedConfirm()
    {
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateSingleRegionWithInternalBlankBandExcelBytes(),
            "smart-recognize-single-region-internal-gap.xlsx");

        var table = await RecognizeSingleTableAsync(fileId);

        table.GetProperty("dataEndRowIndex").GetInt32().Should().Be(6);
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
        var region = table.GetProperty("regions").EnumerateArray().Single();
        region.GetProperty("dataEndRowIndex").GetInt32().Should().Be(6);
        region.GetProperty("issues").EnumerateArray()
            .Should().Contain(issue => issue.GetProperty("code").GetString() == "UnassignedDataAfterGap");
    }

    [Fact]
    public async Task Recognize_WhenMultiRegionLeavesBusinessTailUncovered_ShouldReturnCoverageIssue()
    {
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateMultiRegionWithUncoveredBusinessTailExcelBytes(),
            "smart-recognize-multi-region-uncovered-tail.xlsx");

        var table = await RecognizeSingleTableAsync(fileId);
        var regions = table.GetProperty("regions").EnumerateArray().ToList();

        regions.Should().HaveCount(2);
        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
        var coverageIssue = regions.SelectMany(region => region.GetProperty("issues").EnumerateArray())
            .Single(issue => issue.GetProperty("code").GetString() == "UncoveredBusinessRows");
        coverageIssue.GetProperty("message").GetString().Should()
            .Contain("发现 2 行")
            .And.Contain("第 11 行、第 13 行")
            .And.NotContain("第 11-13 行");
    }

    [Fact]
    public async Task Recognize_WhenMultiRegionLeavesOneProjectAndSpecificationRow_ShouldReturnCoverageIssue()
    {
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateMultiRegionWithUncoveredBusinessTailExcelBytes(includeSecondTailRow: false),
            "smart-recognize-multi-region-single-uncovered-row.xlsx");

        var table = await RecognizeSingleTableAsync(fileId);
        var regions = table.GetProperty("regions").EnumerateArray().ToList();

        regions.Should().HaveCount(2);
        var coverageIssue = regions.SelectMany(region => region.GetProperty("issues").EnumerateArray())
            .Single(issue => issue.GetProperty("code").GetString() == "UncoveredBusinessRows");
        coverageIssue.GetProperty("message").GetString().Should()
            .Contain("发现 1 行")
            .And.Contain("第 11 行");
    }

    [Fact]
    public async Task Recognize_WhenWordDataRowLooksLikeRepeatedLeafHeader_ShouldKeepEarlierHeader()
    {
        var bytes = SmartConfigRecognizeTestFiles.CreateWordBytes(
        [
            ["项目", "规格", "附件", "验收", "备注"],
            ["测试项目", "规格要求", "规格要求", "验收结果", "补充说明"],
            ["测试项目", "规格要求", "规格要求", "验收结果", "补充说明"]
        ]);
        var fileId = await SmartConfigRecognizeTestFiles.UploadWordAsync(
            _client,
            bytes,
            "smart-recognize-disguised-data-header.docx");

        var table = await RecognizeSingleTableAsync(fileId);

        table.GetProperty("headerRowIndex").GetInt32().Should().Be(0);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Recognize_WhenOneCustomerRuleTextMatchesAllTypes_ShouldNotTreatItAsCompleteHeader()
    {
        var customerId = await CreateCustomerAsync("重复叶表头-动态规则冲突客户");
        for (var targetField = 1; targetField <= 4; targetField++)
        {
            await CreateColumnRuleAsync(customerId, "万能字段", targetField);
        }

        var bytes = SmartConfigRecognizeTestFiles.CreateWordBytes(
        [
            ["项目", "规格", "验收", "备注"],
            ["万能字段", "万能字段", "普通值", "普通值"]
        ]);
        var fileId = await SmartConfigRecognizeTestFiles.UploadWordAsync(
            _client,
            bytes,
            "smart-recognize-conflicting-customer-rules.docx");

        var table = await RecognizeSingleTableAsync(fileId, customerId);

        table.GetProperty("headerRowIndex").GetInt32().Should().Be(0);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(1);
    }

    [Fact]
    public void HeaderMatcher_WhenDynamicRuleEvidenceDiffersOnlyByCase_ShouldRequireDifferentTexts()
    {
        var matcherType = typeof(SmartConfigurationAppService).Assembly.GetType(
            "AcceptanceSpecSystem.Application.Services.HeaderKeywordMatcher")!;
        var rules = new[]
        {
            new ColumnHeaderMappingRule(ColumnType.Project, ColumnHeaderMatchMode.Equals, "field"),
            new ColumnHeaderMappingRule(ColumnType.Specification, ColumnHeaderMatchMode.Equals, "field"),
            new ColumnHeaderMappingRule(ColumnType.Acceptance, ColumnHeaderMatchMode.Equals, "field"),
            new ColumnHeaderMappingRule(ColumnType.Remark, ColumnHeaderMatchMode.Equals, "field")
        };
        var matcher = matcherType.GetMethod("FromRules")!.Invoke(null, [rules])!;
        var row = new RowData
        {
            Cells = new[] { "FIELD", "Field", "field", "fIeLd" }
                .Select((value, columnIndex) => new CellData
                {
                    ColumnIndex = columnIndex,
                    Value = value
                })
                .ToList()
        };

        var result = (bool)matcherType.GetMethod("IsCompleteRepeatedLeafHeader")!
            .Invoke(matcher, [row])!;

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Recognize_WhenRepeatedBusinessRowsFollowRealLeafHeader_ShouldKeepRealLeafHeader()
    {
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateGroupedHeaderWithRepeatedBusinessRowsExcelBytes(),
            "smart-recognize-business-rows-after-leaf.xlsx");

        var table = await RecognizeSingleTableAsync(fileId);

        table.GetProperty("headerRowIndex").GetInt32().Should().Be(1);
        table.GetProperty("headerRowCount").GetInt32().Should().Be(1);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Recognize_WhenRepeatedLeafAnchorIsFollowedByCompleteRepeatedBusinessRows_ShouldKeepAnchor()
    {
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateRepeatedLeafAnchorWithCompleteBusinessRowsExcelBytes(),
            "smart-recognize-repeated-leaf-anchor-with-business-rows.xlsx");

        var table = await RecognizeSingleTableAsync(fileId);

        table.GetProperty("headerRowIndex").GetInt32().Should().Be(0);
        table.GetProperty("headerRowCount").GetInt32().Should().Be(1);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Recognize_WhenSecondRegionStartsWithMergedBusinessAnchor_ShouldNotDropAnchorRow()
    {
        var customerId = await CreateCustomerAsync($"合并业务首行-{Guid.NewGuid():N}");
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateSecondRegionWithMergedBusinessAnchorExcelBytes(),
            "smart-recognize-merged-business-anchor.xlsx");

        var table = await RecognizeSingleTableAsync(fileId, customerId);
        var regions = table.GetProperty("regions").EnumerateArray().ToList();

        regions.Should().HaveCount(2);
        regions[1].GetProperty("headerRowIndex").GetInt32().Should().Be(5);
        regions[1].GetProperty("headerRowCount").GetInt32().Should().Be(1);
        regions[1].GetProperty("dataStartRowIndex").GetInt32().Should().Be(
            6,
            "横向合并造成项目列与规格列同值时，该行仍可能是第二段首条业务数据");
        regions[1].GetProperty("dataEndRowIndex").GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task Recognize_WhenRemarkHasDistinctEqualConfidenceCandidates_ShouldRequireUserSelection()
    {
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateEqualConfidenceRemarkCandidatesExcelBytes(),
            "smart-recognize-remark-conflict.xlsx");

        var table = await RecognizeSingleTableAsync(fileId);

        table.GetProperty("decision").GetString().Should().Be("NeedConfirm");
        var region = table.GetProperty("regions").EnumerateArray().Single();
        var conflict = region.GetProperty("fieldConflicts").EnumerateArray()
            .Single(item => item.GetProperty("field").GetString() == "Remark");
        conflict.GetProperty("recommendedColumnIndex").GetInt32().Should().Be(3);
        var candidates = conflict.GetProperty("candidates").EnumerateArray().ToList();
        candidates.Select(item => item.GetProperty("columnIndex").GetInt32())
            .Should().Equal(3, 4);
        candidates.Select(item => item.GetProperty("header").GetString())
            .Should().Equal("Remark", "備註");
        candidates.Should().OnlyContain(item =>
            item.GetProperty("confidence").GetDouble() >= 0.95);
        candidates[0].GetProperty("samples").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain("J列内容");
        candidates[1].GetProperty("samples").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain("O列内容");
    }

    [Fact]
    public async Task Recognize_WhenLearnedAliasesHaveEqualPriority_ShouldNotCreateNewFieldConflict()
    {
        var customerId = await CreateCustomerAsync($"学习规则稳定性-{Guid.NewGuid():N}");
        await CreateColumnRuleAsync(
            customerId,
            "項目",
            targetField: 1,
            source: ColumnMappingRuleSource.Learned);
        await CreateColumnRuleAsync(
            customerId,
            "細項",
            targetField: 1,
            source: ColumnMappingRuleSource.Learned);
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateLearnedProjectAliasesExcelBytes(),
            "smart-recognize-learned-project-aliases.xlsx");

        var table = await RecognizeSingleTableAsync(fileId, customerId);

        table.GetProperty("fieldConflicts").EnumerateArray()
            .Should().NotContain(conflict =>
                conflict.GetProperty("field").GetString() == "Project",
                "自动学习用于改善下次映射，不应反过来制造新的人工确认项");
    }

    [Fact]
    public async Task Recognize_WhenRecommendedRemarkColumnIsVerticallyMerged_ShouldPreferPerRowColumn()
    {
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateVerticallyMergedRemarkCandidateExcelBytes(),
            "smart-recognize-vertical-merged-remark.xlsx");

        var table = await RecognizeSingleTableAsync(fileId);
        var region = table.GetProperty("regions").EnumerateArray().Single();

        region.GetProperty("remarkColumnIndex").GetInt32().Should().Be(
            4,
            "纵向合并的备注列无法逐行写回，应自动选择未跨行合并的同义列");
    }

    [Fact]
    public async Task Recognize_WhenWritableRemarkAlternativeComesFromLearnedRule_ShouldKeepMappedColumn()
    {
        var customerId = await CreateCustomerAsync($"可写备注学习规则-{Guid.NewGuid():N}");
        await CreateColumnRuleAsync(
            customerId,
            "归档甲",
            targetField: 4,
            source: ColumnMappingRuleSource.Learned);
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateVerticallyMergedLearnedRemarkCandidateExcelBytes(),
            "smart-recognize-learned-writable-remark.xlsx");
        var headers = new[] { "项目", "规格", "OK/NG", "Remark", "归档甲" };
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            db.DocumentTemplates.Add(new DocumentTemplate
            {
                CustomerId = customerId,
                TemplateName = "历史纵向合并备注模板",
                HeadersFingerprint = Guid.NewGuid().ToString("N"),
                HeadersJson = JsonSerializer.Serialize(headers),
                ProjectColumnIndex = 0,
                SpecificationColumnIndex = 1,
                AcceptanceColumnIndex = 2,
                RemarkColumnIndex = null,
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1,
                DataEndRowIndex = 3,
                TableKind = "Acceptance",
                Recommendation = "NeedConfirm",
                CreatedAt = now,
                UpdatedAt = now,
                Regions =
                {
                    new DocumentTemplateRegion
                    {
                        RegionIndex = 0,
                        HeadersJson = JsonSerializer.Serialize(headers),
                        ProjectColumnIndex = 0,
                        SpecificationColumnIndex = 1,
                        AcceptanceColumnIndex = 2,
                        RemarkColumnIndex = null,
                        HeaderRowIndex = 0,
                        HeaderRowCount = 1,
                        DataStartRowIndex = 1,
                        DataEndRowIndex = 3
                    }
                }
            });
            await db.SaveChangesAsync();
        }

        var table = await RecognizeSingleTableAsync(fileId, customerId);
        var region = table.GetProperty("regions").EnumerateArray().Single();

        region.GetProperty("remarkColumnIndex").GetInt32().Should().Be(
            4,
            "自动学习规则可以提供逐行可写的目标列，但不应因此制造新的字段冲突");
        region.GetProperty("fieldConflicts").EnumerateArray()
            .Should().NotContain(conflict =>
                conflict.GetProperty("field").GetString() == "Remark");
    }

    [Fact]
    public async Task Recognize_WhenAcceptanceColumnHasOnlyLocalVerticalMerge_ShouldKeepAcceptanceColumn()
    {
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateLocallyMergedAcceptanceColumnExcelBytes(),
            "smart-recognize-local-merged-acceptance.xlsx");

        var table = await RecognizeSingleTableAsync(fileId);
        var region = table.GetProperty("regions").EnumerateArray().Single();

        region.GetProperty("acceptanceColumnIndex").GetInt32().Should().Be(
            2,
            "局部逻辑合并不应导致整列验收字段被清空");
    }

    [Fact]
    public async Task Recognize_WhenWordHasGroupedRepeatedLeafHeader_ShouldPreferSingleLeafHeader()
    {
        var bytes = SmartConfigRecognizeTestFiles.CreateWordBytes(
        [
            ["基本信息", "基本信息", "规格信息", "规格信息", "验收信息", "验收信息", "备注信息"],
            ["项目", "附件", "规格", "规格", "验收", "结果", "备注"],
            ["外观", "图片", "无划伤", "无划伤", "OK", "通过", "抽检"]
        ]);
        var fileId = await SmartConfigRecognizeTestFiles.UploadWordAsync(
            _client,
            bytes,
            "smart-recognize-word-grouped-leaf.docx");

        var table = await RecognizeSingleTableAsync(fileId);

        table.GetProperty("headerRowIndex").GetInt32().Should().Be(1);
        table.GetProperty("headerRowCount").GetInt32().Should().Be(1);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Recognize_WhenTrailingHeaderRowsAreIdentical_ShouldPreferLastSingleLeafHeader()
    {
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateIdenticalTrailingHeaderRowsExcelBytes(),
            "smart-recognize-identical-trailing-headers.xlsx");

        var table = await RecognizeSingleTableAsync(fileId);

        table.GetProperty("headerRowIndex").GetInt32().Should().Be(2);
        table.GetProperty("headerRowCount").GetInt32().Should().Be(1);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Recognize_WhenMetadataPrecedesTwoLevelHeader_ShouldUseLeafHeaderAndFullDataRange()
    {
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateMetadataAndTwoLevelHeaderExcelBytes(),
            "smart-recognize-metadata-two-level-header.xlsx");

        var table = await RecognizeSingleTableAsync(fileId);
        var region = table.GetProperty("regions").EnumerateArray().Single();

        table.GetProperty("headerRowIndex").GetInt32().Should().Be(4);
        table.GetProperty("headerRowCount").GetInt32().Should().Be(1);
        table.GetProperty("dataStartRowIndex").GetInt32().Should().Be(5);
        table.GetProperty("dataEndRowIndex").GetInt32().Should().Be(8);
        region.GetProperty("headerRowIndex").GetInt32().Should().Be(4);
        region.GetProperty("dataStartRowIndex").GetInt32().Should().Be(5);
        region.GetProperty("dataEndRowIndex").GetInt32().Should().Be(8);
    }

    [Fact]
    public void StructuralHeaderDetection_WhenLabelsAreUnknown_ShouldUseRowShapeOnly()
    {
        var tableData = new TableData
        {
            Rows =
            [
                CreateStructuralRow(0, "zenith", "zenith", "zenith", "zenith", "zenith", "zenith", "zenith", "zenith"),
                CreateStructuralRow(1, "meta-x", "", "", "meta-y", "", "", "", "v-7"),
                CreateStructuralRow(2, "", "", "", "", "", "", "", ""),
                CreateStructuralRow(3, "amber", "amber", "birch", "birch", "coral", "coral", "dune", "dune"),
                CreateStructuralRow(4, "atlas", "boreal", "cinder", "delta", "ember", "fjord", "glyph", "harbor"),
                CreateStructuralRow(5, "A-01", "north", "1", "a deliberately long payload that represents row content", "x", "", "", ""),
                CreateStructuralRow(6, "A-02", "south", "2", "another deliberately long payload representing row content", "y", "", "", ""),
                CreateStructuralRow(7, "A-03", "west", "3", "a third deliberately long payload representing row content", "z", "", "", "")
            ]
        };

        var detectedRowIndex = DetectStructuralHeaderRow(tableData);

        detectedRowIndex.Should().Be(4);
    }

    [Fact]
    public void StructuralHeaderDetection_WhenShortFirstDataRowPrecedesNumericRows_ShouldAbstain()
    {
        var tableData = new TableData
        {
            Rows =
            [
                CreateStructuralRow(0, "", "", ""),
                CreateStructuralRow(1, "A", "B", "C"),
                CreateStructuralRow(2, "10000", "20000", "30000"),
                CreateStructuralRow(3, "10001", "20001", "30001")
            ]
        };

        DetectStructuralHeaderRow(tableData).Should().BeNull();
    }

    [Fact]
    public void StructuralHeaderDetection_WhenOnlyOneDataRowFollows_ShouldAbstain()
    {
        var tableData = new TableData
        {
            Rows =
            [
                CreateStructuralRow(0, "atlas", "boreal", "cinder", "delta"),
                CreateStructuralRow(1, "A-01", "1", "a deliberately long payload representing row content", "x")
            ]
        };

        DetectStructuralHeaderRow(tableData).Should().BeNull();
    }

    [Fact]
    public void StructuralHeaderDetection_WhenOrdinaryRowsContainAdjacentRepeatedValues_ShouldAbstain()
    {
        var tableData = new TableData
        {
            Rows =
            [
                CreateStructuralRow(0, "批次甲", "批次甲", "工位一", "工位一", "白班", "白班", "线体A", "线体A"),
                CreateStructuralRow(1, "A-01", "north", "1", "短说明", "x", "已确认", "张三", "一号"),
                CreateStructuralRow(2, "A-02", "south", "2", "第二条非常详细且长度足够的普通业务说明内容用于记录现场情况", "y", "已确认", "李四", "2026-07-02"),
                CreateStructuralRow(3, "A-03", "west", "3", "第三条非常详细且长度足够的普通业务说明内容用于记录现场情况", "z", "已确认", "王五", "2026-07-03")
            ]
        };

        DetectStructuralHeaderRow(tableData).Should().BeNull();
    }

    [Fact]
    public async Task Recognize_WhenExcelSheetIsEmpty_ShouldReturnEmptyTableWithoutError()
    {
        using var workbook = new XLWorkbook();
        workbook.AddWorksheet("空表");
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            stream.ToArray(),
            "smart-recognize-empty-sheet.xlsx");

        var table = await RecognizeSingleTableAsync(fileId);

        table.GetProperty("headers").GetArrayLength().Should().Be(0);
        table.GetProperty("dataEndRowIndex").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private async Task<JsonElement> RecognizeSingleTableAsync(int fileId, int? customerId = null)
    {
        var response = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId,
            customerId
        }));
        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);

        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return body.Data.GetProperty("tables").EnumerateArray().Single();
    }

    private async Task<int> CreateCustomerAsync(string name)
    {
        var response = await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("id").GetInt32();
    }

    private async Task CreateColumnRuleAsync(
        int? customerId,
        string pattern,
        int targetField,
        ColumnMappingRuleSource source = ColumnMappingRuleSource.Manual)
    {
        var response = await _client.PostAsync("/api/column-mapping-rules", ApiClientJson.ToJsonContent(new
        {
            pattern,
            targetField,
            matchMode = 2,
            priority = 200,
            enabled = true,
            source,
            customerId
        }));
        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);
    }

    private static byte[] CreateLearnedProjectAliasesExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "項目";
        worksheet.Cell(1, 2).Value = "細項";
        worksheet.Cell(1, 3).Value = "規格";
        worksheet.Cell(1, 4).Value = "OK/NG";
        worksheet.Cell(1, 5).Value = "Remark";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "表面";
        worksheet.Cell(2, 3).Value = "无划伤";
        worksheet.Cell(2, 4).Value = "OK";
        worksheet.Cell(2, 5).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateRepeatedHeaderWithDisguisedDataExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "附件";
        worksheet.Cell(1, 4).Value = "验收";
        worksheet.Cell(1, 5).Value = "备注";
        worksheet.Cell(2, 1).Value = "测试项目";
        worksheet.Range("B2:C2").Merge().Value = "规格要求";
        worksheet.Cell(2, 4).Value = "验收结果";
        worksheet.Cell(2, 5).Value = "补充说明";
        worksheet.Cell(3, 1).Value = "测试项目";
        worksheet.Range("B3:C3").Merge().Value = "规格要求";
        worksheet.Cell(3, 4).Value = "验收结果";
        worksheet.Cell(3, 5).Value = "补充说明";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateSingleRegionWithInternalBlankBandExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "验收";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(2, 3).Value = "OK";
        worksheet.Cell(3, 1).Value = "尺寸";
        worksheet.Cell(3, 2).Value = "100mm";
        worksheet.Cell(3, 3).Value = "OK";
        worksheet.Cell(6, 1).Value = "功能";
        worksheet.Cell(6, 2).Value = "运行正常";
        worksheet.Cell(6, 3).Value = "OK";
        worksheet.Cell(7, 1).Value = "安全";
        worksheet.Cell(7, 2).Value = "保护有效";
        worksheet.Cell(7, 3).Value = "OK";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateMultiRegionWithUncoveredBusinessTailExcelBytes(bool includeSecondTailRow = true)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        void WriteHeader(int row)
        {
            worksheet.Cell(row, 1).Value = "项目";
            worksheet.Cell(row, 2).Value = "规格";
            worksheet.Cell(row, 3).Value = "验收";
        }

        void WriteData(int row, string project, string specification)
        {
            worksheet.Cell(row, 1).Value = project;
            worksheet.Cell(row, 2).Value = specification;
            worksheet.Cell(row, 3).Value = "OK";
        }

        WriteHeader(1);
        WriteData(2, "外观", "无划伤");
        WriteData(3, "尺寸", "100mm");
        WriteHeader(6);
        WriteData(7, "功能", "运行正常");
        WriteData(8, "安全", "保护有效");
        WriteData(11, "噪声", "低于标准");
        if (includeSecondTailRow)
        {
            WriteData(13, "温升", "低于限值");
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateIdenticalTrailingHeaderRowsExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Utility");
        worksheet.Cell(1, 1).Value = "基本信息";
        worksheet.Cell(1, 2).Value = "基本信息";
        worksheet.Cell(1, 3).Value = "规格信息";
        worksheet.Cell(1, 4).Value = "规格信息";
        worksheet.Cell(1, 5).Value = "验收信息";
        worksheet.Cell(1, 6).Value = "备注信息";
        for (var row = 2; row <= 3; row++)
        {
            worksheet.Cell(row, 1).Value = "项目";
            worksheet.Cell(row, 2).Value = "附件";
            worksheet.Cell(row, 3).Value = "规格";
            worksheet.Cell(row, 4).Value = "验收";
            worksheet.Cell(row, 5).Value = "结果";
            worksheet.Cell(row, 6).Value = "备注";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateGroupedHeaderWithRepeatedBusinessRowsExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Range("A1:B1").Merge().Value = "基本信息";
        worksheet.Range("C1:D1").Merge().Value = "规格信息";
        worksheet.Range("E1:F1").Merge().Value = "验收信息";
        worksheet.Cell(1, 7).Value = "备注信息";
        worksheet.Cell(2, 1).Value = "项目";
        worksheet.Range("B2:C2").Merge().Value = "规格";
        worksheet.Cell(2, 4).Value = "验收";
        worksheet.Cell(2, 5).Value = "备注";
        for (var row = 3; row <= 4; row++)
        {
            worksheet.Cell(row, 1).Value = "测试项目";
            worksheet.Cell(row, 2).Value = "业务描述";
            worksheet.Cell(row, 3).Value = "业务描述";
            worksheet.Cell(row, 4).Value = "OK";
            worksheet.Cell(row, 5).Value = "现场备注";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateRepeatedLeafAnchorWithCompleteBusinessRowsExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Range("B1:C1").Merge().Value = "规格";
        worksheet.Cell(1, 4).Value = "验收";
        worksheet.Cell(1, 5).Value = "备注";
        for (var row = 2; row <= 3; row++)
        {
            worksheet.Cell(row, 1).Value = "测试项目";
            worksheet.Range($"B{row}:C{row}").Merge().Value = "规格要求";
            worksheet.Cell(row, 4).Value = "验收结果";
            worksheet.Cell(row, 5).Value = "补充说明";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateSecondRegionWithMergedBusinessAnchorExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 3).Value = "项目";
        worksheet.Cell(1, 4).Value = "规格";
        worksheet.Cell(1, 5).Value = "OK/NG";
        worksheet.Cell(1, 6).Value = "备注";
        worksheet.Cell(2, 3).Value = "外观";
        worksheet.Cell(2, 4).Value = "无划伤";
        worksheet.Cell(2, 5).Value = "OK";
        worksheet.Cell(3, 3).Value = "尺寸";
        worksheet.Cell(3, 4).Value = "10±1mm";
        worksheet.Cell(3, 5).Value = "OK";

        worksheet.Cell(6, 3).Value = "项目";
        worksheet.Cell(6, 4).Value = "规格";
        worksheet.Cell(6, 5).Value = "OK/NG";
        worksheet.Cell(6, 6).Value = "备注";
        worksheet.Cell(7, 2).Value = "设备装机";
        worksheet.Range(7, 3, 7, 4).Merge();
        worksheet.Cell(7, 3).Value = "装机前验机";
        worksheet.Cell(8, 3).Value = "供电";
        worksheet.Cell(8, 4).Value = "220V";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateEqualConfidenceRemarkCandidatesExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "OK/NG";
        worksheet.Cell(1, 4).Value = "Remark";
        worksheet.Cell(1, 5).Value = "備註";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(2, 3).Value = "OK";
        worksheet.Cell(2, 4).Value = "J列内容";
        worksheet.Cell(2, 5).Value = "O列内容";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateVerticallyMergedRemarkCandidateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "OK/NG";
        worksheet.Cell(1, 4).Value = "Remark";
        worksheet.Cell(1, 5).Value = "備註";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(3, 1).Value = "尺寸";
        worksheet.Cell(3, 2).Value = "10±1mm";
        worksheet.Range("D2:D3").Merge();
        worksheet.Cell(2, 5).Value = "逐行备注1";
        worksheet.Cell(3, 5).Value = "逐行备注2";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateVerticallyMergedLearnedRemarkCandidateExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "OK/NG";
        worksheet.Cell(1, 4).Value = "Remark";
        worksheet.Cell(1, 5).Value = "归档甲";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(3, 1).Value = "尺寸";
        worksheet.Cell(3, 2).Value = "10±1mm";
        worksheet.Cell(4, 1).Value = "功能";
        worksheet.Cell(4, 2).Value = "运行正常";
        worksheet.Range(2, 4, 4, 4).Merge();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateLocallyMergedAcceptanceColumnExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "OK/NG";
        worksheet.Cell(1, 4).Value = "Remark";
        for (var row = 2; row <= 7; row++)
        {
            worksheet.Cell(row, 1).Value = $"项目{row}";
            worksheet.Cell(row, 2).Value = $"规格{row}";
        }
        worksheet.Range("C6:C7").Merge();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateMetadataAndTwoLevelHeaderExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("設備需求表(自動化)");
        worksheet.Range("B2:K2").Merge().Value = "設備需求表（自動化設備）";
        worksheet.Cell("B3").Value = "设备名称：鐳射鑽孔下料機/SAA";
        worksheet.Cell("F3").Value = "产品流向：进出口方案：";
        worksheet.Cell("K3").Value = "版次:2510";

        var groupedHeaders = new[]
        {
            "項次", "設備評估專案", "分類", "評估說明", "評估說明", "驗收方式",
            "廠商確認", "廠商確認", "廠商確認", "AaltoSemi確認", "設備驗收", "設備驗收", "验收前佐助资料"
        };
        var leafHeaders = new[]
        {
            "項次", "設備評估專案", "分類", "評估說明", "評估說明", "驗收方式",
            "可满足", "不涉及", "說明", "AaltoSemi確認", "點檢人", "佐證資料", "（張貼照片或文件）"
        };
        for (var index = 0; index < groupedHeaders.Length; index++)
        {
            worksheet.Cell(5, index + 2).Value = groupedHeaders[index];
            worksheet.Cell(6, index + 2).Value = leafHeaders[index];
        }

        for (var row = 7; row <= 10; row++)
        {
            worksheet.Cell(row, 2).Value = "1-1";
            worksheet.Cell(row, 3).Value = "符合规定";
            worksheet.Cell(row, 4).Value = row == 10 ? "串线时序" : "产品质保";
            worksheet.Cell(row, 5).Value = row - 6;
            worksheet.Cell(row, 6).Value = $"设备规格说明 {row}，必须满足验收要求。";
            worksheet.Cell(row, 7).Value = row % 2 == 0 ? "实际确认" : "保证函";
            worksheet.Cell(row, 8).Value = "OK";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static int? DetectStructuralHeaderRow(TableData tableData)
    {
        var detector = typeof(SmartConfigurationAppService).GetMethod(
            "FindStructuralHeaderToDataTransition",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        detector.Should().NotBeNull();
        return (int?)detector!.Invoke(null, [tableData, 20]);
    }

    private static RowData CreateStructuralRow(int rowIndex, params string[] values) => new()
    {
        Index = rowIndex,
        Cells = values
            .Select((value, columnIndex) => new CellData
            {
                RowIndex = rowIndex,
                ColumnIndex = columnIndex,
                Value = value
            })
            .ToList()
    };
}

public class SmartConfigConfirmValidationRegressionTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SmartConfigConfirmValidationRegressionTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData(0, 0, 1, null, 0, 1, 2, 3, "表头行数必须大于0")]
    [InlineData(1, 1, 1, null, 0, 1, 2, 3, "数据起始行不能早于表头结束行")]
    [InlineData(0, 1, 1, null, 0, 4, 2, 3, "规格列索引超出表头范围")]
    [InlineData(0, 1, 1, 0, 0, 1, 2, 3, "数据结束行不能早于数据起始行")]
    [InlineData(0, 1, 1, null, -1, 1, 2, 3, "项目列索引超出表头范围")]
    [InlineData(0, 1, 1, null, 0, 1, 4, 3, "验收列索引超出表头范围")]
    [InlineData(0, 1, 1, null, 0, 1, 2, 4, "备注列索引超出表头范围")]
    public async Task Confirm_WhenStructureIsInvalid_ShouldRejectBeforeSaving(
        int headerRowIndex,
        int headerRowCount,
        int dataStartRowIndex,
        int? dataEndRowIndex,
        int projectColumnIndex,
        int specificationColumnIndex,
        int acceptanceColumnIndex,
        int remarkColumnIndex,
        string expectedMessage)
    {
        var customerId = await CreateCustomerAsync($"确认结构校验-{Guid.NewGuid():N}");

        var response = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            customerId,
            templateName = "无效结构模板",
            headers = new[] { "项目", "规格", "验收", "备注" },
            projectColumnIndex,
            specificationColumnIndex,
            acceptanceColumnIndex,
            remarkColumnIndex,
            headerRowIndex,
            headerRowCount,
            dataStartRowIndex,
            dataEndRowIndex,
            isSpecificationOnly = false,
            learnedColumns = Array.Empty<object>()
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain(expectedMessage);
    }

    [Fact]
    public async Task Confirm_WhenUserModifiedStructureWithoutFileId_ShouldReject()
    {
        var customerId = await CreateCustomerAsync($"确认结构校验-缺文件-{Guid.NewGuid():N}");

        var response = await PostValidConfirmAsync(customerId, userModifiedStructure: true);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("确认结构时必须提供有效FileId");
    }

    [Fact]
    public async Task Confirm_WhenRequestIsNotUserModifiedButHasNoFileId_ShouldReject()
    {
        var customerId = await CreateCustomerAsync($"确认结构校验-旧请求-{Guid.NewGuid():N}");

        var response = await PostValidConfirmAsync(customerId, userModifiedStructure: false);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("确认结构时必须提供有效FileId");
    }

    [Fact]
    public async Task Confirm_WhenDataEndExceedsUploadedTable_ShouldRejectBeforeSaving()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            stream.ToArray(),
            $"smart-confirm-invalid-end-{Guid.NewGuid():N}.xlsx");
        var customerId = await CreateCustomerAsync($"确认结束行上界-{Guid.NewGuid():N}");

        var response = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            fileId,
            tableIndex = 0,
            customerId,
            templateName = "结束行越界模板",
            headers = new[] { "项目", "规格" },
            projectColumnIndex = 0,
            specificationColumnIndex = 1,
            headerRowIndex = 0,
            headerRowCount = 1,
            dataStartRowIndex = 1,
            dataEndRowIndex = 2,
            isSpecificationOnly = false,
            userModifiedStructure = true,
            learnedColumns = Array.Empty<object>()
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain("数据结束行超出表格范围");
    }

    [Fact]
    public async Task Confirm_WhenRegionsAreComplete_ShouldNotRequireLegacyTopLevelProjection()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "验收";
        worksheet.Cell(1, 4).Value = "备注";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(2, 3).Value = "OK";
        worksheet.Cell(2, 4).Value = "抽检";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            stream.ToArray(),
            $"smart-confirm-regions-only-{Guid.NewGuid():N}.xlsx");
        var customerId = await CreateCustomerAsync($"区域请求无需旧投影-{Guid.NewGuid():N}");

        var response = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            fileId,
            tableIndex = 0,
            customerId,
            templateName = "区域请求模板",
            regions = new[]
            {
                new
                {
                    regionId = "region-0",
                    regionIndex = 0,
                    headers = Array.Empty<string>(),
                    projectColumnIndex = 0,
                    specificationColumnIndex = 1,
                    acceptanceColumnIndex = 2,
                    remarkColumnIndex = 3,
                    headerRowIndex = 0,
                    headerRowCount = 1,
                    dataStartRowIndex = 1,
                    dataEndRowIndex = 1,
                    isSpecificationOnly = false
                }
            }
        }));

        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);
    }

    [Theory]
    [InlineData("project", "项目列不能为空")]
    [InlineData("acceptance", "验收列不能为空")]
    [InlineData("remark", "备注列不能为空")]
    public async Task Confirm_WhenExcelRequiredFieldColumnIsMissing_ShouldReject(
        string missingField,
        string expectedMessage)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "验收";
        worksheet.Cell(1, 4).Value = "备注";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(2, 3).Value = "OK";
        worksheet.Cell(2, 4).Value = "抽检";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            stream.ToArray(),
            $"smart-confirm-missing-{missingField}-{Guid.NewGuid():N}.xlsx");
        var customerId = await CreateCustomerAsync($"确认必选列-{missingField}-{Guid.NewGuid():N}");

        var response = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            fileId,
            tableIndex = 0,
            customerId,
            templateName = "必选列校验模板",
            regions = new[]
            {
                new
                {
                    regionId = "region-0",
                    regionIndex = 0,
                    headers = Array.Empty<string>(),
                    projectColumnIndex = missingField == "project" ? (int?)null : 0,
                    specificationColumnIndex = (int?)1,
                    acceptanceColumnIndex = missingField == "acceptance" ? (int?)null : 2,
                    remarkColumnIndex = missingField == "remark" ? (int?)null : 3,
                    headerRowIndex = 0,
                    headerRowCount = 1,
                    dataStartRowIndex = 1,
                    dataEndRowIndex = (int?)1,
                    isSpecificationOnly = false
                }
            }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain(expectedMessage);
    }

    [Theory]
    [InlineData(0, 2, 2, "Excel表头行数必须为1")]
    [InlineData(0, 1, 2, "Excel表头必须是数据起始行的上一行")]
    public async Task Confirm_WhenExcelHeaderIsNotLeafRowImmediatelyBeforeData_ShouldReject(
        int headerRowIndex,
        int headerRowCount,
        int dataStartRowIndex,
        string expectedMessage)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        for (var row = 1; row <= 3; row++)
        {
            worksheet.Cell(row, 1).Value = row < 3 ? "项目" : "外观";
            worksheet.Cell(row, 2).Value = row < 3 ? "规格" : "无划伤";
            worksheet.Cell(row, 3).Value = row < 3 ? "验收" : "OK";
            worksheet.Cell(row, 4).Value = row < 3 ? "备注" : "抽检";
        }
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            stream.ToArray(),
            $"smart-confirm-invalid-leaf-header-{Guid.NewGuid():N}.xlsx");
        var customerId = await CreateCustomerAsync($"确认末级表头-{Guid.NewGuid():N}");

        var response = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            fileId,
            tableIndex = 0,
            customerId,
            templateName = "末级表头校验模板",
            headers = new[] { "项目", "规格", "验收", "备注" },
            projectColumnIndex = 0,
            specificationColumnIndex = 1,
            acceptanceColumnIndex = 2,
            remarkColumnIndex = 3,
            headerRowIndex,
            headerRowCount,
            dataStartRowIndex,
            dataEndRowIndex = 2,
            isSpecificationOnly = false
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain(expectedMessage);
    }

    [Theory]
    [InlineData(false, "数据区域之间不能重叠")]
    [InlineData(true, "字段列不能重复")]
    public async Task Confirm_WhenSubmittedRegionsConflict_ShouldRejectBeforeSaving(
        bool duplicateColumns,
        string expectedMessage)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        for (var row = 1; row <= 8; row++)
        {
            worksheet.Cell(row, 1).Value = row is 1 or 5 ? "项目" : $"项目-{row}";
            worksheet.Cell(row, 2).Value = row is 1 or 5 ? "规格" : $"规格-{row}";
            worksheet.Cell(row, 3).Value = row is 1 or 5 ? "验收" : "OK";
            worksheet.Cell(row, 4).Value = row is 1 or 5 ? "备注" : string.Empty;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            stream.ToArray(),
            $"smart-confirm-invalid-regions-{Guid.NewGuid():N}.xlsx");
        var customerId = await CreateCustomerAsync($"确认区域冲突-{Guid.NewGuid():N}");

        var response = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            fileId,
            tableIndex = 0,
            customerId,
            templateName = "冲突区域模板",
            headers = new[] { "项目", "规格", "验收", "备注" },
            projectColumnIndex = 0,
            specificationColumnIndex = 1,
            acceptanceColumnIndex = 2,
            remarkColumnIndex = 3,
            headerRowIndex = 0,
            headerRowCount = 1,
            dataStartRowIndex = 1,
            dataEndRowIndex = 3,
            isSpecificationOnly = false,
            userModifiedStructure = true,
            learnedColumns = Array.Empty<object>(),
            regions = new object[]
            {
                new
                {
                    regionId = "region-0",
                    regionIndex = 0,
                    headers = new[] { "项目", "规格", "验收", "备注" },
                    projectColumnIndex = 0,
                    specificationColumnIndex = duplicateColumns ? 0 : 1,
                    acceptanceColumnIndex = 2,
                    remarkColumnIndex = 3,
                    headerRowIndex = 0,
                    headerRowCount = 1,
                    dataStartRowIndex = 1,
                    dataEndRowIndex = 4,
                    isSpecificationOnly = false
                },
                new
                {
                    regionId = "region-1",
                    regionIndex = 1,
                    headers = new[] { "项目", "规格", "验收", "备注" },
                    projectColumnIndex = 0,
                    specificationColumnIndex = 1,
                    acceptanceColumnIndex = 2,
                    remarkColumnIndex = 3,
                    headerRowIndex = duplicateColumns ? 4 : 3,
                    headerRowCount = 1,
                    dataStartRowIndex = duplicateColumns ? 5 : 4,
                    dataEndRowIndex = 7,
                    isSpecificationOnly = false
                }
            }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Message.Should().Contain(expectedMessage);
    }

    private Task<HttpResponseMessage> PostValidConfirmAsync(int customerId, bool userModifiedStructure)
    {
        return _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            customerId,
            templateName = "结构校验模板",
            headers = new[] { "项目", "规格", "验收", "备注" },
            projectColumnIndex = 0,
            specificationColumnIndex = 1,
            acceptanceColumnIndex = 2,
            remarkColumnIndex = 3,
            headerRowIndex = 0,
            headerRowCount = 1,
            dataStartRowIndex = 1,
            isSpecificationOnly = false,
            userModifiedStructure,
            learnedColumns = Array.Empty<object>()
        }));
    }

    private async Task<int> CreateCustomerAsync(string name)
    {
        var response = await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("id").GetInt32();
    }
}

public class SmartConfigConfirmHeaderRefreshRegressionTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SmartConfigConfirmHeaderRefreshRegressionTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Confirm_WhenHeaderCoordinatesWereModified_ShouldRefreshHeadersAndReuseTemplate()
    {
        var customerId = await CreateCustomerAsync($"确认表头刷新-{Guid.NewGuid():N}");
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateHeaderCorrectionExcelBytes(),
            "smart-confirm-refresh-headers.xlsx");

        var confirmResponse = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            fileId,
            tableIndex = 0,
            customerId,
            templateName = "修正后的结构模板",
            headers = new[] { "旧项目", "旧规格", "旧验收", "旧备注" },
            projectColumnIndex = 0,
            specificationColumnIndex = 1,
            acceptanceColumnIndex = 2,
            remarkColumnIndex = 3,
            headerRowIndex = 1,
            headerRowCount = 1,
            dataStartRowIndex = 2,
            isSpecificationOnly = false,
            userModifiedStructure = true,
            learnedColumns = new[]
            {
                new { header = "旧项目", targetField = 1 },
                new { header = "旧规格", targetField = 2 },
                new { header = "旧验收", targetField = 3 },
                new { header = "旧备注", targetField = 4 }
            }
        }));
        var confirmText = await confirmResponse.Content.ReadAsStringAsync();
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.OK, confirmText);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var template = await db.DocumentTemplates.SingleAsync(item => item.CustomerId == customerId);
            JsonSerializer.Deserialize<string[]>(template.HeadersJson)
                .Should().Equal("新项目", "新规格", "新验收", "新备注");

            var learnedPatterns = await db.ColumnMappingRules
                .Where(rule => rule.CustomerId == customerId && rule.Source == ColumnMappingRuleSource.Learned)
                .OrderBy(rule => rule.TargetField)
                .Select(rule => rule.Pattern)
                .ToListAsync();
            learnedPatterns.Should().Equal("新项目", "新规格", "新验收", "新备注");
        }

        var reuseFileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            CreateCleanCorrectedHeaderExcelBytes(),
            "smart-confirm-reuse-refreshed-template.xlsx");
        var recognizeResponse = await _client.PostAsync("/api/smart-config/recognize", ApiClientJson.ToJsonContent(new
        {
            fileId = reuseFileId,
            customerId
        }));
        var recognizeText = await recognizeResponse.Content.ReadAsStringAsync();
        recognizeResponse.StatusCode.Should().Be(HttpStatusCode.OK, recognizeText);
        var body = await recognizeResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var table = body.Data.GetProperty("tables").EnumerateArray().Single();
        table.GetProperty("source").GetString().Should().Be("Template");
        table.GetProperty("headers").EnumerateArray().Select(item => item.GetString())
            .Should().Equal("新项目", "新规格", "新验收", "新备注");
    }

    [Fact]
    public async Task Confirm_WhenWordHeaderCoordinatesWereModified_ShouldRefreshHeadersAndLearnedRules()
    {
        var customerId = await CreateCustomerAsync($"确认Word表头刷新-{Guid.NewGuid():N}");
        var fileId = await SmartConfigRecognizeTestFiles.UploadWordAsync(
            _client,
            SmartConfigRecognizeTestFiles.CreateWordBytes(
            [
                ["旧项目", "旧规格", "旧验收", "旧备注"],
                ["新项目", "新规格", "新验收", "新备注"],
                ["外观", "无划伤", "OK", "抽检"]
            ]),
            "smart-confirm-refresh-word-headers.docx");

        var response = await _client.PostAsync("/api/smart-config/confirm", ApiClientJson.ToJsonContent(new
        {
            fileId,
            tableIndex = 0,
            customerId,
            templateName = "Word修正结构模板",
            headers = new[] { "旧项目", "旧规格", "旧验收", "旧备注" },
            projectColumnIndex = 0,
            specificationColumnIndex = 1,
            acceptanceColumnIndex = 2,
            remarkColumnIndex = 3,
            headerRowIndex = 1,
            headerRowCount = 1,
            dataStartRowIndex = 2,
            isSpecificationOnly = false,
            userModifiedStructure = true,
            learnedColumns = new[]
            {
                new { header = "旧项目", targetField = 1 },
                new { header = "旧规格", targetField = 2 },
                new { header = "旧验收", targetField = 3 },
                new { header = "旧备注", targetField = 4 }
            }
        }));
        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, responseText);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var template = await db.DocumentTemplates.SingleAsync(item => item.CustomerId == customerId);
        JsonSerializer.Deserialize<string[]>(template.HeadersJson)
            .Should().Equal("新项目", "新规格", "新验收", "新备注");
        var learnedPatterns = await db.ColumnMappingRules
            .Where(rule => rule.CustomerId == customerId && rule.Source == ColumnMappingRuleSource.Learned)
            .OrderBy(rule => rule.TargetField)
            .Select(rule => rule.Pattern)
            .ToListAsync();
        learnedPatterns.Should().Equal("新项目", "新规格", "新验收", "新备注");
    }

    private async Task<int> CreateCustomerAsync(string name)
    {
        var response = await _client.PostAsync("/api/customers", ApiClientJson.ToJsonContent(new { name }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return json.Data.GetProperty("id").GetInt32();
    }

    private static byte[] CreateHeaderCorrectionExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "旧项目";
        worksheet.Cell(1, 2).Value = "旧规格";
        worksheet.Cell(1, 3).Value = "旧验收";
        worksheet.Cell(1, 4).Value = "旧备注";
        worksheet.Cell(2, 1).Value = "新项目";
        worksheet.Cell(2, 2).Value = "新规格";
        worksheet.Cell(2, 3).Value = "新验收";
        worksheet.Cell(2, 4).Value = "新备注";
        worksheet.Cell(3, 1).Value = "外观";
        worksheet.Cell(3, 2).Value = "无划伤";
        worksheet.Cell(3, 3).Value = "OK";
        worksheet.Cell(3, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateCleanCorrectedHeaderExcelBytes()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "新项目";
        worksheet.Cell(1, 2).Value = "新规格";
        worksheet.Cell(1, 3).Value = "新验收";
        worksheet.Cell(1, 4).Value = "新备注";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "无划伤";
        worksheet.Cell(2, 3).Value = "OK";
        worksheet.Cell(2, 4).Value = "抽检";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
