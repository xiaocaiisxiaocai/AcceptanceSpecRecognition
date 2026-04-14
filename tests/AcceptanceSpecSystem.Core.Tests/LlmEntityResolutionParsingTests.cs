using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.Matching.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Core.Tests;

public class LlmEntityResolutionParsingTests
{
    [Fact]
    public void TryParseEntityResolutionResult_ShouldParseStrictJson()
    {
        var service = new LlmMatchingAssistService(
            null!,
            null!,
            null!,
            NullLogger<LlmMatchingAssistService>.Instance);

        var ok = service.TryParseEntityResolutionResult(
            "{\"relation\":\"alias_same\",\"confidence\":0.93,\"normalizedEntity\":\"松下\",\"reason\":\"中英文别名一致\"}",
            out var result);

        ok.Should().BeTrue();
        result.Relation.Should().Be(LlmEntityRelation.AliasSame);
        result.Confidence.Should().Be(0.93);
        result.NormalizedEntity.Should().Be("松下");
        result.Reason.Should().Be("中英文别名一致");
    }

    [Fact]
    public void PromptTemplateCatalog_ShouldExposeMatchingEntityResolutionScene()
    {
        var service = new PromptTemplateValidationService();
        var definition = PromptTemplateCatalog.GetByScene(PromptTemplateScene.MatchingEntityResolution);

        var result = service.Validate(definition, definition.DefaultContent);

        result.IsValid.Should().BeTrue();
        result.StructuredOutputIsValid.Should().BeTrue();
        result.ExampleJson.Should().Contain("\"relation\"");
        result.ExampleJson.Should().Contain("\"confidence\"");
    }

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

    [Fact]
    public void PromptTemplateCatalog_ShouldExposeMatchingEquivalenceAdjudicationScene()
    {
        var service = new PromptTemplateValidationService();
        var definition = PromptTemplateCatalog.GetByScene(PromptTemplateScene.MatchingEquivalenceAdjudication);

        var result = service.Validate(definition, definition.DefaultContent);

        result.IsValid.Should().BeTrue();
        result.StructuredOutputIsValid.Should().BeTrue();
        result.ExampleJson.Should().Contain("\"verdict\"");
        result.ExampleJson.Should().Contain("\"reasonType\"");
        result.ExampleJson.Should().Contain("\"confidence\"");
    }
}
