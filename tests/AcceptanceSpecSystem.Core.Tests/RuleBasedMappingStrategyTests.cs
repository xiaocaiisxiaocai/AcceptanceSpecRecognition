using AcceptanceSpecSystem.Core.Documents.Intelligence;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Strategies;
using AcceptanceSpecSystem.Core.TextProcessing.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Core.Tests;

public class RuleBasedMappingStrategyTests
{
    private static RuleBasedMappingStrategy CreateStrategy() => new(
        new MinimalTextPreprocessingPipeline(),
        NullLogger<RuleBasedMappingStrategy>.Instance);

    private static IReadOnlyDictionary<ColumnType, IReadOnlyList<string>> DefaultSynonyms() =>
        ColumnMappingRuleDefaults.GetAll();

    [Fact]
    public async Task IdentifyAsync_WithTraditionalAcceptanceHeaders_ShouldMapColumns()
    {
        var strategy = CreateStrategy();

        var result = await strategy.IdentifyAsync(
            ["項次", "驗收項目", "", "驗收規格", "驗收方法", "設備商確認", "備註"],
            [
                ["1", "投收板機設備制程能力", "設備流向", "依主設備流向", "裝機時檢查", "■OK   □NG", ""]
            ],
            DefaultSynonyms());

        result.Mapping.ProjectColumn.Should().Be(1);
        result.Mapping.SpecificationColumn.Should().Be(3);
        result.Mapping.AcceptanceColumn.Should().Be(5);
        result.Mapping.RemarkColumn.Should().Be(6);
        result.Confidence.Should().BeGreaterThan(0.85);
    }

    [Fact]
    public async Task IdentifyAsync_WithoutDatabaseSynonyms_ShouldNotMapHeaderKeywordsFromHardcodedDefaults()
    {
        var strategy = CreateStrategy();

        var result = await strategy.IdentifyAsync(
            ["项目", "规格内容", "验收结果", "备注"],
            [
                ["外观", "无划伤", "OK", "抽检"]
            ],
            new Dictionary<ColumnType, IReadOnlyList<string>>
            {
                [ColumnType.Project] = [],
                [ColumnType.Specification] = [],
                [ColumnType.Acceptance] = [],
                [ColumnType.Remark] = []
            });

        result.Mapping.ProjectColumn.Should().BeNull();
        result.Mapping.SpecificationColumn.Should().Be(1);
        result.Mapping.AcceptanceColumn.Should().Be(2);
        result.Mapping.RemarkColumn.Should().BeNull();
        result.Confidence.Should().Be(0);
    }

    [Fact]
    public async Task IdentifyAsync_WithAcceptanceStandardHeader_ShouldPreferAcceptance()
    {
        var strategy = CreateStrategy();

        var result = await strategy.IdentifyAsync(
            ["序号", "标识", "评议项目", "评议标准要求", "验收标准", "验收方式", "评议大纲", "供应商回复", "备注"],
            [
                ["1", "A", "外观", "无明显划伤", "", "目视", "", "", "待确认"]
            ],
            DefaultSynonyms());

        result.Mapping.ProjectColumn.Should().Be(2);
        result.Mapping.SpecificationColumn.Should().Be(3);
        result.Mapping.AcceptanceColumn.Should().Be(4);
        result.Mapping.RemarkColumn.Should().Be(8);
    }

    [Fact]
    public async Task IdentifyAsync_WithInspectionMethodHeader_ShouldNotMapAcceptance()
    {
        var strategy = CreateStrategy();

        var result = await strategy.IdentifyAsync(
            ["检查方式"],
            [],
            [
                new ColumnHeaderMappingRule(
                    ColumnType.Acceptance,
                    ColumnHeaderMatchMode.Contains,
                    "检查")
            ]);

        result.Mapping.AcceptanceColumn.Should().BeNull();
    }

    [Theory]
    [InlineData("确认")]
    [InlineData("结果")]
    [InlineData("结论")]
    [InlineData("判定")]
    [InlineData("OK")]
    [InlineData("NG")]
    public async Task IdentifyAsync_WithAcceptanceMethodAndResultSignal_ShouldMapAcceptance(string resultSignal)
    {
        var strategy = CreateStrategy();

        var result = await strategy.IdentifyAsync(
            [$"验收方式{resultSignal}"],
            [],
            [
                new ColumnHeaderMappingRule(
                    ColumnType.Acceptance,
                    ColumnHeaderMatchMode.Contains,
                    "验收")
            ]);

        result.Mapping.AcceptanceColumn.Should().Be(0);
    }

