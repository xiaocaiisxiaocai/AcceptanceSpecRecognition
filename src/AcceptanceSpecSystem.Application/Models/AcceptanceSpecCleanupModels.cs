using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Models;

public sealed record StartSpecCleanupScanRequest(int NewItemGraceDays = 30, int UnusedDays = 365);

public sealed record SpecCleanupScanStatusModel(
    string Id,
    AcceptanceSpecCleanupScanStatus Status,
    int NewItemGraceDays,
    int UnusedDays,
    int TotalCount,
    int ProcessedCount,
    int RecommendedCleanupCount,
    int ManualReviewCount,
    int HealthyCount,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? ErrorMessage);

public sealed record SpecCleanupScanItemModel(
    long Id,
    int AcceptanceSpecId,
    string Project,
    string Specification,
    string? Acceptance,
    string? Remark,
    string CustomerName,
    string? ProcessName,
    long ReferenceVersion,
    long CurrentReferenceCount,
    long RecordedReferenceCount,
    long UntrackedReferenceCount,
    DateTime? LastReferencedAtUtc,
    DateTime ContentActivityAtUtc,
    AcceptanceSpecCleanupCategory Category,
    AcceptanceSpecCleanupReason Reason,
    AcceptanceSpecCleanupReviewStatus ReviewStatus);

public sealed record SpecCleanupActionItem(long ScanItemId, string? Reason = null);

public sealed record SpecCleanupActionResult(
    long ItemId,
    int? AcceptanceSpecId,
    bool Success,
    string Message);

public sealed record SpecCleanupBatchResult(
    int SuccessCount,
    int FailedCount,
    IReadOnlyList<SpecCleanupActionResult> Items);

public sealed record QuarantinedAcceptanceSpecModel(
    int Id,
    string Project,
    string Specification,
    string? Acceptance,
    string? Remark,
    string CustomerName,
    string? ProcessName,
    long ReferenceVersion,
    DateTime QuarantinedAtUtc,
    DateTime QuarantineExpiresAtUtc,
    int? QuarantinedByUserId,
    string? QuarantineReason,
    string? SourceScanId);

public sealed record IgnoredAcceptanceSpecModel(
    int Id,
    string Project,
    string Specification,
    string? Acceptance,
    string? Remark,
    string CustomerName,
    string? ProcessName,
    long ReferenceVersion,
    DateTime? IgnoredAtUtc,
    int? IgnoredByUserId,
    string? IgnoreReason);

public sealed record RestoreSpecCleanupRequest(IReadOnlyCollection<int> SpecIds);

public sealed record PermanentlyDeleteSpecCleanupRequest(
    IReadOnlyCollection<SpecPermanentDeleteItem> Items,
    bool ConfirmPermanentDelete);

public sealed record SpecPermanentDeleteItem(int SpecId, long ReferenceVersion);
