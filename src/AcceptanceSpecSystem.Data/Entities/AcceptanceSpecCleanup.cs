namespace AcceptanceSpecSystem.Data.Entities;

public enum AcceptanceSpecCleanupStatus
{
    Active = 0,
    Quarantined = 1
}

public enum AcceptanceSpecCleanupScanStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Cancelled = 3,
    Failed = 4
}

public enum AcceptanceSpecCleanupCategory
{
    RecommendedCleanup = 1,
    ManualReview = 2,
    Healthy = 3
}

public enum AcceptanceSpecCleanupReason
{
    NeverReferenced = 1,
    LongUnused = 2,
    UntrackedHistoricalReferences = 3,
    CurrentVersionNeverReferenced = 4,
    RecentlyChanged = 5,
    RecentlyUsed = 6
}

public enum AcceptanceSpecCleanupReviewStatus
{
    Pending = 0,
    Kept = 1
}

public sealed class AcceptanceSpecCleanupScan
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int CompanyId { get; set; }
    public int RequestedByUserId { get; set; }
    public bool IsAllScope { get; set; }
    public bool IncludeSelf { get; set; }
    public string ScopeOrgUnitIds { get; set; } = string.Empty;
    public int NewItemGraceDays { get; set; }
    public int UnusedDays { get; set; }
    public AcceptanceSpecCleanupScanStatus Status { get; set; }
    public int TotalCount { get; set; }
    public int ProcessedCount { get; set; }
    public int RecommendedCleanupCount { get; set; }
    public int ManualReviewCount { get; set; }
    public int HealthyCount { get; set; }
    public int LastProcessedSpecId { get; set; }
    public bool CancellationRequested { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }

    public ICollection<AcceptanceSpecCleanupScanItem> Items { get; set; } =
        new List<AcceptanceSpecCleanupScanItem>();
}

public sealed class AcceptanceSpecCleanupScanItem
{
    public long Id { get; set; }
    public string ScanId { get; set; } = string.Empty;
    public int AcceptanceSpecId { get; set; }
    public long ReferenceVersion { get; set; }
    public long CurrentReferenceCount { get; set; }
    public long RecordedReferenceCount { get; set; }
    public long UntrackedReferenceCount { get; set; }
    public DateTime? LastReferencedAtUtc { get; set; }
    public DateTime ContentActivityAtUtc { get; set; }
    public AcceptanceSpecCleanupCategory Category { get; set; }
    public AcceptanceSpecCleanupReason Reason { get; set; }
    public AcceptanceSpecCleanupReviewStatus ReviewStatus { get; set; }
    public DateTime ScannedAtUtc { get; set; }

    public AcceptanceSpecCleanupScan Scan { get; set; } = null!;
    public AcceptanceSpec AcceptanceSpec { get; set; } = null!;
}

public sealed class AcceptanceSpecCleanupDeletionRecord
{
    public long Id { get; set; }
    public int OriginalAcceptanceSpecId { get; set; }
    public int CompanyId { get; set; }
    public int? OwnerOrgUnitId { get; set; }
    public int DeletedByUserId { get; set; }
    public DateTime DeletedAtUtc { get; set; }
    public string? SourceScanId { get; set; }
    public long ReferenceVersion { get; set; }
    public long RecordedReferenceCount { get; set; }
    public int ContentVersionCount { get; set; }
}
