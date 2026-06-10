using System.Reflection;
using AcceptanceSpecSystem.Core.Matching.Models;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests;

public class EvidenceDrivenMatchingModelsTests
{
    [Fact]
    public void MatchResult_WhenDecisionIsManualReview_ShouldNotBeHighConfidence()
    {
        var result = new MatchResult
        {
            Score = 0.99,
            Decision = MatchDecision.ManualReview
        };

        result.IsHighConfidence.Should().BeFalse();
        result.IsMediumConfidence.Should().BeFalse();
        result.IsLowConfidence.Should().BeFalse();
    }

    [Fact]
    public void MatchResult_WhenScoreBelowMinScoreThreshold_ShouldBeLowConfidence()
    {
        var result = new MatchResult
        {
            Score = 0.89,
            Decision = MatchDecision.AutoApply,
            MinScoreThreshold = 0.90,
            HighConfidenceThreshold = 0.98
        };

        result.IsHighConfidence.Should().BeFalse();
        result.IsMediumConfidence.Should().BeFalse();
        result.IsLowConfidence.Should().BeTrue();
    }

    [Fact]
    public void MatchResult_WhenScoreBetweenMinAndHighThreshold_ShouldBeMediumConfidence()
    {
        var result = new MatchResult
        {
            Score = 0.92,
            Decision = MatchDecision.AutoApply,
            MinScoreThreshold = 0.90,
            HighConfidenceThreshold = 0.98
        };

        result.IsHighConfidence.Should().BeFalse();
        result.IsMediumConfidence.Should().BeTrue();
        result.IsLowConfidence.Should().BeFalse();
    }

    [Fact]
    public void MatchEvidence_ShouldTrackSummaryWithoutLegacyHardConflictFlag()
    {
        var evidence = new MatchEvidence
        {
            Summary = ["命中实体硬冲突"]
        };

        evidence.Summary.Should().ContainSingle().Which.Should().Be("命中实体硬冲突");
    }

    [Fact]
    public void MatchResult_ShouldExposeStructuredIssues()
    {
        var result = new MatchResult();

        result.Issues.Should().NotBeNull();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void MatchingConfig_ShouldNotExposeRemovedLlmEntityResolutionSettings()
    {
        var config = new MatchingConfig();

        config.MinScoreThreshold.Should().Be(0.9);
        config.RecallTopK.Should().Be(2);
        config.AmbiguityMargin.Should().Be(0.02);
        config.HighConfidenceThreshold.Should().Be(0.95);
        config.LlmParallelism.Should().Be(4);
        typeof(MatchingConfig).GetProperty("UseLlmEntityResolution", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull();
        typeof(MatchingConfig).GetProperty("LlmEntityResolutionTopCandidates", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull();
        typeof(MatchingConfig).GetProperty("LlmEntityPositiveConfidenceThreshold", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull();
        typeof(MatchingConfig).GetProperty("LlmEntityConflictReviewConfidenceThreshold", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull();
        typeof(MatchingConfig).GetProperty("LlmEntityConflictRejectConfidenceThreshold", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull();
    }

    [Fact]
    public void MatchingConfig_ShouldNotExposeLegacyUseLlmReviewFlag()
    {
        typeof(MatchingConfig).GetProperty("UseLlmReview", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull();
    }
}
