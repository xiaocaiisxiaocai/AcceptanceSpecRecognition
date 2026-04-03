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
    public void MatchEvidence_ShouldTrackHardConflictAndSummary()
    {
        var evidence = new MatchEvidence
        {
            HasHardConflict = true,
            Summary = ["命中型号硬冲突"]
        };

        evidence.HasHardConflict.Should().BeTrue();
        evidence.Summary.Should().ContainSingle().Which.Should().Be("命中型号硬冲突");
    }

    [Fact]
    public void MatchResult_ShouldExposeStructuredIssues()
    {
        var result = new MatchResult();

        result.Issues.Should().NotBeNull();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void MatchingConfig_ShouldExposeLlmEntityResolutionSettings()
    {
        var config = new MatchingConfig();

        config.MatchingStrategy.Should().Be(MatchingStrategy.SingleStage);
        config.MinScoreThreshold.Should().Be(0.9);
        config.RecallTopK.Should().Be(2);
        config.AmbiguityMargin.Should().Be(0.02);
        config.HighConfidenceThreshold.Should().Be(0.98);
        config.UseLlmEntityResolution.Should().BeFalse();
        config.LlmEntityResolutionTopCandidates.Should().Be(2);
        config.LlmEntityPositiveConfidenceThreshold.Should().Be(0.85);
        config.LlmEntityConflictReviewConfidenceThreshold.Should().Be(0.7);
        config.LlmEntityConflictRejectConfidenceThreshold.Should().Be(0.9);
    }
}
