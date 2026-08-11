namespace AcceptanceSpecSystem.Application.Services;

public enum SpecCleanupCategory
{
    RecommendedCleanup = 1,
    ManualReview = 2,
    Healthy = 3
}

public enum SpecCleanupReason
{
    NeverReferenced = 1,
    LongUnused = 2,
    UntrackedHistoricalReferences = 3,
    CurrentVersionNeverReferenced = 4,
    RecentlyChanged = 5,
    RecentlyUsed = 6
}

public sealed record SpecCleanupThresholds(int NewItemGraceDays, int UnusedDays)
{
    public void Validate()
    {
        if (NewItemGraceDays < 1 || NewItemGraceDays > 3650)
            throw new ArgumentOutOfRangeException(nameof(NewItemGraceDays));
        if (UnusedDays <= NewItemGraceDays || UnusedDays > 36500)
            throw new ArgumentOutOfRangeException(nameof(UnusedDays));
    }
}

public sealed record SpecCleanupFacts(
    DateTimeOffset ContentActivityAtUtc,
    long CurrentReferenceCount,
    long RecordedReferenceCount,
    long UntrackedReferenceCount,
    DateTimeOffset? LastReferencedAtUtc);

public sealed record SpecCleanupDecision(
    SpecCleanupCategory Category,
    SpecCleanupReason Reason);

public static class AcceptanceSpecCleanupClassifier
{
    public static SpecCleanupDecision Classify(
        SpecCleanupFacts facts,
        SpecCleanupThresholds thresholds,
        DateTimeOffset scannedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(thresholds);
        thresholds.Validate();

        if (facts.CurrentReferenceCount < 0 ||
            facts.RecordedReferenceCount < 0 ||
            facts.UntrackedReferenceCount < 0 ||
            facts.UntrackedReferenceCount > facts.RecordedReferenceCount)
        {
            throw new ArgumentOutOfRangeException(nameof(facts));
        }

        if (facts.UntrackedReferenceCount > 0)
        {
            return new(SpecCleanupCategory.ManualReview,
                SpecCleanupReason.UntrackedHistoricalReferences);
        }

        var graceCutoff = scannedAtUtc.AddDays(-thresholds.NewItemGraceDays);
        if (facts.ContentActivityAtUtc >= graceCutoff)
        {
            return new(SpecCleanupCategory.Healthy, SpecCleanupReason.RecentlyChanged);
        }

        if (facts.RecordedReferenceCount == 0)
        {
            return new(SpecCleanupCategory.RecommendedCleanup,
                SpecCleanupReason.NeverReferenced);
        }

        var unusedCutoff = scannedAtUtc.AddDays(-thresholds.UnusedDays);
        var lastActivityAtUtc = facts.LastReferencedAtUtc is { } referencedAt &&
                                referencedAt > facts.ContentActivityAtUtc
            ? referencedAt
            : facts.ContentActivityAtUtc;
        if (lastActivityAtUtc < unusedCutoff)
        {
            return new(SpecCleanupCategory.RecommendedCleanup,
                SpecCleanupReason.LongUnused);
        }

        if (facts.CurrentReferenceCount == 0)
        {
            return new(SpecCleanupCategory.ManualReview,
                SpecCleanupReason.CurrentVersionNeverReferenced);
        }

        return new(SpecCleanupCategory.Healthy, SpecCleanupReason.RecentlyUsed);
    }
}
