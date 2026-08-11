using AcceptanceSpecSystem.Application.Services;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class AcceptanceSpecCleanupClassifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Classify_ShouldProtectRecentlyChangedSpec()
    {
        var result = Classify(contentActivityAtUtc: Now.AddDays(-29));

        result.Category.Should().Be(SpecCleanupCategory.Healthy);
        result.Reason.Should().Be(SpecCleanupReason.RecentlyChanged);
    }

    [Fact]
    public void Classify_ShouldRecommendNeverReferencedSpecAfterGracePeriod()
    {
        var result = Classify(contentActivityAtUtc: Now.AddDays(-31));

        result.Category.Should().Be(SpecCleanupCategory.RecommendedCleanup);
        result.Reason.Should().Be(SpecCleanupReason.NeverReferenced);
    }

    [Fact]
    public void Classify_ShouldRecommendLongUnusedOnlyWhenReferenceAndContentAreOld()
    {
        var result = Classify(
            contentActivityAtUtc: Now.AddDays(-500),
            currentReferenceCount: 2,
            recordedReferenceCount: 4,
            lastReferencedAtUtc: Now.AddDays(-366));

        result.Category.Should().Be(SpecCleanupCategory.RecommendedCleanup);
        result.Reason.Should().Be(SpecCleanupReason.LongUnused);
    }

    [Fact]
    public void Classify_ShouldRequireReviewWhenOnlyPreviousVersionWasRecentlyReferenced()
    {
        var result = Classify(
            contentActivityAtUtc: Now.AddDays(-40),
            recordedReferenceCount: 3,
            lastReferencedAtUtc: Now.AddDays(-10));

        result.Category.Should().Be(SpecCleanupCategory.ManualReview);
        result.Reason.Should().Be(SpecCleanupReason.CurrentVersionNeverReferenced);
    }

    [Fact]
    public void Classify_ShouldRequireReviewForMigrationBaselineBeforeOtherRules()
    {
        var result = Classify(
            contentActivityAtUtc: Now.AddDays(-800),
            recordedReferenceCount: 9,
            untrackedReferenceCount: 9);

        result.Category.Should().Be(SpecCleanupCategory.ManualReview);
        result.Reason.Should().Be(SpecCleanupReason.UntrackedHistoricalReferences);
    }

    [Fact]
    public void Classify_ShouldTreatExactThresholdAsNotExpired()
    {
        var result = Classify(contentActivityAtUtc: Now.AddDays(-30));

        result.Category.Should().Be(SpecCleanupCategory.Healthy);
        result.Reason.Should().Be(SpecCleanupReason.RecentlyChanged);
    }

    private static SpecCleanupDecision Classify(
        DateTimeOffset contentActivityAtUtc,
        long currentReferenceCount = 0,
        long recordedReferenceCount = 0,
        long untrackedReferenceCount = 0,
        DateTimeOffset? lastReferencedAtUtc = null) =>
        AcceptanceSpecCleanupClassifier.Classify(
            new SpecCleanupFacts(
                contentActivityAtUtc,
                currentReferenceCount,
                recordedReferenceCount,
                untrackedReferenceCount,
                lastReferencedAtUtc),
            new SpecCleanupThresholds(30, 365),
            Now);
}
