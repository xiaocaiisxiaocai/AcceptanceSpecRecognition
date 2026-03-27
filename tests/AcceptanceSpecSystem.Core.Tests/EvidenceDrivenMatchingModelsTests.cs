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
}
