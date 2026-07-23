using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Strategies;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests;

public class ColumnHeaderRuleMatcherTests
{
    [Fact]
    public void Match_WhenShortHeadersAreSimilar_ShouldKeepFuzzyMatching()
    {
        var rule = new ColumnHeaderMappingRule(
            ColumnType.Project,
            ColumnHeaderMatchMode.Contains,
            "alphaobject");

        var result = ColumnHeaderRuleMatcher.Match("alphazbjfct", rule);

        result.Matched.Should().BeTrue();
        result.Confidence.Should().BeGreaterThan(0.7);
    }

    [Fact]
    public void Match_WhenHeaderAndPatternContainEquivalentWhitespace_ShouldNormalizeBothSides()
    {
        var rule = new ColumnHeaderMappingRule(
            ColumnType.Specification,
            ColumnHeaderMatchMode.Equals,
            "规格\t要求");

        ColumnHeaderRuleMatcher.IsMatch("  规格\r\n要求  ", rule).Should().BeTrue();
    }

    [Fact]
    public void Match_WhenFuzzyInputsExceedBudget_ShouldNotAllocateUnboundedDistanceMatrix()
    {
        var header = new string('a', 100_000) + "x";
        var pattern = new string('a', 100_000) + "y";
        var rule = new ColumnHeaderMappingRule(
            ColumnType.Project,
            ColumnHeaderMatchMode.Contains,
            pattern);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var matched = ColumnHeaderRuleMatcher.IsMatch(header, rule);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        matched.Should().BeFalse();
        allocatedBytes.Should().BeLessThan(16_384, "超长输入必须在空白归一化和编辑距离分配前被拒绝");
    }

    [Fact]
    public void Match_WhenRegexPatternExceedsBudget_ShouldRejectRule()
    {
        var rule = new ColumnHeaderMappingRule(
            ColumnType.Project,
            ColumnHeaderMatchMode.Regex,
            new string('a', 257));

        ColumnHeaderRuleMatcher.IsMatch(new string('a', 257), rule).Should().BeFalse();
    }
}