    [Theory]
    [InlineData("是否响应", "供应商说明")]
    [InlineData("是否相应", "供应商说明")]
    public async Task IdentifyAsync_WithG5PsdMetadataColumns_ShouldMapBusinessColumns(
        string acceptanceHeader,
        string remarkHeader)
    {
        var strategy = CreateStrategy();

        var result = await strategy.IdentifyAsync(
            [
                "PSD_CODE / 技术标准编码",
                "PSD_CATCLSCODE / 设备小类编码",
                "PSD_NUM / *序号",
                "PSD_IMPORTANCE*关键指标",
                "PSD_MODIFY / 可修改标识",
                "PSD_CONFORM / 符合性验证通过",
                "PSD_ACCEPTANCE / 最终验收通过",
                "评议项目",
                "评议标准要求",
                "验收方式",
                "评议大纲",
                acceptanceHeader,
                remarkHeader
            ],
            [
                [
                    "A02-035-02-T1",
                    "A02-035-02",
                    "1.3.01",
                    "*",
                    "N",
                    "是",
                    "是",
                    "设备功能",
                    "本设备应具备放板、防叠板、居中、传送以及速度调节装置等。",
                    "/",
                    "A.设备功能/工艺流程要求",
                    "是",
                    "满足"
                ]
            ],
            DefaultSynonyms());

        result.Mapping.ProjectColumn.Should().Be(7);
        result.Mapping.SpecificationColumn.Should().Be(8);
        result.Mapping.AcceptanceColumn.Should().Be(11);
        result.Mapping.RemarkColumn.Should().Be(12);
    }

    [Fact]
    public async Task IdentifyAsync_WithG5PsdAcceptanceMetadataAndUnknownBusinessAcceptanceHeader_ShouldNotMapMetadataAsAcceptance()
    {
        var strategy = CreateStrategy();

        var result = await strategy.IdentifyAsync(
            [
                "PSD_ACCEPTANCE / 最终验收通过",
                "评议项目",
                "评议标准要求",
                "评审结论",
                "供应商说明"
            ],
            [
                ["是", "设备功能", "本设备应具备放板、防叠板、居中、传送以及速度调节装置等。", "通过", "满足"]
            ],
            DefaultSynonyms());

        result.Mapping.ProjectColumn.Should().Be(1);
        result.Mapping.SpecificationColumn.Should().Be(2);
        result.Mapping.AcceptanceColumn.Should().NotBe(0);
    }

    [Fact]
    public async Task IdentifyAsync_WithExtraSynonyms_ShouldUseCustomerLearnedTerms()
    {
        var strategy = CreateStrategy();

        var result = await strategy.IdentifyAsync(
            ["检查对象", "管制条件", "供应商回复", "补充说明"],
            [
                ["外观", "表面不得有明显划伤", "OK", "抽检"]
            ],
            new Dictionary<ColumnType, IReadOnlyList<string>>
            {
                [ColumnType.Project] = ["检查对象"],
                [ColumnType.Specification] = ["管制条件"],
                [ColumnType.Acceptance] = ["供应商回复"],
                [ColumnType.Remark] = ["补充说明"]
            });

        result.Mapping.ProjectColumn.Should().Be(0);
        result.Mapping.SpecificationColumn.Should().Be(1);
        result.Mapping.AcceptanceColumn.Should().Be(2);
        result.Mapping.RemarkColumn.Should().Be(3);
        result.Confidence.Should().BeGreaterThan(0.85);
    }

    [Fact]
    public async Task IdentifyAsync_WithEqualsRule_ShouldRequireWholeHeaderMatch()
    {
        var strategy = CreateStrategy();

        var result = await strategy.IdentifyAsync(
            ["检查对象说明"],
            [],
            [
                new ColumnHeaderMappingRule(
                    ColumnType.Project,
                    ColumnHeaderMatchMode.Equals,
                    "检查对象")
            ]);

        result.Mapping.ProjectColumn.Should().BeNull();
    }

