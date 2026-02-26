using AcceptanceSpecSystem.Core.Matching.Services;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests;

/// <summary>
/// TextSimilarityService（Levenshtein）单元测试
/// </summary>
public class TextSimilarityServiceTests
{
    private readonly TextSimilarityService _service = new();

    [Fact]
    public void ComputeSimilarity_IdenticalTexts_ShouldReturnOne()
    {
        var score = _service.ComputeSimilarity("ABC", "ABC");
        score.Should().Be(1.0);
    }

    [Fact]
    public void ComputeSimilarity_CompletelyDifferent_ShouldReturnLow()
    {
        var score = _service.ComputeSimilarity("AAAA", "ZZZZ");
        score.Should().BeLessThan(0.3);
    }

    [Fact]
    public void ComputeSimilarity_EmptyStrings_ShouldReturnOne()
    {
        var score = _service.ComputeSimilarity("", "");
        score.Should().Be(1.0);
    }

    [Fact]
    public void ComputeSimilarity_OneEmpty_ShouldReturnZero()
    {
        var score = _service.ComputeSimilarity("ABC", "");
        score.Should().Be(0.0);
    }
}
