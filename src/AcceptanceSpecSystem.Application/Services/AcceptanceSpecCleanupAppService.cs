using AcceptanceSpecSystem.Application.Models;
using AcceptanceSpecSystem.Application.Options;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Application.Services;

public interface IAcceptanceSpecCleanupAppService
{
    Task<SpecCleanupScanStatusModel> StartScanAsync(SpecAccessContext scope, StartSpecCleanupScanRequest request, CancellationToken cancellationToken = default);
    Task<SpecCleanupScanStatusModel?> GetScanStatusAsync(SpecAccessContext scope, string scanId, CancellationToken cancellationToken = default);
    Task<PagedResult<SpecCleanupScanItemModel>> GetScanItemsAsync(SpecAccessContext scope, string scanId, AcceptanceSpecCleanupCategory category, int page, int pageSize, CancellationToken cancellationToken = default);
    Task CancelScanAsync(SpecAccessContext scope, string scanId, CancellationToken cancellationToken = default);
    Task<SpecCleanupBatchResult> KeepAsync(SpecAccessContext scope, IReadOnlyCollection<SpecCleanupActionItem> items, CancellationToken cancellationToken = default);
    Task<SpecCleanupBatchResult> IgnoreAsync(SpecAccessContext scope, IReadOnlyCollection<SpecCleanupActionItem> items, CancellationToken cancellationToken = default);
    Task<SpecCleanupBatchResult> QuarantineAsync(SpecAccessContext scope, IReadOnlyCollection<SpecCleanupActionItem> items, CancellationToken cancellationToken = default);
    Task<PagedResult<QuarantinedAcceptanceSpecModel>> GetQuarantinedAsync(SpecAccessContext scope, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<PagedResult<IgnoredAcceptanceSpecModel>> GetIgnoredAsync(SpecAccessContext scope, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<SpecCleanupBatchResult> UnignoreAsync(SpecAccessContext scope, IReadOnlyCollection<int> specIds, CancellationToken cancellationToken = default);
    Task<SpecCleanupBatchResult> RestoreAsync(SpecAccessContext scope, IReadOnlyCollection<int> specIds, CancellationToken cancellationToken = default);
    Task<SpecCleanupBatchResult> PermanentlyDeleteAsync(SpecAccessContext scope, PermanentlyDeleteSpecCleanupRequest request, CancellationToken cancellationToken = default);
    Task<bool> ProcessNextScanBatchAsync(CancellationToken cancellationToken = default);
    Task<int> CleanupExpiredScansAsync(CancellationToken cancellationToken = default);
}

public sealed class AcceptanceSpecCleanupAppService : IAcceptanceSpecCleanupAppService
{
    private const int MaxBatchActionItems = 200;
    private readonly AppDbContext _db;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly AcceptanceSpecCleanupOptions _options;
    private readonly ILogger<AcceptanceSpecCleanupAppService> _logger;

    public AcceptanceSpecCleanupAppService(
        AppDbContext db,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IOptions<AcceptanceSpecCleanupOptions> options,
        ILogger<AcceptanceSpecCleanupAppService> logger)
    {
        _db = db;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SpecCleanupScanStatusModel> StartScanAsync(
        SpecAccessContext scope,
        StartSpecCleanupScanRequest request,
        CancellationToken cancellationToken = default)
    {
        var thresholds = new SpecCleanupThresholds(request.NewItemGraceDays, request.UnusedDays);
        try
        {
            thresholds.Validate();
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ApplicationServiceException(400, "保护期必须为 1-3650 天，长期未引用阈值必须更大");
        }

        var query = BuildActiveScopeQuery(scope).Where(spec => !spec.CleanupScanIgnored);
        var now = UtcNow();
        var scan = new AcceptanceSpecCleanupScan
        {
            Id = Guid.NewGuid().ToString("N"),
            CompanyId = scope.CompanyId,
            RequestedByUserId = scope.UserId,
            IsAllScope = scope.IsAll,
            IncludeSelf = scope.IncludeSelf,
            ScopeOrgUnitIds = string.Join(',', scope.OrgUnitIds.Distinct().Order()),
            NewItemGraceDays = request.NewItemGraceDays,
            UnusedDays = request.UnusedDays,
            Status = AcceptanceSpecCleanupScanStatus.Pending,
            TotalCount = await query.CountAsync(cancellationToken),
            CreatedAtUtc = now
        };
        _db.AcceptanceSpecCleanupScans.Add(scan);
        await _db.SaveChangesAsync(cancellationToken);
        return ToStatus(scan);
    }

    public async Task<SpecCleanupScanStatusModel?> GetScanStatusAsync(
        SpecAccessContext scope,
        string scanId,
        CancellationToken cancellationToken = default)
    {
        var scan = await FindOwnedScanAsync(scope, scanId, cancellationToken);
        return scan is null ? null : ToStatus(scan);
    }

    public async Task<PagedResult<SpecCleanupScanItemModel>> GetScanItemsAsync(
        SpecAccessContext scope,
        string scanId,
        AcceptanceSpecCleanupCategory category,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        await RequireOwnedScanAsync(scope, scanId, cancellationToken);
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = _db.AcceptanceSpecCleanupScanItems.AsNoTracking()
            .Where(item => item.ScanId == scanId && item.Category == category);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new SpecCleanupScanItemModel(
                item.Id,
                item.AcceptanceSpecId,
                item.AcceptanceSpec.Project,
                item.AcceptanceSpec.Specification,
                item.AcceptanceSpec.Acceptance,
                item.AcceptanceSpec.Remark,
                item.AcceptanceSpec.Customer.Name,
                item.AcceptanceSpec.Process != null ? item.AcceptanceSpec.Process.Name : null,
                item.ReferenceVersion,
                item.CurrentReferenceCount,
                item.RecordedReferenceCount,
                item.UntrackedReferenceCount,
                item.LastReferencedAtUtc,
                item.ContentActivityAtUtc,
                item.Category,
                item.Reason,
                item.ReviewStatus))
            .ToListAsync(cancellationToken);
        return new PagedResult<SpecCleanupScanItemModel>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task CancelScanAsync(
        SpecAccessContext scope,
        string scanId,
        CancellationToken cancellationToken = default)
    {
        var scan = await RequireOwnedScanAsync(scope, scanId, cancellationToken);
        if (scan.Status is AcceptanceSpecCleanupScanStatus.Completed or
            AcceptanceSpecCleanupScanStatus.Cancelled or AcceptanceSpecCleanupScanStatus.Failed)
        {
            return;
        }
        scan.CancellationRequested = true;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task<SpecCleanupBatchResult> KeepAsync(
        SpecAccessContext scope,
        IReadOnlyCollection<SpecCleanupActionItem> items,
        CancellationToken cancellationToken = default) =>
        UpdateScanItemsAsync(scope, items, CleanupAction.Keep, cancellationToken);

    public Task<SpecCleanupBatchResult> IgnoreAsync(
        SpecAccessContext scope,
        IReadOnlyCollection<SpecCleanupActionItem> items,
        CancellationToken cancellationToken = default) =>
        UpdateScanItemsAsync(scope, items, CleanupAction.Ignore, cancellationToken);

    public Task<SpecCleanupBatchResult> QuarantineAsync(
        SpecAccessContext scope,
        IReadOnlyCollection<SpecCleanupActionItem> items,
        CancellationToken cancellationToken = default) =>
        UpdateScanItemsAsync(scope, items, CleanupAction.Quarantine, cancellationToken);

    public async Task<PagedResult<QuarantinedAcceptanceSpecModel>> GetQuarantinedAsync(
        SpecAccessContext scope,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = _db.AcceptanceSpecs.IgnoreQueryFilters()
            .Where(spec => spec.CleanupStatus == AcceptanceSpecCleanupStatus.Quarantined &&
                           spec.WordFile.CompanyId == scope.CompanyId);
        query = scope.ApplySpecScopeToQuery(query);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.AsNoTracking()
            .OrderBy(spec => spec.QuarantineExpiresAtUtc).ThenBy(spec => spec.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(spec => new QuarantinedAcceptanceSpecModel(
                spec.Id,
                spec.Project,
                spec.Specification,
                spec.Acceptance,
                spec.Remark,
                spec.Customer.Name,
                spec.Process != null ? spec.Process.Name : null,
                spec.ReferenceVersion,
                spec.QuarantinedAtUtc!.Value,
                spec.QuarantineExpiresAtUtc!.Value,
                spec.QuarantinedByUserId,
                spec.QuarantineReason,
                spec.QuarantineSourceScanId))
            .ToListAsync(cancellationToken);
        return new PagedResult<QuarantinedAcceptanceSpecModel>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PagedResult<IgnoredAcceptanceSpecModel>> GetIgnoredAsync(
        SpecAccessContext scope,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = BuildActiveScopeQuery(scope).Where(spec => spec.CleanupScanIgnored);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.AsNoTracking()
            .OrderByDescending(spec => spec.CleanupScanIgnoredAtUtc).ThenBy(spec => spec.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(spec => new IgnoredAcceptanceSpecModel(
                spec.Id, spec.Project, spec.Specification, spec.Acceptance, spec.Remark,
                spec.Customer.Name,
                spec.Process != null ? spec.Process.Name : null,
                spec.ReferenceVersion, spec.CleanupScanIgnoredAtUtc,
                spec.CleanupScanIgnoredByUserId, spec.CleanupScanIgnoreReason))
            .ToListAsync(cancellationToken);
        return new PagedResult<IgnoredAcceptanceSpecModel>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<SpecCleanupBatchResult> UnignoreAsync(
        SpecAccessContext scope,
        IReadOnlyCollection<int> specIds,
        CancellationToken cancellationToken = default)
    {
        var ids = NormalizeIds(specIds);
        var results = new List<SpecCleanupActionResult>(ids.Count);
        foreach (var id in ids)
        {
            var spec = await LoadManagedSpecAsync(scope, id, cancellationToken);
            if (spec is null || spec.CleanupStatus != AcceptanceSpecCleanupStatus.Active ||
                !spec.CleanupScanIgnored)
            {
                results.Add(new(0, id, false, "已忽略规格不存在或无权操作"));
                continue;
            }
            spec.CleanupScanIgnored = false;
            spec.CleanupScanIgnoredAtUtc = null;
            spec.CleanupScanIgnoredByUserId = null;
            spec.CleanupScanIgnoreReason = null;
            results.Add(new(0, id, true, "已重新纳入扫描"));
        }
        await _db.SaveChangesAsync(cancellationToken);
        return ToBatchResult(results);
    }

    public async Task<SpecCleanupBatchResult> RestoreAsync(
        SpecAccessContext scope,
        IReadOnlyCollection<int> specIds,
        CancellationToken cancellationToken = default)
    {
        var ids = NormalizeIds(specIds);
        var results = new List<SpecCleanupActionResult>(ids.Count);
        foreach (var id in ids)
        {
            var spec = await LoadManagedSpecAsync(scope, id, cancellationToken);
            if (spec is null || spec.CleanupStatus != AcceptanceSpecCleanupStatus.Quarantined)
            {
                results.Add(new(0, id, false, "隔离规格不存在或无权操作"));
                continue;
            }
            spec.CleanupStatus = AcceptanceSpecCleanupStatus.Active;
            ClearQuarantine(spec);
            results.Add(new(0, id, true, "已恢复"));
        }
        await _db.SaveChangesAsync(cancellationToken);
        return ToBatchResult(results);
    }

    public async Task<SpecCleanupBatchResult> PermanentlyDeleteAsync(
        SpecAccessContext scope,
        PermanentlyDeleteSpecCleanupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.ConfirmPermanentDelete)
            throw new ApplicationServiceException(400, "必须明确二次确认永久删除");
        if (request.Items.Count is < 1 or > MaxBatchActionItems)
            throw new ApplicationServiceException(400, $"每次请选择 1-{MaxBatchActionItems} 条规格");

        var now = UtcNow();
        var results = new List<SpecCleanupActionResult>(request.Items.Count);
        foreach (var command in request.Items.DistinctBy(item => item.SpecId))
        {
            var spec = await LoadManagedSpecAsync(scope, command.SpecId, cancellationToken);
            if (spec is null || spec.CleanupStatus != AcceptanceSpecCleanupStatus.Quarantined)
            {
                results.Add(new(0, command.SpecId, false, "隔离规格不存在或无权操作"));
                continue;
            }
            if (spec.ReferenceVersion != command.ReferenceVersion ||
                spec.QuarantinedReferenceVersion != command.ReferenceVersion)
            {
                results.Add(new(0, spec.Id, false, "规格版本已变化，请刷新后重试"));
                continue;
            }
            if (!spec.QuarantineExpiresAtUtc.HasValue || spec.QuarantineExpiresAtUtc.Value > now)
            {
                results.Add(new(0, spec.Id, false,
                    $"隔离期未满，最早可于 {spec.QuarantineExpiresAtUtc:yyyy-MM-dd HH:mm} UTC 永久删除"));
                continue;
            }

            var recordedCount = await _db.AcceptanceSpecReferenceEvents.IgnoreQueryFilters()
                .Where(item => item.AcceptanceSpecId == spec.Id)
                .SumAsync(item => (long?)item.OccurrenceCount, cancellationToken) ?? 0;
            var contentVersionCount = await _db.AcceptanceSpecContentVersions.IgnoreQueryFilters()
                .CountAsync(item => item.AcceptanceSpecId == spec.Id, cancellationToken);
            _db.AcceptanceSpecCleanupDeletionRecords.Add(new AcceptanceSpecCleanupDeletionRecord
            {
                OriginalAcceptanceSpecId = spec.Id,
                CompanyId = scope.CompanyId,
                OwnerOrgUnitId = spec.OwnerOrgUnitId,
                DeletedByUserId = scope.UserId,
                DeletedAtUtc = now,
                SourceScanId = spec.QuarantineSourceScanId,
                ReferenceVersion = spec.ReferenceVersion,
                RecordedReferenceCount = recordedCount,
                ContentVersionCount = contentVersionCount
            });
            _db.AcceptanceSpecs.Remove(spec);
            results.Add(new(0, spec.Id, true, "已永久删除"));
        }
        await _db.SaveChangesAsync(cancellationToken);
        return ToBatchResult(results);
    }

    public async Task<bool> ProcessNextScanBatchAsync(CancellationToken cancellationToken = default)
    {
        await using var operationLock = await _unitOfWork.AcquireOperationLockAsync(
            "acceptance-spec-cleanup-scan-worker",
            cancellationToken);
        var scan = await _db.AcceptanceSpecCleanupScans
            .Where(item => item.Status == AcceptanceSpecCleanupScanStatus.Running ||
                           item.Status == AcceptanceSpecCleanupScanStatus.Pending)
            .OrderBy(item => item.Status == AcceptanceSpecCleanupScanStatus.Running ? 0 : 1)
            .ThenBy(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (scan is null)
            return false;

        var now = UtcNow();
        if (scan.CancellationRequested)
        {
            scan.Status = AcceptanceSpecCleanupScanStatus.Cancelled;
            scan.CompletedAtUtc = now;
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        if (scan.Status == AcceptanceSpecCleanupScanStatus.Pending)
        {
            scan.Status = AcceptanceSpecCleanupScanStatus.Running;
            scan.StartedAtUtc = now;
            await _db.SaveChangesAsync(cancellationToken);
        }

        try
        {
            var scope = RestoreScope(scan);
            var batchSize = Math.Clamp(_options.BatchSize, 1, 1000);
            var batch = await BuildActiveScopeQuery(scope)
                .Where(spec => !spec.CleanupScanIgnored && spec.Id > scan.LastProcessedSpecId)
                .OrderBy(spec => spec.Id)
                .Take(batchSize)
                .Select(spec => new ScanFactsProjection(
                    spec.Id,
                    spec.ReferenceVersion,
                    spec.ReferenceCount,
                    spec.UpdatedAt ?? spec.ImportedAt,
                    _db.AcceptanceSpecReferenceEvents
                        .Where(item => item.AcceptanceSpecId == spec.Id)
                        .Sum(item => (long?)item.OccurrenceCount) ?? 0,
                    _db.AcceptanceSpecReferenceEvents
                        .Where(item => item.AcceptanceSpecId == spec.Id && item.ReferencedAtUtc == null)
                        .Sum(item => (long?)item.OccurrenceCount) ?? 0,
                    _db.AcceptanceSpecReferenceEvents
                        .Where(item => item.AcceptanceSpecId == spec.Id && item.ReferencedAtUtc != null)
                        .Max(item => (DateTime?)item.ReferencedAtUtc)))
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                scan.Status = AcceptanceSpecCleanupScanStatus.Completed;
                scan.CompletedAtUtc = now;
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            }

            var thresholds = new SpecCleanupThresholds(scan.NewItemGraceDays, scan.UnusedDays);
            foreach (var item in batch)
            {
                var decision = AcceptanceSpecCleanupClassifier.Classify(
                    new SpecCleanupFacts(
                        AsUtcOffset(item.ContentActivityAtUtc),
                        item.CurrentReferenceCount,
                        item.RecordedReferenceCount,
                        item.UntrackedReferenceCount,
                        item.LastReferencedAtUtc.HasValue ? AsUtcOffset(item.LastReferencedAtUtc.Value) : null),
                    thresholds,
                    AsUtcOffset(scan.CreatedAtUtc));
                var category = (AcceptanceSpecCleanupCategory)(int)decision.Category;
                _db.AcceptanceSpecCleanupScanItems.Add(new AcceptanceSpecCleanupScanItem
                {
                    ScanId = scan.Id,
                    AcceptanceSpecId = item.SpecId,
                    ReferenceVersion = item.ReferenceVersion,
                    CurrentReferenceCount = item.CurrentReferenceCount,
                    RecordedReferenceCount = item.RecordedReferenceCount,
                    UntrackedReferenceCount = item.UntrackedReferenceCount,
                    LastReferencedAtUtc = item.LastReferencedAtUtc,
                    ContentActivityAtUtc = item.ContentActivityAtUtc,
                    Category = category,
                    Reason = (AcceptanceSpecCleanupReason)(int)decision.Reason,
                    ReviewStatus = AcceptanceSpecCleanupReviewStatus.Pending,
                    ScannedAtUtc = scan.CreatedAtUtc
                });
                scan.ProcessedCount++;
                if (category == AcceptanceSpecCleanupCategory.RecommendedCleanup) scan.RecommendedCleanupCount++;
                else if (category == AcceptanceSpecCleanupCategory.ManualReview) scan.ManualReviewCount++;
                else scan.HealthyCount++;
                scan.LastProcessedSpecId = item.SpecId;
            }
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            scan.Status = AcceptanceSpecCleanupScanStatus.Failed;
            scan.CompletedAtUtc = UtcNow();
            scan.ErrorMessage = "扫描失败，请重新发起";
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogError(ex, "验收规格清理扫描失败: {ScanId}", scan.Id);
            return true;
        }
    }

    public async Task<int> CleanupExpiredScansAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = UtcNow().AddDays(-_options.ScanRetentionDays);
        var expired = await _db.AcceptanceSpecCleanupScans
            .Where(scan => scan.CompletedAtUtc < cutoff &&
                           (scan.Status == AcceptanceSpecCleanupScanStatus.Completed ||
                            scan.Status == AcceptanceSpecCleanupScanStatus.Cancelled ||
                            scan.Status == AcceptanceSpecCleanupScanStatus.Failed))
            .OrderBy(scan => scan.CompletedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);
        if (expired.Count == 0)
            return 0;
        _db.AcceptanceSpecCleanupScans.RemoveRange(expired);
        await _db.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    private async Task<SpecCleanupBatchResult> UpdateScanItemsAsync(
        SpecAccessContext scope,
        IReadOnlyCollection<SpecCleanupActionItem> commands,
        CleanupAction action,
        CancellationToken cancellationToken)
    {
        if (commands.Count is < 1 or > MaxBatchActionItems)
            throw new ApplicationServiceException(400, $"每次请选择 1-{MaxBatchActionItems} 条结果");

        var results = new List<SpecCleanupActionResult>(commands.Count);
        foreach (var command in commands.DistinctBy(item => item.ScanItemId))
        {
            var item = await _db.AcceptanceSpecCleanupScanItems.IgnoreQueryFilters()
                .Include(value => value.Scan)
                .Include(value => value.AcceptanceSpec)
                .ThenInclude(spec => spec.WordFile)
                .SingleOrDefaultAsync(value => value.Id == command.ScanItemId, cancellationToken);
            if (item is null || item.Scan.CompanyId != scope.CompanyId ||
                item.Scan.RequestedByUserId != scope.UserId || !scope.CanAccess(item.AcceptanceSpec))
            {
                results.Add(new(command.ScanItemId, null, false, "扫描结果不存在或无权操作"));
                continue;
            }
            var spec = item.AcceptanceSpec;
            if (spec.WordFile.CompanyId != scope.CompanyId)
            {
                results.Add(new(item.Id, spec.Id, false, "规格归属已变化，请重新扫描"));
                continue;
            }
            if (spec.CleanupStatus != AcceptanceSpecCleanupStatus.Active)
            {
                results.Add(new(item.Id, spec.Id, false, "规格状态已变化，请刷新后重试"));
                continue;
            }
            if (!await SnapshotStillMatchesAsync(item, spec, cancellationToken))
            {
                results.Add(new(item.Id, spec.Id, false, "规格版本或引用已变化，请重新扫描"));
                continue;
            }

            switch (action)
            {
                case CleanupAction.Keep:
                    item.ReviewStatus = AcceptanceSpecCleanupReviewStatus.Kept;
                    break;
                case CleanupAction.Ignore:
                    spec.CleanupScanIgnored = true;
                    spec.CleanupScanIgnoredAtUtc = UtcNow();
                    spec.CleanupScanIgnoredByUserId = scope.UserId;
                    spec.CleanupScanIgnoreReason = NormalizeReason(command.Reason);
                    break;
                case CleanupAction.Quarantine:
                    var now = UtcNow();
                    spec.CleanupStatus = AcceptanceSpecCleanupStatus.Quarantined;
                    spec.QuarantinedAtUtc = now;
                    spec.QuarantineExpiresAtUtc = now.AddDays(_options.QuarantineDays);
                    spec.QuarantinedByUserId = scope.UserId;
                    spec.QuarantineReason = NormalizeReason(command.Reason) ?? ReasonLabel(item.Reason);
                    spec.QuarantineSourceScanId = item.ScanId;
                    spec.QuarantinedReferenceVersion = spec.ReferenceVersion;
                    var caches = await _db.EmbeddingCaches.IgnoreQueryFilters()
                        .Where(cache => cache.SpecId == spec.Id)
                        .ToListAsync(cancellationToken);
                    _db.EmbeddingCaches.RemoveRange(caches);
                    break;
            }
            results.Add(new(item.Id, spec.Id, true, action switch
            {
                CleanupAction.Keep => "本次保留",
                CleanupAction.Ignore => "已忽略后续扫描",
                _ => "已移入隔离区"
            }));
        }
        await _db.SaveChangesAsync(cancellationToken);
        return ToBatchResult(results);
    }

    private async Task<bool> SnapshotStillMatchesAsync(
        AcceptanceSpecCleanupScanItem item,
        AcceptanceSpec spec,
        CancellationToken cancellationToken)
    {
        if (spec.ReferenceVersion != item.ReferenceVersion ||
            spec.ReferenceCount != item.CurrentReferenceCount ||
            (spec.UpdatedAt ?? spec.ImportedAt) != item.ContentActivityAtUtc)
            return false;
        var recorded = await _db.AcceptanceSpecReferenceEvents.IgnoreQueryFilters()
            .Where(value => value.AcceptanceSpecId == spec.Id)
            .SumAsync(value => (long?)value.OccurrenceCount, cancellationToken) ?? 0;
        return recorded == item.RecordedReferenceCount;
    }

    private IQueryable<AcceptanceSpec> BuildActiveScopeQuery(SpecAccessContext scope)
    {
        var query = _db.AcceptanceSpecs.Where(spec => spec.WordFile.CompanyId == scope.CompanyId);
        return scope.ApplySpecScopeToQuery(query);
    }

    private async Task<AcceptanceSpec?> LoadManagedSpecAsync(
        SpecAccessContext scope,
        int id,
        CancellationToken cancellationToken)
    {
        var query = _db.AcceptanceSpecs.IgnoreQueryFilters()
            .Include(spec => spec.WordFile)
            .Where(spec => spec.Id == id && spec.WordFile.CompanyId == scope.CompanyId);
        return await scope.ApplySpecScopeToQuery(query).SingleOrDefaultAsync(cancellationToken);
    }

    private Task<AcceptanceSpecCleanupScan?> FindOwnedScanAsync(
        SpecAccessContext scope,
        string scanId,
        CancellationToken cancellationToken) =>
        _db.AcceptanceSpecCleanupScans.SingleOrDefaultAsync(scan =>
            scan.Id == scanId && scan.CompanyId == scope.CompanyId &&
            scan.RequestedByUserId == scope.UserId, cancellationToken);

    private async Task<AcceptanceSpecCleanupScan> RequireOwnedScanAsync(
        SpecAccessContext scope,
        string scanId,
        CancellationToken cancellationToken) =>
        await FindOwnedScanAsync(scope, scanId, cancellationToken) ??
        throw new ApplicationServiceException(404, "扫描任务不存在");

    private static SpecAccessContext RestoreScope(AcceptanceSpecCleanupScan scan) => new()
    {
        UserId = scan.RequestedByUserId,
        CompanyId = scan.CompanyId,
        IsAll = scan.IsAllScope,
        IncludeSelf = scan.IncludeSelf,
        OrgUnitIds = scan.ScopeOrgUnitIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, out var id) ? id : 0)
            .Where(id => id > 0).Distinct().ToArray()
    };

    private static SpecCleanupScanStatusModel ToStatus(AcceptanceSpecCleanupScan scan) => new(
        scan.Id, scan.Status, scan.NewItemGraceDays, scan.UnusedDays,
        scan.TotalCount, scan.ProcessedCount, scan.RecommendedCleanupCount,
        scan.ManualReviewCount, scan.HealthyCount, scan.CreatedAtUtc,
        scan.StartedAtUtc, scan.CompletedAtUtc, scan.ErrorMessage);

    private static SpecCleanupBatchResult ToBatchResult(List<SpecCleanupActionResult> results) =>
        new(results.Count(item => item.Success), results.Count(item => !item.Success), results);

    private static (int Page, int PageSize) NormalizePage(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));

    private static List<int> NormalizeIds(IReadOnlyCollection<int> ids)
    {
        var values = ids.Where(id => id > 0).Distinct().Take(MaxBatchActionItems + 1).ToList();
        if (values.Count is < 1 or > MaxBatchActionItems)
            throw new ApplicationServiceException(400, $"每次请选择 1-{MaxBatchActionItems} 条规格");
        return values;
    }

    private static string? NormalizeReason(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 500)];

    private static string ReasonLabel(AcceptanceSpecCleanupReason reason) => reason switch
    {
        AcceptanceSpecCleanupReason.NeverReferenced => "从未引用",
        AcceptanceSpecCleanupReason.LongUnused => "长期未引用",
        AcceptanceSpecCleanupReason.UntrackedHistoricalReferences => "迁移前引用时间不可追溯",
        AcceptanceSpecCleanupReason.CurrentVersionNeverReferenced => "当前版本尚未引用",
        _ => "人工清理"
    };

    private static void ClearQuarantine(AcceptanceSpec spec)
    {
        spec.QuarantinedAtUtc = null;
        spec.QuarantineExpiresAtUtc = null;
        spec.QuarantinedByUserId = null;
        spec.QuarantineReason = null;
        spec.QuarantineSourceScanId = null;
        spec.QuarantinedReferenceVersion = null;
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static DateTimeOffset AsUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record ScanFactsProjection(
        int SpecId,
        long ReferenceVersion,
        long CurrentReferenceCount,
        DateTime ContentActivityAtUtc,
        long RecordedReferenceCount,
        long UntrackedReferenceCount,
        DateTime? LastReferencedAtUtc);

    private enum CleanupAction { Keep, Ignore, Quarantine }
}