    [Fact]
    public async Task IdentifyAsync_WithContainsRule_ShouldMatchContainedHeaderText()
    {
        var strategy = CreateStrategy();

        var result = await strategy.IdentifyAsync(
            ["设备检查对象"],
            [],
            [
                new ColumnHeaderMappingRule(
                    ColumnType.Project,
                    ColumnHeaderMatchMode.Contains,
                    "检查对象")
            ]);

        result.Mapping.ProjectColumn.Should().Be(0);
    }

    [Fact]
    public async Task IdentifyAsync_WithExactContainsRule_ShouldKeepExactMatchConfidence()
    {
        var strategy = CreateStrategy();

        var result = await strategy.IdentifyAsync(
            ["检查对象"],
            [],
            [
                new ColumnHeaderMappingRule(
                    ColumnType.Project,
                    ColumnHeaderMatchMode.Contains,
                    "检查对象")
            ]);

        result.Details.Should().ContainSingle().Which.Confidence.Should().Be(0.99);
    }

    [Fact]
    public async Task IdentifyAsync_WithRegexRule_ShouldMatchRegularExpression()
    {
        var strategy = CreateStrategy();

        var result = await strategy.IdentifyAsync(
            ["检查设备对象"],
            [],
            [
                new ColumnHeaderMappingRule(
                    ColumnType.Project,
                    ColumnHeaderMatchMode.Regex,
                    "检查.*对象")
            ]);

        result.Mapping.ProjectColumn.Should().Be(0);
    }

    [Fact]
    public async Task IdentifyAsync_WithEmptyHeaders_ShouldReturnZeroConfidence()
    {
        var strategy = CreateStrategy();

        var result = await strategy.IdentifyAsync([], []);

        result.Mapping.ProjectColumn.Should().BeNull();
        result.Mapping.SpecificationColumn.Should().BeNull();
        result.Mapping.AcceptanceColumn.Should().BeNull();
        result.Mapping.RemarkColumn.Should().BeNull();
        result.Confidence.Should().Be(0);
        result.Details.Should().BeEmpty();
    }

    [Fact]
    public async Task IdentifyAsync_WithOnlyHeaderRow_ShouldMapKnownHeadersWithoutSampleRows()
    {
        var strategy = CreateStrategy();

        var result = await strategy.IdentifyAsync(
            ["项目", "规格", "验收结果", "备注"],
            [],
            DefaultSynonyms());

        result.Mapping.ProjectColumn.Should().Be(0);
        result.Mapping.SpecificationColumn.Should().Be(1);
        result.Mapping.AcceptanceColumn.Should().Be(2);
        result.Mapping.RemarkColumn.Should().Be(3);
        result.Confidence.Should().BeGreaterThan(0.85);
    }

    [Fact]
    public async Task IdentifyAsync_WithDataRowsButNoHeaders_ShouldNotAutoApplyCandidate()
    {
        var strategy = CreateStrategy();

        var result = await strategy.IdentifyAsync(
            ["", "", "", ""],
            [
                ["外观", "无明显划伤", "OK", "抽检"],
                ["尺寸", "长度 10mm", "NG", ""]
        ]);

        result.Mapping.ProjectColumn.Should().BeNull();
        result.Mapping.SpecificationColumn.Should().Be(1);
        result.Mapping.AcceptanceColumn.Should().Be(2);
        result.Mapping.RemarkColumn.Should().BeNull();
        result.Confidence.Should().Be(0);
    }

    [Fact]
    public async Task IdentifyAsync_WhenRequiredColumnsAreLowConfidence_ShouldWeightRequiredColumnsMoreThanOptionalColumns()
    {
        var strategy = CreateStrategy();

        var result = await strategy.IdentifyAsync(
            ["alphazbjfct", "betacondztzon", "gammareply", "deltacomment"],
            [
                ["外观", "无明显划伤", "OK", "抽检"]
            ],
            new Dictionary<ColumnType, IReadOnlyList<string>>
            {
                [ColumnType.Project] = ["alphaobject"],
                [ColumnType.Specification] = ["betacondition"],
                [ColumnType.Acceptance] = ["gammareply"],
                [ColumnType.Remark] = ["deltacomment"]
            });

        result.Mapping.ProjectColumn.Should().Be(0);
        result.Mapping.SpecificationColumn.Should().Be(1);
        result.Mapping.AcceptanceColumn.Should().Be(2);
        result.Mapping.RemarkColumn.Should().Be(3);
        result.Confidence.Should().BeLessThan(0.85);
    }
}
