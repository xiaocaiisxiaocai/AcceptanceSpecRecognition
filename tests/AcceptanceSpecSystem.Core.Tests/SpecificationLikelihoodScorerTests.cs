using AcceptanceSpecSystem.Core.Documents.Intelligence.Scoring;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests;

public class SpecificationLikelihoodScorerTests
{
    [Fact]
    public void Calculate_ShouldGiveHigherScoreToSpecificationTextThanProjectName()
    {
        var specificationScore = SpecificationLikelihoodScorer.Calculate("长度 10mm，公差 ±0.1mm，不得有明显变形");
        var projectScore = SpecificationLikelihoodScorer.Calculate("外观");

        specificationScore.Should().BeGreaterThan(projectScore + 0.35);
        specificationScore.Should().BeGreaterThanOrEqualTo(0.55);
    }

    [Fact]
    public void Average_ShouldIgnoreBlankValues()
    {
        var score = SpecificationLikelihoodScorer.Average(["", "  ", "无划伤、无明显变形"]);

        score.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CountTechnicalPatternValues_ShouldDetectUnitAndRangeSamples()
    {
        var count = SpecificationLikelihoodScorer.CountTechnicalPatternValues(
            ["10-20mm", "重量 ≤ 5kg", "外观"]);

        count.Should().Be(2);
    }
}
