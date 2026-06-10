using System.Reflection;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.Matching.Services;
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

    [Fact]
    public void Validate_WhenTemplateUsesWhitespacePlaceholders_ShouldRenderThemConsistently()
    {
        var definition = PromptTemplateCatalog.GetByScene(PromptTemplateScene.MatchingReview);
        var content = definition.DefaultContent
            .Replace("{{workflowScene}}", "{{ workflowScene }}", StringComparison.Ordinal)
            .Replace("{{sourceProject}}", "{{ sourceProject }}", StringComparison.Ordinal)
            .Replace("{{sourceSpecification}}", "{{ sourceSpecification }}", StringComparison.Ordinal);

        var result = new PromptTemplateValidationService().Validate(definition, content);

        result.IsValid.Should().BeTrue();
        result.RenderedPrompt.Should().Contain("【业务场景】智能填充复核");
        result.RenderedPrompt.Should().Contain("项目：平台吸附精度");
        result.RenderedPrompt.Should().Contain("规格：平台平面度需控制在0.05mm以内");
        result.RenderedPrompt.Should().NotContain("{{ workflowScene }}");
        result.RenderedPrompt.Should().NotContain("{{ sourceProject }}");
        result.RenderedPrompt.Should().NotContain("{{ sourceSpecification }}");
    }

    [Fact]
    public void PromptTemplateCatalog_ShouldExposeFourSystemTemplates_WithoutEntityResolutionTemplate()
    {
        var definitions = PromptTemplateCatalog.GetSystemTemplates();

        definitions.Should().HaveCount(4);
        definitions.Select(item => item.Name).Should().BeEquivalentTo([
            "matching-review",
            "import-duplicate-review",
            "matching-equivalence-adjudication",
            "matching-candidate-rerank"
        ]);

        var review = definitions.Single(item => item.Name == "matching-review");
        review.DefaultContent.Should().Contain("【业务场景】{{workflowScene}}");
        review.AvailableVariables.Should().Contain("workflowScene");
        new PromptTemplateValidationService()
            .Validate(review, review.DefaultContent)
            .RenderedPrompt.Should().Contain("【业务场景】智能填充复核");
        var duplicateReview = definitions.Single(item => item.Name == "import-duplicate-review");
        duplicateReview.DefaultContent.Should().Contain("你是导入重复复核助手");
        duplicateReview.AvailableVariables.Should().Contain("workflowScene");
        PromptTemplateCatalog.GetByScene(PromptTemplateScene.ImportDuplicateReview).Name.Should().Be("import-duplicate-review");
        PromptTemplateCatalog.TryGetByName("import-duplicate-review", out _).Should().BeTrue();

        var adjudication = definitions.Single(item => item.Name == "matching-equivalence-adjudication");
        adjudication.DefaultContent.Should().Contain("区间上下限");

        var rerank = definitions.Single(item => item.Name == "matching-candidate-rerank");
        rerank.DefaultContent.Should().Contain("selectedSpecId");
        rerank.AvailableVariables.Should().Contain("candidatesJson");
    }

    [Fact]
    public void PromptTemplateCatalog_ShouldNotExposeMatchingEntityResolutionTemplate()
    {
        PromptTemplateCatalog.TryGetByName("matching-entity-resolution", out _).Should().BeFalse();
    }

    [Fact]
    public void Validate_WhenPreviewingEquivalenceTemplate_ShouldFillKeySampleVariables()
    {
        var definition = PromptTemplateCatalog.GetByScene(PromptTemplateScene.MatchingEquivalenceAdjudication);

        var result = new PromptTemplateValidationService().Validate(definition, definition.DefaultContent);

        result.IsValid.Should().BeTrue();
        result.RenderedPrompt.Should().Contain("项目：平台吸附精度");
        result.RenderedPrompt.Should().Contain("规格：平台平面度需控制在0.05mm以内");
        result.RenderedPrompt.Should().Contain("项目：平台吸附精度候选");
        result.RenderedPrompt.Should().Contain("规格：平台平面度不超过0.05mm");
        result.RenderedPrompt.Should().NotContain("{{candidateProject}}");
        result.RenderedPrompt.Should().NotContain("{{candidateSpecification}}");
    }

    [Fact]
    public void Validate_WhenPreviewingCandidateRerankTemplate_ShouldFillKeySampleVariables()
    {
        var definition = PromptTemplateCatalog.GetByScene(PromptTemplateScene.MatchingCandidateRerank);

        var result = new PromptTemplateValidationService().Validate(definition, definition.DefaultContent);

        result.IsValid.Should().BeTrue();
        result.RenderedPrompt.Should().Contain("【当前本地 Top1 SpecId】101");
        result.RenderedPrompt.Should().Contain("\"specId\":101");
        result.RenderedPrompt.Should().Contain("\"specId\":102");
        result.RenderedPrompt.Should().NotContain("{{candidatesJson}}");
    }

    [Fact]
    public void ReplacePlaceholders_WhenValueContainsAnotherPlaceholderToken_ShouldNotPerformSecondPassReplacement()
    {
        var rendererType = typeof(PromptTemplateValidationService).Assembly
            .GetType("AcceptanceSpecSystem.Core.Matching.Services.PromptTemplatePlaceholderRenderer");
        var method = rendererType!.GetMethod(
            "ReplacePlaceholders",
            BindingFlags.Public | BindingFlags.Static);

        var rendered = (string)method!.Invoke(
            null,
            [
                "项目：{{sourceProject}}\n当前决策：{{currentDecision}}",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sourceProject"] = "导入{{currentDecision}}",
                    ["currentDecision"] = "manualReview"
                }
            ])!;

        rendered.Should().Contain("项目：导入{{currentDecision}}");
        rendered.Should().Contain("当前决策：manualReview");
        rendered.Should().NotContain("项目：导入manualReview");
    }

    [Fact]
    public void Validate_WhenMatchingReviewExampleScoreOutOfRange_ShouldRejectTemplate()
    {
        var definition = PromptTemplateCatalog.GetByScene(PromptTemplateScene.MatchingReview);
        var invalidContent = definition.DefaultContent.Replace(
            "{\"score\":0,\"reason\":\"...\",\"commentary\":\"...\"}",
            "{\"score\":101,\"reason\":\"分数越界\",\"commentary\":\"示例错误\"}",
            StringComparison.Ordinal);

        var result = new PromptTemplateValidationService().Validate(definition, invalidContent);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("score", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WhenEquivalenceExampleVerdictAndReasonTypeConflict_ShouldRejectTemplate()
    {
        var definition = PromptTemplateCatalog.GetByScene(PromptTemplateScene.MatchingEquivalenceAdjudication);
        var invalidContent = definition.DefaultContent.Replace(
            "{\"verdict\":\"uncertain\",\"reasonType\":\"uncertain\",\"reason\":\"...\",\"confidence\":0.0}",
            "{\"verdict\":\"equivalent\",\"reasonType\":\"semantic_difference\",\"reason\":\"示例错误\",\"confidence\":0.9}",
            StringComparison.Ordinal);

        var result = new PromptTemplateValidationService().Validate(definition, invalidContent);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.Contains("reasonType", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("verdict", StringComparison.OrdinalIgnoreCase));
    }
}
