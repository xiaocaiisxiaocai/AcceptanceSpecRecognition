using System.Data;
using System.Security.Cryptography;
using System.Text;
using AcceptanceSpecSystem.Application.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 验规写入与用例编排服务。
/// </summary>
public sealed class AcceptanceSpecAppService
{
    private const string ManualFileName = "__MANUAL_ENTRY__";
    private const string ManualFileHash = "manual_entry_placeholder";
    private const int RemarkMaxLength = 2000;
    private const int RemarkPreviewLength = 160;

    private readonly IUnitOfWork _unitOfWork;
    private readonly AcceptanceSpecQueryService _acceptanceSpecQueryService;
    private readonly IBusinessOrgScopeService _businessOrgScopeService;
    private readonly AcceptanceSpecContentVersionCoordinator _contentVersionCoordinator;
    private readonly ILogger<AcceptanceSpecAppService> _logger;

    public AcceptanceSpecAppService(
        IUnitOfWork unitOfWork,
        AcceptanceSpecQueryService acceptanceSpecQueryService,
        IBusinessOrgScopeService businessOrgScopeService,
        ILogger<AcceptanceSpecAppService> logger)
        : this(
            unitOfWork,
            acceptanceSpecQueryService,
            businessOrgScopeService,
            new AcceptanceSpecContentVersionCoordinator(unitOfWork),
            logger)
    {
    }

    public AcceptanceSpecAppService(
        IUnitOfWork unitOfWork,
        AcceptanceSpecQueryService acceptanceSpecQueryService,
        IBusinessOrgScopeService businessOrgScopeService,
        AcceptanceSpecContentVersionCoordinator contentVersionCoordinator,
        ILogger<AcceptanceSpecAppService> logger)
    {
        _unitOfWork = unitOfWork;
        _acceptanceSpecQueryService = acceptanceSpecQueryService;
        _businessOrgScopeService = businessOrgScopeService;
        _contentVersionCoordinator = contentVersionCoordinator;
        _logger = logger;
    }

    public Task<List<SpecGroupSummary>> GetGroupsAsync(
        SpecAccessContext scope,
        CancellationToken cancellationToken = default)
    {
        return _acceptanceSpecQueryService.GetGroupSummaryAsync(scope, cancellationToken);
    }

    public Task<PagedResult<AcceptanceSpecSummary>> GetPagedAsync(
        SpecAccessContext scope,
        int page,
        int pageSize,
        string? keyword = null,
        int? customerId = null,
        int? processId = null,
        int? machineModelId = null,
        bool? processIdIsNull = null,
        bool? machineModelIdIsNull = null,
        DateTime? importedFrom = null,
        DateTime? importedTo = null,
        CancellationToken cancellationToken = default)
    {
        return _acceptanceSpecQueryService.GetPagedAsync(
            scope,
            page,
            pageSize,
            keyword,
            customerId,
            processId,
            machineModelId,
            processIdIsNull,
            machineModelIdIsNull,
            importedFrom,
            importedTo,
            cancellationToken);
    }

    public Task<SpecDuplicateDetectionResultModel> GetDuplicateGroupsAsync(
        SpecAccessContext scope,
        string? keyword = null,
        int? customerId = null,
        int? processId = null,
        int? machineModelId = null,
        bool? processIdIsNull = null,
        bool? machineModelIdIsNull = null,
        double? minSimilarity = null,
        int? maxGroups = null,
        CancellationToken cancellationToken = default)
    {
        return _acceptanceSpecQueryService.GetDuplicateGroupsAsync(
            scope,
            keyword,
            customerId,
            processId,
            machineModelId,
            processIdIsNull,
            machineModelIdIsNull,
            minSimilarity,
            maxGroups,
            cancellationToken);
    }

    public async Task<AcceptanceSpecSummary?> GetByIdAsync(
        SpecAccessContext scope,
        int id,
        CancellationToken cancellationToken = default)
    {
        var spec = await _unitOfWork.AcceptanceSpecs.GetByIdWithCustomerAndProcessAsync(id, cancellationToken);
        if (spec == null)
            return null;

        if (!scope.CanAccess(spec))
            throw new ApplicationServiceException(403, "无权访问该规格");

        return AcceptanceSpecQueryService.MapDto(spec);
    }

