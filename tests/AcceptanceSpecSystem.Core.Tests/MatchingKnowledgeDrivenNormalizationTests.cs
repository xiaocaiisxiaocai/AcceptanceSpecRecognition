using AcceptanceSpecSystem.Core.TextProcessing.Services;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests;

public class MatchingKnowledgeDrivenNormalizationTests
{
    [Fact]
    public async Task MinimalTextPreprocessingPipeline_ShouldOnlyNormalizeWhitespace()
    {
        var pipeline = new MinimalTextPreprocessingPipeline();

        var session = await pipeline.CreateSessionAsync();

        session.Process("  PASS \r\n NG\t ").Should().Be("PASS NG");
        session.Process("宽尺寸   <  0.5cm").Should().Be("宽尺寸 < 0.5cm");
    }
}
