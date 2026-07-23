using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class SmartConfigRegionHeaderMappingTests
{
    [Fact]
    public void HeaderCandidateRank_WhenConfidenceTies_ShouldPreferCustomerLearnedRule()
    {
        var matcherType = typeof(SmartConfigurationAppService).Assembly.GetType(
            "AcceptanceSpecSystem.Application.Services.HeaderKeywordMatcher")!;
        var matcher = matcherType.GetMethod("FromRules")!.Invoke(null,
        [
            new[]
            {
                new ColumnHeaderMappingRule(
                    ColumnType.Remark,
                    ColumnHeaderMatchMode.Equals,
                    "Remark"),
                new ColumnHeaderMappingRule(
                    ColumnType.Remark,
                    ColumnHeaderMatchMode.Equals,
                    "備註",
                    Priority: 200,
                    IsCustomerSpecific: true)
            }
        ])!;
        var getRank = matcherType.GetMethod("GetRank")!;

        var builtInRank = getRank.Invoke(matcher, [ColumnType.Remark, "Remark"])!;
        var learnedRank = getRank.Invoke(matcher, [ColumnType.Remark, "備註"])!;
        var rankType = learnedRank.GetType();

        rankType.GetProperty("Confidence")!.GetValue(learnedRank)
            .Should().Be(rankType.GetProperty("Confidence")!.GetValue(builtInRank));
        rankType.GetProperty("IsCustomerSpecific")!.GetValue(builtInRank)
            .Should().Be(false);
        rankType.GetProperty("IsCustomerSpecific")!.GetValue(learnedRank)
            .Should().Be(true);
        rankType.GetProperty("Priority")!.GetValue(learnedRank)
            .Should().Be(200);
    }

    [Fact]
    public void RegionHeaderMapping_ShouldDistinguishProjectDetailHierarchyFromARealLeftColumnMove()
    {
        var serviceType = typeof(SmartConfigurationAppService);
        var matcherType = serviceType.Assembly.GetType(
            "AcceptanceSpecSystem.Application.Services.HeaderKeywordMatcher")!;
        var matcher = matcherType.GetMethod("FromRules")!.Invoke(null,
        [
            new[]
            {
                new ColumnHeaderMappingRule(ColumnType.Project, ColumnHeaderMatchMode.Equals, "项目"),
                new ColumnHeaderMappingRule(ColumnType.Specification, ColumnHeaderMatchMode.Equals, "细项"),
                new ColumnHeaderMappingRule(ColumnType.Specification, ColumnHeaderMatchMode.Equals, "规格"),
                new ColumnHeaderMappingRule(ColumnType.Acceptance, ColumnHeaderMatchMode.Equals, "验收"),
                new ColumnHeaderMappingRule(ColumnType.Remark, ColumnHeaderMatchMode.Equals, "备注")
            }
        ])!;
        var apply = serviceType.GetMethod(
            "ApplyRegionHeaderMappings",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var fallback = new SmartConfigurationRecognizedTable
        {
            ProjectColumnIndex = 2,
            SpecificationColumnIndex = 3,
            AcceptanceColumnIndex = 8,
            RemarkColumnIndex = 9
        };

        var hierarchy = (SmartConfigurationRecognizedTable)apply.Invoke(null,
            [fallback, new[] { "", "项目", "细项", "规格", "", "", "", "", "验收", "备注" }, matcher])!;
        hierarchy.ProjectColumnIndex.Should().Be(2);
        hierarchy.SpecificationColumnIndex.Should().Be(3);

        var moved = (SmartConfigurationRecognizedTable)apply.Invoke(null,
            [fallback, new[] { "", "项目", "序号", "规格", "", "", "", "", "验收", "备注" }, matcher])!;
        moved.ProjectColumnIndex.Should().Be(1);
        moved.SpecificationColumnIndex.Should().Be(3);
    }

    [Fact]
    public void RegionHeaderMapping_WhenGroupedRemarkHeaderHasLeafSemantic_ShouldKeepTheMappedColumn()
    {
        var serviceType = typeof(SmartConfigurationAppService);
        var matcherType = serviceType.Assembly.GetType(
            "AcceptanceSpecSystem.Application.Services.HeaderKeywordMatcher")!;
        var matcher = matcherType.GetMethod("FromRules")!.Invoke(null,
        [
            new[]
            {
                new ColumnHeaderMappingRule(ColumnType.Project, ColumnHeaderMatchMode.Equals, "项目"),
                new ColumnHeaderMappingRule(ColumnType.Specification, ColumnHeaderMatchMode.Equals, "规格"),
                new ColumnHeaderMappingRule(ColumnType.Acceptance, ColumnHeaderMatchMode.Equals, "OK/NG"),
                new ColumnHeaderMappingRule(ColumnType.Remark, ColumnHeaderMatchMode.Equals, "Remark"),
                new ColumnHeaderMappingRule(ColumnType.Remark, ColumnHeaderMatchMode.Equals, "備註")
            }
        ])!;
        var apply = serviceType.GetMethod(
            "ApplyRegionHeaderMappings",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var fallback = new SmartConfigurationRecognizedTable
        {
            ProjectColumnIndex = 2,
            SpecificationColumnIndex = 3,
            AcceptanceColumnIndex = 8,
            RemarkColumnIndex = 9
        };
        var headers = new[]
        {
            "三、安装需求：", "项目", "细项", "规格", "", "", "", "",
            "厂商确认 / OK/NG", "厂商确认 / Remark", "", "", "", "", "備註"
        };

        var result = (SmartConfigurationRecognizedTable)apply.Invoke(null, [fallback, headers, matcher])!;

        result.AcceptanceColumnIndex.Should().Be(8);
        result.RemarkColumnIndex.Should().Be(9);
    }
}