    public async Task<AcceptanceSpecReferenceHistoryModel?> GetReferenceHistoryAsync(
        SpecAccessContext scope,
        int id,
        int page,
        int pageSize,
        bool includePreviousVersions,
        string? sort,
        CancellationToken cancellationToken = default)
    {
        var spec = await _unitOfWork.AcceptanceSpecs.GetByIdAsync(id, cancellationToken);
        if (spec == null)
            return null;

        if (!scope.CanAccess(spec))
            throw new ApplicationServiceException(403, "无权访问该规格");

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var normalizedSort = string.IsNullOrWhiteSpace(sort)
            ? "newest"
            : sort.Trim().ToLowerInvariant();
        if (normalizedSort is not ("oldest" or "newest"))
            throw new ApplicationServiceException(400, "引用历史排序只支持 oldest 或 newest");

        var allEvents = _unitOfWork.AcceptanceSpecReferenceEvents
            .Query()
            .Where(reference => reference.AcceptanceSpecId == id);
        var currentEvents = allEvents.Where(reference =>
            reference.ReferenceVersion == spec.ReferenceVersion);
        var selectedEvents = includePreviousVersions ? allEvents : currentEvents;

        var untrackedReferenceCount = await selectedEvents
            .Where(reference => !reference.ReferencedAtUtc.HasValue)
            .SumAsync(reference => (long?)reference.OccurrenceCount, cancellationToken) ?? 0;
        var recordedReferenceCount = await selectedEvents
            .Where(reference => reference.ReferencedAtUtc.HasValue)
            .SumAsync(reference => (long?)reference.OccurrenceCount, cancellationToken) ?? 0;

        var historyQuery = selectedEvents.Where(reference => reference.ReferencedAtUtc.HasValue);

        var total = await historyQuery.CountAsync(cancellationToken);
        var skip = (page - 1) * pageSize;
        var orderedQuery = normalizedSort == "oldest"
            ? historyQuery
                .OrderBy(reference => reference.ReferencedAtUtc)
                .ThenBy(reference => reference.TaskId)
                .ThenBy(reference => reference.TaskOccurrenceIndex)
                .ThenBy(reference => reference.Id)
            : historyQuery
                .OrderByDescending(reference => reference.ReferencedAtUtc)
                .ThenByDescending(reference => reference.TaskId)
                .ThenByDescending(reference => reference.TaskOccurrenceIndex)
                .ThenByDescending(reference => reference.Id);
        var events = await orderedQuery
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = events.Select((reference, index) =>
        {
            long? ordinal = null;
            if (!includePreviousVersions)
            {
                ordinal = normalizedSort == "oldest"
                    ? untrackedReferenceCount + skip + index + 1L
                    : untrackedReferenceCount + total - skip - index;
            }

            return new AcceptanceSpecReferenceHistoryItemModel
            {
                Id = reference.Id,
                ReferenceOrdinal = ordinal,
                ReferenceVersion = reference.ReferenceVersion,
                IsCurrentVersion = reference.ReferenceVersion == spec.ReferenceVersion,
                ReferencedAtUtc = reference.ReferencedAtUtc!.Value
            };
        }).ToList();

        return new AcceptanceSpecReferenceHistoryModel
        {
            SpecId = spec.Id,
            CurrentReferenceVersion = spec.ReferenceVersion,
            CurrentReferenceCount = spec.ReferenceCount,
            RecordedReferenceCount = recordedReferenceCount,
            UntrackedReferenceCount = untrackedReferenceCount,
            IncludePreviousVersions = includePreviousVersions,
            Sort = normalizedSort,
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<AcceptanceSpecContentVersionHistoryModel?> GetContentVersionHistoryAsync(
        SpecAccessContext scope,
        int id,
        int page,
        int pageSize,
        string? sort,
        CancellationToken cancellationToken = default)
    {
        var spec = await _unitOfWork.AcceptanceSpecs.GetByIdAsync(id, cancellationToken);
        if (spec == null)
            return null;
        if (!scope.CanAccess(spec))
            throw new ApplicationServiceException(403, "无权访问该规格");
        if (page < 1 || pageSize is < 1 or > 100)
            throw new ApplicationServiceException(400, "版本历史分页参数无效");

        var normalizedSort = string.IsNullOrWhiteSpace(sort) ? "newest" : sort.Trim().ToLowerInvariant();
        if (normalizedSort is not ("oldest" or "newest"))
            throw new ApplicationServiceException(400, "内容版本排序只支持 oldest 或 newest");

        var query = _unitOfWork.AcceptanceSpecContentVersions.Query()
            .Where(version => version.AcceptanceSpecId == id);
        var total = await query.CountAsync(cancellationToken);
        var earliestAvailableVersion = await query
            .OrderBy(version => version.Version)
            .Select(version => (long?)version.Version)
            .FirstOrDefaultAsync(cancellationToken);
        var ordered = normalizedSort == "oldest"
            ? query.OrderBy(version => version.Version)
            : query.OrderByDescending(version => version.Version);
        var pageVersions = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        AcceptanceSpecContentVersion? pagePredecessor = null;
        if (pageVersions.Count > 0)
        {
            var minimumPageVersion = pageVersions.Min(version => version.Version);
            pagePredecessor = await query
                .Where(version => version.Version < minimumPageVersion)
                .OrderByDescending(version => version.Version)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var versionsForComparison = pagePredecessor == null
            ? pageVersions
            : pageVersions.Append(pagePredecessor).ToList();
        var previousByVersion = versionsForComparison
            .OrderBy(version => version.Version)
            .Zip(
                versionsForComparison.OrderBy(version => version.Version).Skip(1),
                (previous, current) => new { current.Version, Previous = previous })
            .ToDictionary(pair => pair.Version, pair => pair.Previous);

        return new AcceptanceSpecContentVersionHistoryModel
        {
            SpecId = id,
            CurrentVersion = spec.ReferenceVersion,
            EarliestAvailableVersion = earliestAvailableVersion ?? spec.ReferenceVersion,
            HasUnavailableEarlierVersions = earliestAvailableVersion > 1,
            Sort = normalizedSort,
            Items = pageVersions.Select(version => MapContentVersionItem(
                version,
                previousByVersion.GetValueOrDefault(version.Version))).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<AcceptanceSpecContentVersionDetailModel?> GetContentVersionAsync(
        SpecAccessContext scope,
        int id,
        long version,
        CancellationToken cancellationToken = default)
    {
        var spec = await _unitOfWork.AcceptanceSpecs.GetByIdAsync(id, cancellationToken);
        if (spec == null)
            return null;
        if (!scope.CanAccess(spec))
            throw new ApplicationServiceException(403, "无权访问该规格");

        var snapshot = await _unitOfWork.AcceptanceSpecContentVersions.FirstOrDefaultAsync(
            item => item.AcceptanceSpecId == id && item.Version == version,
            cancellationToken);
        if (snapshot == null)
            return null;

        return MapContentVersionDetail(snapshot);
    }

    public async Task<AcceptanceSpecContentVersionDiffModel?> GetContentVersionDiffAsync(
        SpecAccessContext scope,
        int id,
        long fromVersion,
        long toVersion,
        CancellationToken cancellationToken = default)
    {
        var spec = await _unitOfWork.AcceptanceSpecs.GetByIdAsync(id, cancellationToken);
        if (spec == null)
            return null;
        if (!scope.CanAccess(spec))
            throw new ApplicationServiceException(403, "无权访问该规格");
        if (fromVersion <= 0 || toVersion <= 0 || fromVersion == toVersion)
            throw new ApplicationServiceException(400, "请选择两个不同的有效内容版本");

        var snapshots = await _unitOfWork.AcceptanceSpecContentVersions.Query()
            .Where(item => item.AcceptanceSpecId == id &&
                           (item.Version == fromVersion || item.Version == toVersion))
            .ToListAsync(cancellationToken);
        var from = snapshots.SingleOrDefault(item => item.Version == fromVersion);
        var to = snapshots.SingleOrDefault(item => item.Version == toVersion);
        if (from == null || to == null)
            throw new ApplicationServiceException(404, "指定内容版本不存在或正文不可追溯");

        return new AcceptanceSpecContentVersionDiffModel
        {
            SpecId = id,
            FromVersion = fromVersion,
            ToVersion = toVersion,
            Fields = new Dictionary<string, AcceptanceSpecContentFieldDiffModel>
            {
                ["project"] = BuildFieldDiff(from.Project, to.Project),
                ["specification"] = BuildFieldDiff(from.Specification, to.Specification),
                ["acceptance"] = BuildFieldDiff(from.Acceptance, to.Acceptance),
                ["remark"] = BuildFieldDiff(from.Remark, to.Remark)
            }
        };
    }

    public async Task<AcceptanceSpecSummary?> RestoreContentVersionAsync(
        SpecAccessContext scope,
        int id,
        long version,
        long expectedCurrentVersion,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var spec = await _unitOfWork.AcceptanceSpecs.GetByIdWithCustomerAndProcessAsync(id, cancellationToken);
        if (spec == null)
            return null;
        if (!scope.CanAccess(spec))
            throw new ApplicationServiceException(403, "无权操作该规格");
        if (spec.ReferenceVersion != expectedCurrentVersion)
            throw new ApplicationServiceException(409, "规格内容已被更新，请刷新版本历史后重试");

        var target = await _unitOfWork.AcceptanceSpecContentVersions.FirstOrDefaultAsync(
            item => item.AcceptanceSpecId == id && item.Version == version,
            cancellationToken);
        if (target == null)
            throw new ApplicationServiceException(404, "要恢复的内容版本不存在或正文不可追溯");
        if (!AcceptanceSpecContentVersionCoordinator.HasMaterialChange(
                spec,
                target.Project,
                target.Specification,
                target.Acceptance,
                target.Remark))
            throw new ApplicationServiceException(422, "所选版本与当前内容一致，无需恢复");

        await _contentVersionCoordinator.ApplyChangeAsync(
            spec,
            target.Project,
            target.Specification,
            target.Acceptance,
            target.Remark,
            "restore",
            scope.UserId,
            reason,
            version,
            cancellationToken: cancellationToken);
        await RemoveEmbeddingCachesAsync(spec.Id, cancellationToken);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApplicationServiceException(409, "规格内容已被更新，请刷新版本历史后重试");
        }

        return AcceptanceSpecQueryService.MapDto(spec);
    }

    public async Task<AcceptanceSpecSummary> CreateAsync(
        SpecAccessContext scope,
        int customerId,
        int? processId,
        int? machineModelId,
        string project,
        string specification,
        string? acceptance,
        string? remark,
        CancellationToken cancellationToken = default)
    {
        var customer = await RequireCustomerAsync(customerId, cancellationToken);
        var process = await RequireProcessAsync(processId, cancellationToken);
        var machineModel = await RequireMachineModelAsync(machineModelId, cancellationToken);
        var wordFile = await GetOrCreateManualWordFileAsync(scope, cancellationToken);

        var spec = new AcceptanceSpec
        {
            CustomerId = customerId,
            ProcessId = processId,
            MachineModelId = machineModelId,
            Project = NormalizeRequiredText(project, "项目名称不能为空"),
            Specification = NormalizeRequiredText(specification, "规格内容不能为空"),
            Acceptance = NormalizeOptionalText(acceptance),
            Remark = NormalizeOptionalText(remark),
            OwnerOrgUnitId = scope.OrgUnitId,
            CreatedByUserId = scope.UserId,
            WordFileId = wordFile.Id,
            ImportedAt = DateTime.UtcNow
        };

        await _unitOfWork.AcceptanceSpecs.AddAsync(spec, cancellationToken);
        await _contentVersionCoordinator.CreateInitialSnapshotAsync(
            spec,
            "create",
            scope.UserId,
            cancellationToken: cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("创建验收规格成功: {SpecId}", spec.Id);

        return new AcceptanceSpecSummary
        {
            Id = spec.Id,
            CustomerId = spec.CustomerId,
            ProcessId = spec.ProcessId,
            MachineModelId = spec.MachineModelId,
            ProcessName = process?.Name ?? string.Empty,
            MachineModelName = machineModel?.Name ?? string.Empty,
            CustomerName = customer.Name,
            Project = spec.Project,
            Specification = spec.Specification,
            Acceptance = spec.Acceptance,
            Remark = spec.Remark,
            ReferenceCount = spec.ReferenceCount,
            ReferenceVersion = spec.ReferenceVersion,
            ImportedAt = spec.ImportedAt,
            UpdatedAt = spec.UpdatedAt,
            OwnerOrgUnitId = spec.OwnerOrgUnitId,
            CreatedByUserId = spec.CreatedByUserId
        };
    }

    public async Task<AcceptanceSpecSummary?> UpdateAsync(
        SpecAccessContext scope,
        int id,
        string project,
        string specification,
        string? acceptance,
        string? remark,
        long? expectedReferenceVersion = null,
        string? changeReason = null,
        CancellationToken cancellationToken = default)
    {
        var spec = await _unitOfWork.AcceptanceSpecs.GetByIdWithCustomerAndProcessAsync(id, cancellationToken);
        if (spec == null)
            return null;

        if (!scope.CanAccess(spec))
            throw new ApplicationServiceException(403, "无权操作该规格");
        if (expectedReferenceVersion.HasValue && spec.ReferenceVersion != expectedReferenceVersion.Value)
            throw new ApplicationServiceException(409, "规格内容已被更新，请刷新后重试");

        var normalizedProject = NormalizeRequiredText(project, "项目名称不能为空");
        var normalizedSpecification = NormalizeRequiredText(specification, "规格内容不能为空");
        var normalizedAcceptance = NormalizeOptionalText(acceptance);
        var normalizedRemark = NormalizeOptionalText(remark);
        await _contentVersionCoordinator.ApplyChangeAsync(
            spec,
            normalizedProject,
            normalizedSpecification,
            normalizedAcceptance,
            normalizedRemark,
            "manual-update",
            scope.UserId,
            changeReason,
            cancellationToken: cancellationToken);

        await RemoveEmbeddingCachesAsync(spec.Id, cancellationToken);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApplicationServiceException(409, "规格内容已被更新，请刷新后重试");
        }

        _logger.LogInformation("更新验收规格成功: {SpecId}", spec.Id);
        return AcceptanceSpecQueryService.MapDto(spec);
    }

    public async Task<bool> DeleteAsync(
        SpecAccessContext scope,
        int id,
        CancellationToken cancellationToken = default)
    {
        var spec = await _unitOfWork.AcceptanceSpecs.GetByIdAsync(id, cancellationToken);
        if (spec == null)
            return false;

        if (!scope.CanAccess(spec))
            throw new ApplicationServiceException(403, "无权操作该规格");

        _unitOfWork.AcceptanceSpecs.Remove(spec);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("删除验收规格成功: {SpecId}", spec.Id);
        return true;
    }

    public async Task<SpecRemarkReplacePreviewModel> PreviewRemarkReplaceAsync(
        SpecAccessContext scope,
        string searchText,
        string replacementText,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        EnsureExactDepartmentScope(scope);
        if (page < 1)
            throw new ApplicationServiceException(400, "预览页码必须大于0");
        if (pageSize is < 1 or > 100)
            throw new ApplicationServiceException(400, "每页预览数量必须在1到100之间");
        var snapshot = await BuildRemarkReplaceSnapshotAsync(
            scope,
            searchText,
            replacementText,
            trackEntities: false,
            cancellationToken);
        return MapRemarkReplacePreview(snapshot, page, pageSize);
    }

    public async Task<SpecRemarkReplaceResultModel> ExecuteRemarkReplaceAsync(
        SpecAccessContext scope,
        string searchText,
        string replacementText,
        int expectedAffectedSpecCount,
        int expectedMatchCount,
        string confirmationToken,
        CancellationToken cancellationToken = default)
    {
        EnsureExactDepartmentScope(scope);
        var orgUnitId = scope.OrgUnitId.GetValueOrDefault();

        var lockKey = $"spec-remark-replace:{scope.CompanyId}:{orgUnitId}";
        await using var operationLock = await _unitOfWork.AcquireOperationLockAsync(
            lockKey,
            cancellationToken);
        var transactionStarted = false;
        try
        {
            await _unitOfWork.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            transactionStarted = true;
            var snapshot = await BuildRemarkReplaceSnapshotAsync(
                scope,
                searchText,
                replacementText,
                trackEntities: true,
                cancellationToken);
            if (snapshot.Specs.Count != expectedAffectedSpecCount ||
                snapshot.MatchCount != expectedMatchCount ||
                !MatchesConfirmationToken(snapshot.ConfirmationToken, confirmationToken))
            {
                throw new ApplicationServiceException(409, "备注数据已发生变化，请重新预览后再确认");
            }

            var now = DateTime.UtcNow;
            foreach (var spec in snapshot.Specs)
            {
                var updatedRemark = spec.Remark!.Replace(
                    snapshot.SearchText,
                    snapshot.ReplacementText,
                    StringComparison.Ordinal);
                await _contentVersionCoordinator.ApplyChangeAsync(
                    spec,
                    spec.Project,
                    spec.Specification,
                    spec.Acceptance,
                    updatedRemark,
                    "remark-replace",
                    scope.UserId,
                    changedAtUtc: now,
                    cancellationToken: cancellationToken);
            }

            var specIds = snapshot.Specs.Select(spec => spec.Id).ToArray();
            var caches = await _unitOfWork.EmbeddingCaches
                .Query(asNoTracking: false)
                .Where(cache => specIds.Contains(cache.SpecId))
                .ToListAsync(cancellationToken);
            if (caches.Count > 0)
            {
                _unitOfWork.EmbeddingCaches.RemoveRange(caches);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            transactionStarted = false;

            _logger.LogInformation(
                "部门内批量替换验收规格备注完成: OrgUnitId={OrgUnitId}, Updated={UpdatedCount}, Matches={MatchCount}",
                orgUnitId,
                snapshot.Specs.Count,
                snapshot.MatchCount);
            return new SpecRemarkReplaceResultModel
            {
                UpdatedSpecCount = snapshot.Specs.Count,
                ReplacedMatchCount = snapshot.MatchCount
            };
        }
        catch
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }
            throw;
        }
    }

    public async Task<BatchImportResultModel> BatchImportAsync(
        SpecAccessContext scope,
        int customerId,
        int? processId,
        int? machineModelId,
        int wordFileId,
        IReadOnlyCollection<BatchImportSpecItemInput> items,
        CancellationToken cancellationToken = default)
    {
        await RequireCustomerAsync(customerId, cancellationToken);
        await RequireProcessAsync(processId, cancellationToken);
        await RequireMachineModelAsync(machineModelId, cancellationToken);

        var wordFile = await _unitOfWork.WordFiles.GetByIdAsync(wordFileId, cancellationToken);
        if (wordFile == null)
            throw new ApplicationServiceException(400, "Word文件不存在");

        var businessScope = await _businessOrgScopeService.ResolveFileScopeAsync(
            scope,
            wordFile,
            cancellationToken);
        var successCount = 0;
        var failedCount = 0;

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Project) || string.IsNullOrWhiteSpace(item.Specification))
            {
                failedCount++;
                continue;
            }

            var spec = new AcceptanceSpec
            {
                CustomerId = customerId,
                ProcessId = processId,
                MachineModelId = machineModelId,
                Project = item.Project.Trim(),
                Specification = item.Specification.Trim(),
                Acceptance = NormalizeOptionalText(item.Acceptance),
                Remark = NormalizeOptionalText(item.Remark),
                OwnerOrgUnitId = businessScope.OrgUnitId,
                CreatedByUserId = businessScope.UserId,
                WordFileId = wordFileId,
                ImportedAt = DateTime.UtcNow
            };
            await _unitOfWork.AcceptanceSpecs.AddAsync(spec, cancellationToken);
            await _contentVersionCoordinator.CreateInitialSnapshotAsync(
                spec,
                "create",
                businessScope.UserId,
                cancellationToken: cancellationToken);

            successCount++;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("批量导入验收规格完成: 成功{Success}, 失败{Failed}", successCount, failedCount);

        return new BatchImportResultModel
        {
            SuccessCount = successCount,
            FailedCount = failedCount,
            TotalCount = items.Count
        };
    }

    public async Task<int> BatchDeleteAsync(
        SpecAccessContext scope,
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default)
    {
        var idList = BatchDeleteInputNormalizer.Normalize(
            ids,
            "请选择要删除的规格",
            cancellationToken);
        var query = _unitOfWork.AcceptanceSpecs.Query(asNoTracking: true)
            .Where(s =>
                idList.Contains(s.Id) &&
                s.WordFile.CompanyId == scope.CompanyId);

        // 将 scope.CanAccess 逻辑转为 SQL 谓词，避免加载实体到内存
        if (!scope.IsAll)
        {
            var orgUnitIds = scope.OrgUnitIds.ToList();
            query = query.Where(s =>
                (scope.IncludeSelf && s.CreatedByUserId == scope.UserId) ||
                (s.OwnerOrgUnitId.HasValue && orgUnitIds.Contains(s.OwnerOrgUnitId.Value)));
        }

        // ExecuteDeleteAsync 生成单条 DELETE WHERE Id IN (...) SQL，避免 N 次往返
        int deletedCount;
        try
        {
            deletedCount = await query.ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception ex) when (DatabaseConstraintClassifier.IsDeleteConflict(ex))
        {
            throw new ApplicationServiceException(409, "删除期间数据发生冲突，请刷新后重试");
        }

        if (deletedCount == 0)
            throw new ApplicationServiceException(403, "未找到可删除的规格或无权限");

        _logger.LogInformation("批量删除验收规格成功: {Count}条", deletedCount);
        return deletedCount;
    }

    private async Task<Customer> RequireCustomerAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        var customer = await _unitOfWork.Customers.GetByIdAsync(customerId, cancellationToken);
        return customer ?? throw new ApplicationServiceException(400, "所选客户不存在");
    }

