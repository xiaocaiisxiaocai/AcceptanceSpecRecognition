using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using static AcceptanceSpecSystem.Application.Services.MatchingResultHelpers;

namespace AcceptanceSpecSystem.Application.Services;

public interface ISmartFillSpecBackfillAppService
{
    Task<MatchingOperationResult<SmartFillSpecBackfillResponse>> BackfillAsync(
        MatchingUserContext user,
        SmartFillSpecBackfillRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 智能填充编辑值回填验收规格服务。
/// </summary>
public sealed class SmartFillSpecBackfillAppService : ISmartFillSpecBackfillAppService
{
    private const string ManualFileName = "__MANUAL_ENTRY__";
    private const string ManualFileHash = "manual_entry_placeholder";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthDataScopeService _authDataScopeService;
    private readonly IDocumentFileAccessService _documentFileAccessService;
    private readonly IBusinessOrgScopeService _businessOrgScopeService;
    private readonly AcceptanceSpecContentVersionCoordinator _contentVersionCoordinator;

    public SmartFillSpecBackfillAppService(
        IUnitOfWork unitOfWork,
        IAuthDataScopeService authDataScopeService,
        IDocumentFileAccessService documentFileAccessService,
        IBusinessOrgScopeService businessOrgScopeService)
        : this(
            unitOfWork,
            authDataScopeService,
            documentFileAccessService,
            businessOrgScopeService,
            new AcceptanceSpecContentVersionCoordinator(unitOfWork))
    {
    }

    public SmartFillSpecBackfillAppService(
        IUnitOfWork unitOfWork,
        IAuthDataScopeService authDataScopeService,
        IDocumentFileAccessService documentFileAccessService,
        IBusinessOrgScopeService businessOrgScopeService,
        AcceptanceSpecContentVersionCoordinator contentVersionCoordinator)
    {
        _unitOfWork = unitOfWork;
        _authDataScopeService = authDataScopeService;
        _documentFileAccessService = documentFileAccessService;
        _businessOrgScopeService = businessOrgScopeService;
        _contentVersionCoordinator = contentVersionCoordinator;
    }

    public async Task<MatchingOperationResult<SmartFillSpecBackfillResponse>> BackfillAsync(
        MatchingUserContext user,
        SmartFillSpecBackfillRequest request,
        CancellationToken cancellationToken = default)
    {
        var scope = await _authDataScopeService.GetScopeAsync(
            user.UserId,
            user.CompanyId,
            "spec",
            cancellationToken);
        if (scope == null)
        {
            throw Failure(401, "会话缺少用户上下文");
        }

        DataScopeResult businessScope;
        if (request.FileId.HasValue && request.FileId.Value > 0)
        {
            var wordFile = await _documentFileAccessService.GetAccessibleWordFileAsync(
                request.FileId.Value,
                scope);
            if (wordFile == null)
            {
                throw Failure(400, "源文件不存在");
            }

            businessScope = await _businessOrgScopeService.ResolveFileScopeAsync(
                scope,
                wordFile,
                cancellationToken);
        }
        else
        {
            businessScope = await _businessOrgScopeService.ResolveCurrentScopeAsync(
                scope,
                user.IsAdmin,
                cancellationToken);
        }

        if (request.Items.Count == 0)
        {
            throw Failure(400, "请选择要回填的编辑项");
        }

        if (!request.CustomerId.HasValue || request.CustomerId.Value <= 0)
        {
            throw Failure(400, "回填验收规格必须选择客户");
        }

        await EnsureBackfillScopeExistsAsync(
            request.CustomerId.Value,
            request.ProcessId,
            request.MachineModelId,
            cancellationToken);

        // 先完成所有校验，再做写入，避免部分成功造成主数据不一致。
        var normalizedItems = request.Items.Select(NormalizeItem).ToList();
        EnsureDistinctBackfillSpecIds(normalizedItems);
        var specIds = normalizedItems
            .Where(item => item.Decision is BackfillDecision.LegacyUpdate or BackfillDecision.Overwrite)
            .Select(item => item.SpecId!.Value)
            .Distinct()
            .ToArray();

        var specLookup = specIds.Length == 0
            ? new Dictionary<int, AcceptanceSpec>()
            : (await _unitOfWork.AcceptanceSpecs
                    .Query(asNoTracking: false)
                    .Where(spec => specIds.Contains(spec.Id))
                    .ToListAsync(cancellationToken))
                .ToDictionary(spec => spec.Id);

        foreach (var item in normalizedItems)
        {
            if (item.Decision == BackfillDecision.Skip)
            {
                continue;
            }

            if (item.Decision is BackfillDecision.LegacyUpdate or BackfillDecision.Overwrite)
            {
                if (!specLookup.TryGetValue(item.SpecId.GetValueOrDefault(), out var spec))
                {
                    throw Failure(404, "验收规格不存在");
                }

                if (!SpecDataScopeRules.CanAccess(spec, businessScope))
                {
                    throw Failure(403, "无权回填该验收规格");
                }

                if (item.Decision == BackfillDecision.Overwrite)
                {
                    _ = RequireText(item.SourceProject, "覆盖规格的项目不能为空");
                    _ = RequireText(item.SourceSpecification, "覆盖规格的规格不能为空");
                }
            }
            else
            {
                _ = RequireText(item.SourceProject, "新增规格的项目不能为空");
                _ = RequireText(item.SourceSpecification, "新增规格的规格不能为空");
            }
        }

        var response = new SmartFillSpecBackfillResponse();
        var manualWordFile = normalizedItems.Any(item =>
                item.Decision is BackfillDecision.LegacyCreate or BackfillDecision.Create)
            ? await GetOrCreateManualWordFileAsync(businessScope, cancellationToken)
            : null;

        foreach (var item in normalizedItems)
        {
            if (item.Decision == BackfillDecision.Skip)
            {
                response.SkippedCount++;
                continue;
            }

            if (item.Decision is BackfillDecision.LegacyUpdate or BackfillDecision.Overwrite)
            {
                var spec = specLookup[item.SpecId.GetValueOrDefault()];
                if (item.Decision == BackfillDecision.Overwrite)
                {
                    var updatedProject = RequireText(item.SourceProject, "覆盖规格的项目不能为空");
                    var updatedSpecification = RequireText(item.SourceSpecification, "覆盖规格的规格不能为空");
                    await _contentVersionCoordinator.ApplyChangeAsync(
                        spec,
                        updatedProject,
                        updatedSpecification,
                        item.OverrideAcceptance,
                        item.OverrideRemark,
                        "smart-fill-backfill",
                        businessScope.UserId,
                        cancellationToken: cancellationToken);
                }
                else
                {
                    var updatedAcceptance = item.OverrideAcceptance ?? spec.Acceptance;
                    var updatedRemark = item.OverrideRemark ?? spec.Remark;
                    await _contentVersionCoordinator.ApplyChangeAsync(
                        spec,
                        spec.Project,
                        spec.Specification,
                        updatedAcceptance,
                        updatedRemark,
                        "smart-fill-backfill",
                        businessScope.UserId,
                        cancellationToken: cancellationToken);
                }

                await RemoveEmbeddingCachesAsync(spec.Id, cancellationToken);
                response.UpdatedCount++;
                continue;
            }

            var createdSpec = new AcceptanceSpec
            {
                CustomerId = request.CustomerId.Value,
                ProcessId = request.ProcessId,
                MachineModelId = request.MachineModelId,
                Project = RequireText(item.SourceProject, "新增规格的项目不能为空"),
                Specification = RequireText(item.SourceSpecification, "新增规格的规格不能为空"),
                Acceptance = item.OverrideAcceptance,
                Remark = item.OverrideRemark,
                OwnerOrgUnitId = businessScope.OrgUnitId,
                CreatedByUserId = businessScope.UserId,
                WordFileId = manualWordFile!.Id,
                ImportedAt = DateTime.UtcNow
            };
            await _unitOfWork.AcceptanceSpecs.AddAsync(createdSpec, cancellationToken);
            await _contentVersionCoordinator.CreateInitialSnapshotAsync(
                createdSpec,
                "smart-fill-backfill",
                businessScope.UserId,
                cancellationToken: cancellationToken);
            response.CreatedCount++;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return new MatchingOperationResult<SmartFillSpecBackfillResponse>(response, "回填验收规格成功");
    }

    private async Task EnsureBackfillScopeExistsAsync(
        int customerId,
        int? processId,
        int? machineModelId,
        CancellationToken cancellationToken)
    {
        if (await _unitOfWork.Customers.GetByIdAsync(customerId, cancellationToken) == null)
        {
            throw Failure(400, "所选客户不存在");
        }

        if (processId.HasValue &&
            await _unitOfWork.Processes.GetByIdAsync(processId.Value, cancellationToken) == null)
        {
            throw Failure(400, "所选制程不存在");
        }

        if (machineModelId.HasValue &&
            await _unitOfWork.MachineModels.GetByIdAsync(machineModelId.Value, cancellationToken) == null)
        {
            throw Failure(400, "所选机型不存在");
        }
    }

    private async Task<WordFile> GetOrCreateManualWordFileAsync(
        DataScopeResult scope,
        CancellationToken cancellationToken)
    {
        var existingFile = await _unitOfWork.WordFiles.FirstOrDefaultAsync(wordFile =>
            wordFile.FileName == ManualFileName &&
            wordFile.CompanyId == scope.CompanyId &&
            wordFile.CreatedByUserId == scope.UserId &&
            wordFile.OwnerOrgUnitId == scope.OrgUnitId,
            cancellationToken);
        if (existingFile != null)
        {
            return existingFile;
        }

        var wordFile = new WordFile
        {
            CompanyId = scope.CompanyId,
            CreatedByUserId = scope.UserId,
            OwnerOrgUnitId = scope.OrgUnitId,
            FileName = ManualFileName,
            FileContent = [],
            FileHash = ManualFileHash,
            UploadedAt = DateTime.UtcNow
        };

        await _unitOfWork.WordFiles.AddAsync(wordFile, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return wordFile;
    }

    private async Task RemoveEmbeddingCachesAsync(
        int specId,
        CancellationToken cancellationToken)
    {
        var caches = await _unitOfWork.EmbeddingCaches.GetBySpecIdAsync(specId, cancellationToken);
        if (caches.Count > 0)
        {
            _unitOfWork.EmbeddingCaches.RemoveRange(caches);
        }
    }

    private static NormalizedBackfillItem NormalizeItem(SmartFillSpecBackfillItem item)
    {
        var acceptance = NormalizeOverrideText(item.OverrideAcceptance);
        var remark = NormalizeOverrideText(item.OverrideRemark);
        var explicitDecision = NormalizeOptionalText(item.Decision)?.ToLowerInvariant();
        var decision = explicitDecision switch
        {
            null => item.SpecId.HasValue
                ? BackfillDecision.LegacyUpdate
                : BackfillDecision.LegacyCreate,
            "overwrite" => BackfillDecision.Overwrite,
            "create" => BackfillDecision.Create,
            "skip" => BackfillDecision.Skip,
            _ => throw Failure(400, "写库决策必须为 overwrite、create 或 skip")
        };

        if (decision is BackfillDecision.LegacyUpdate or BackfillDecision.LegacyCreate &&
            acceptance == null && remark == null)
        {
            throw Failure(400, "回填项缺少编辑后的验收标准或备注");
        }

        if (decision == BackfillDecision.Overwrite && !item.SpecId.HasValue)
        {
            throw Failure(400, "覆盖已有规格必须提供规格ID");
        }

        return new NormalizedBackfillItem(
            item.SpecId,
            NormalizeOptionalText(item.SourceProject),
            NormalizeOptionalText(item.SourceSpecification),
            acceptance,
            remark,
            decision);
    }

    private static void EnsureDistinctBackfillSpecIds(IReadOnlyCollection<NormalizedBackfillItem> items)
    {
        var duplicateSpecId = items
            .Where(item =>
                item.Decision is BackfillDecision.LegacyUpdate or BackfillDecision.Overwrite)
            .GroupBy(item => item.SpecId!.Value)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;

        if (duplicateSpecId.HasValue)
        {
            throw Failure(400, $"同一验收规格存在重复回填项：{duplicateSpecId.Value}");
        }
    }

    private static string RequireText(string? value, string message)
    {
        var normalized = NormalizeOptionalText(value);
        if (normalized == null)
        {
            throw Failure(400, message);
        }

        return normalized;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeOverrideText(string? value)
    {
        return value == null ? null : value.Trim();
    }

    private sealed record NormalizedBackfillItem(
        int? SpecId,
        string? SourceProject,
        string? SourceSpecification,
        string? OverrideAcceptance,
        string? OverrideRemark,
        BackfillDecision Decision);

    private enum BackfillDecision
    {
        LegacyUpdate,
        LegacyCreate,
        Overwrite,
        Create,
        Skip
    }
}
