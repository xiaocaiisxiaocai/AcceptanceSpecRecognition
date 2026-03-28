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

        // 繁体不被转换
        session.Process("寬度").Should().Be("寬度");
        // 同义词不被替换
        session.Process("松下").Should().Be("松下");
        // 单位不被展开
        session.Process("厘米").Should().Be("厘米");
    }
}