    private async Task<Process?> RequireProcessAsync(
        int? processId,
        CancellationToken cancellationToken = default)
    {
        if (!processId.HasValue)
            return null;

        var process = await _unitOfWork.Processes.GetByIdAsync(processId.Value, cancellationToken);
        return process ?? throw new ApplicationServiceException(400, "所选制程不存在");
    }

    private async Task<MachineModel?> RequireMachineModelAsync(
        int? machineModelId,
        CancellationToken cancellationToken = default)
    {
        if (!machineModelId.HasValue)
            return null;

        var machineModel = await _unitOfWork.MachineModels.GetByIdAsync(machineModelId.Value, cancellationToken);
        return machineModel ?? throw new ApplicationServiceException(400, "所选机型不存在");
    }

    private async Task<WordFile> GetOrCreateManualWordFileAsync(
        SpecAccessContext scope,
        CancellationToken cancellationToken = default)
    {
        var existingFile = await _unitOfWork.WordFiles.FirstOrDefaultAsync(wordFile =>
            wordFile.FileName == ManualFileName &&
            wordFile.CompanyId == scope.CompanyId &&
            wordFile.CreatedByUserId == scope.UserId &&
            wordFile.OwnerOrgUnitId == scope.OrgUnitId,
            cancellationToken);
        if (existingFile != null)
            return existingFile;

        var wordFile = new WordFile
        {
            CompanyId = scope.CompanyId,
            CreatedByUserId = scope.UserId,
            OwnerOrgUnitId = scope.OrgUnitId,
            FileName = ManualFileName,
            FileContent = Array.Empty<byte>(),
            FileHash = ManualFileHash,
            UploadedAt = DateTime.UtcNow
        };

        await _unitOfWork.WordFiles.AddAsync(wordFile, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return wordFile;
    }

    private async Task<RemarkReplaceSnapshot> BuildRemarkReplaceSnapshotAsync(
        SpecAccessContext scope,
        string searchText,
        string replacementText,
        bool trackEntities,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            throw new ApplicationServiceException(400, "查找内容不能为空");
        if (searchText.Length > RemarkMaxLength)
            throw new ApplicationServiceException(400, "查找内容不能超过2000个字符");
        replacementText ??= string.Empty;
        if (replacementText.Length > RemarkMaxLength)
            throw new ApplicationServiceException(400, "替换内容不能超过2000个字符");

        var query = scope.ApplySpecScopeToQuery(
                _unitOfWork.AcceptanceSpecs.Query(asNoTracking: !trackEntities))
            .Where(spec =>
                spec.WordFile.CompanyId == scope.CompanyId &&
                spec.Remark != null &&
                spec.Remark.Contains(searchText));
        var candidates = await query
            .OrderBy(spec => spec.Id)
            .ToListAsync(cancellationToken);
        var matchedSpecs = candidates
            .Where(spec => spec.Remark!.Contains(searchText, StringComparison.Ordinal))
            .ToList();
        var matchCount = 0;
        foreach (var spec in matchedSpecs)
        {
            matchCount += CountOccurrences(spec.Remark!, searchText);
            var replaced = spec.Remark!.Replace(searchText, replacementText, StringComparison.Ordinal);
            if (replaced.Length > RemarkMaxLength)
            {
                throw new ApplicationServiceException(
                    422,
                    $"规格 {spec.Id} 替换后的备注超过2000个字符，请缩短替换内容");
            }
        }

        var tokenSource = new StringBuilder()
            .Append(scope.CompanyId).Append('|')
            .Append(scope.OrgUnitId).Append('|')
            .Append(searchText).Append('|')
            .Append(replacementText).Append('|')
            .Append(matchCount);
        foreach (var spec in matchedSpecs)
        {
            tokenSource.Append('|').Append(spec.Id).Append(':').Append(spec.Remark);
        }

        return new RemarkReplaceSnapshot
        {
            SearchText = searchText,
            ReplacementText = replacementText,
            Specs = matchedSpecs,
            MatchCount = matchCount,
            ConfirmationToken = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(tokenSource.ToString())))
        };
    }

    private static SpecRemarkReplacePreviewModel MapRemarkReplacePreview(
        RemarkReplaceSnapshot snapshot,
        int page,
        int pageSize)
    {
        var skip = (long)(page - 1) * pageSize;
        IEnumerable<AcceptanceSpec> pageSpecs = skip >= snapshot.Specs.Count
            ? []
            : snapshot.Specs.Skip((int)skip).Take(pageSize);
        return new SpecRemarkReplacePreviewModel
        {
            AffectedSpecCount = snapshot.Specs.Count,
            MatchCount = snapshot.MatchCount,
            ConfirmationToken = snapshot.ConfirmationToken,
            SamplePage = page,
            SamplePageSize = pageSize,
            SampleTotal = snapshot.Specs.Count,
            Samples = pageSpecs
                .Select(spec => new SpecRemarkReplaceSampleModel
                {
                    SpecId = spec.Id,
                    Project = spec.Project,
                    BeforePreview = TruncateRemarkPreview(spec.Remark!),
                    AfterPreview = TruncateRemarkPreview(
                        spec.Remark!.Replace(
                            snapshot.SearchText,
                            snapshot.ReplacementText,
                            StringComparison.Ordinal))
                })
                .ToList()
        };
    }

    private static int CountOccurrences(string value, string searchText)
    {
        var count = 0;
        var startIndex = 0;
        while ((startIndex = value.IndexOf(searchText, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += searchText.Length;
        }
        return count;
    }

    private static void EnsureExactDepartmentScope(SpecAccessContext scope)
    {
        var scopedOrgUnitIds = scope.OrgUnitIds.Distinct().ToArray();
        if (scope.IsAll ||
            scope.IncludeSelf ||
            !scope.OrgUnitId.HasValue ||
            scopedOrgUnitIds.Length != 1 ||
            scopedOrgUnitIds[0] != scope.OrgUnitId.Value)
        {
            throw new ApplicationServiceException(400, "该操作必须绑定唯一的具体部门");
        }
    }

    private static bool MatchesConfirmationToken(string expected, string? actual)
    {
        if (string.IsNullOrEmpty(actual) || expected.Length != actual.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(actual));
    }

    private static string TruncateRemarkPreview(string value) =>
        value.Length <= RemarkPreviewLength
            ? value
            : $"{value[..(RemarkPreviewLength - 3)]}...";

    private async Task RemoveEmbeddingCachesAsync(int specId, CancellationToken cancellationToken = default)
    {
        var caches = await _unitOfWork.EmbeddingCaches.GetBySpecIdAsync(specId, cancellationToken);
        if (caches.Count > 0)
        {
            _unitOfWork.EmbeddingCaches.RemoveRange(caches);
        }
    }

    private static string NormalizeRequiredText(string? value, string message)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ApplicationServiceException(400, message);

        return normalized;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static AcceptanceSpecContentVersionItemModel MapContentVersionItem(
        AcceptanceSpecContentVersion version,
        AcceptanceSpecContentVersion? previous)
    {
        return new AcceptanceSpecContentVersionItemModel
        {
            Version = version.Version,
            ChangedAtUtc = version.ChangedAtUtc,
            ChangedByUserId = version.ChangedByUserId,
            ChangedByNameSnapshot = version.ChangedByNameSnapshot,
            ChangeSource = version.ChangeSource,
            ChangeReason = version.ChangeReason,
            RestoredFromVersion = version.RestoredFromVersion,
            IsMigrationBaseline = version.IsMigrationBaseline,
            ChangedFields = GetChangedFields(previous, version)
        };
    }

    private static AcceptanceSpecContentVersionDetailModel MapContentVersionDetail(
        AcceptanceSpecContentVersion version)
    {
        return new AcceptanceSpecContentVersionDetailModel
        {
            SpecId = version.AcceptanceSpecId,
            Version = version.Version,
            Project = version.Project,
            Specification = version.Specification,
            Acceptance = version.Acceptance,
            Remark = version.Remark,
            ChangedAtUtc = version.ChangedAtUtc,
            ChangedByUserId = version.ChangedByUserId,
            ChangedByNameSnapshot = version.ChangedByNameSnapshot,
            ChangeSource = version.ChangeSource,
            ChangeReason = version.ChangeReason,
            RestoredFromVersion = version.RestoredFromVersion,
            IsMigrationBaseline = version.IsMigrationBaseline,
            ChangedFields = []
        };
    }

    private static List<string> GetChangedFields(
        AcceptanceSpecContentVersion? previous,
        AcceptanceSpecContentVersion current)
    {
        if (previous == null)
            return ["project", "specification", "acceptance", "remark"];

        var fields = new List<string>();
        if (!string.Equals(previous.Project, current.Project, StringComparison.Ordinal)) fields.Add("project");
        if (!string.Equals(previous.Specification, current.Specification, StringComparison.Ordinal)) fields.Add("specification");
        if (!string.Equals(previous.Acceptance, current.Acceptance, StringComparison.Ordinal)) fields.Add("acceptance");
        if (!string.Equals(previous.Remark, current.Remark, StringComparison.Ordinal)) fields.Add("remark");
        return fields;
    }

    private static AcceptanceSpecContentFieldDiffModel BuildFieldDiff(string? before, string? after) =>
        new()
        {
            Before = before,
            After = after,
            Changed = !string.Equals(before, after, StringComparison.Ordinal)
        };

    private sealed class RemarkReplaceSnapshot
    {
        public string SearchText { get; init; } = string.Empty;

        public string ReplacementText { get; init; } = string.Empty;

        public List<AcceptanceSpec> Specs { get; init; } = [];

        public int MatchCount { get; init; }

        public string ConfirmationToken { get; init; } = string.Empty;
    }
}
