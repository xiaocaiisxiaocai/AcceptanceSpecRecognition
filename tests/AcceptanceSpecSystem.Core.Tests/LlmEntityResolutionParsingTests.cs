using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.Matching.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Core.Tests;

public class LlmEntityResolutionParsingTests
{
    [Fact]
    public void TryParseEquivalenceAdjudicationResult_ShouldParseStrictJson()
    {
        var service = new LlmMatchingAssistService(
            null!,
            null!,
            null!,
            NullLogger<LlmMatchingAssistService>.Instance);

        var ok = service.TryParseAdjudicationResult(
            "{\"verdict\":\"equivalent\",\"reasonType\":\"equivalent_expression\",\"reason\":\"约等于与≈等价\",\"confidence\":0.92}",
            out var result);

        ok.Should().BeTrue();
        result.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
        result.ReasonType.Should().Be(LlmEquivalenceReasonType.EquivalentExpression);
        result.Reason.Should().Be("约等于与≈等价");
        result.Confidence.Should().Be(0.92);
    }

    [Theory]
    [InlineData("{\"verdict\":\"equivalent\",\"reasonType\":\"semantic_difference\",\"reason\":\"含义不同\",\"confidence\":0.91}")]
    [InlineData("{\"verdict\":\"different\",\"reasonType\":\"format_only\",\"reason\":\"只有格式差异\",\"confidence\":0.81}")]
    [InlineData("{\"verdict\":\"different\",\"reasonType\":\"uncertain\",\"reason\":\"还不确定\",\"confidence\":0.4}")]
    [InlineData("{\"verdict\":\"uncertain\",\"reasonType\":\"equivalent_expression\",\"reason\":\"似乎等价\",\"confidence\":0.51}")]
    public void TryParseEquivalenceAdjudicationResult_ShouldRejectInconsistentVerdictAndReasonType(string raw)
    {
        var service = new LlmMatchingAssistService(
            null!,
            null!,
            null!,
            NullLogger<LlmMatchingAssistService>.Instance);

        var ok = service.TryParseAdjudicationResult(raw, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    public void PromptTemplateCatalog_ShouldExposeMatchingEquivalenceAdjudicationScene()
    {
        var service = new PromptTemplateValidationService();
        var definition = PromptTemplateCatalog.GetByScene(PromptTemplateScene.MatchingEquivalenceAdjudication);

        var result = service.Validate(definition, definition.DefaultContent);

        result.IsValid.Should().BeTrue();
        result.StructuredOutputIsValid.Should().BeTrue();
        definition.LegacyDefaultContent.Should().NotBeNullOrWhiteSpace();
        definition.DefaultContent.Should().Contain("reasonType 只允许");
        definition.DefaultContent.Should().Contain("format_only");
        definition.DefaultContent.Should().Contain("semantic_difference");
        result.ExampleJson.Should().Contain("\"verdict\"");
        result.ExampleJson.Should().Contain("\"reasonType\"");
        result.ExampleJson.Should().Contain("\"confidence\"");
    }
}
