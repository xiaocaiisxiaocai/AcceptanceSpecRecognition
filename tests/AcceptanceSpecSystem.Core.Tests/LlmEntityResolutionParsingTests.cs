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
}
