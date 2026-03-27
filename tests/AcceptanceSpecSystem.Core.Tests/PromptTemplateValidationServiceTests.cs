using AcceptanceSpecSystem.Core.Matching.Services;
using AcceptanceSpecSystem.Core.Matching.Models;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests;

public class PromptTemplateValidationServiceTests
{
    [Fact]
    public void Validate_WhenRenderedPromptContainsScoreDetailsJson_ShouldUseOutputExampleJson()
    {
        var service = new PromptTemplateValidationService();
        var definition = PromptTemplateCatalog.GetByScene(PromptTemplateScene.MatchingReview);

        var result = service.Validate(definition, definition.DefaultContent);

        result.IsValid.Should().BeTrue();
        result.StructuredOutputIsValid.Should().BeTrue();
        result.ExampleJson.Should().NotBeNull();
        result.ExampleJson.Should().Contain("\"score\"");
        result.ExampleJson.Should().NotContain("\"Embedding\"");
    }
}
