using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Strategies;
using AcceptanceSpecSystem.Core.TextProcessing.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Core.Tests;

public class RuleBasedMappingStrategyTests
{
    [Fact]
    public async Task IdentifyAsync_WithTraditionalAcceptanceHeaders_ShouldMapColumns()
    {
        var strategy = new RuleBasedMappingStrategy(
            new MinimalTextPreprocessingPipeline(),
            NullLogger<RuleBasedMappingStrategy>.Instance);

        var result = await strategy.IdentifyAsync(
            ["項次", "驗收項目", "", "驗收規格", "驗收方法", "設備商確認", "備註"],
            [
                ["1", "投收板機設備制程能力", "設備流向", "依主設備流向", "裝機時檢查", "■OK   □NG", ""]
            ]);

        result.Mapping.ProjectColumn.Should().Be(1);
        result.Mapping.SpecificationColumn.Should().Be(3);
        result.Mapping.AcceptanceColumn.Should().Be(5);
        result.Mapping.RemarkColumn.Should().Be(6);
        result.Confidence.Should().BeGreaterThan(0.85);
    }
}
