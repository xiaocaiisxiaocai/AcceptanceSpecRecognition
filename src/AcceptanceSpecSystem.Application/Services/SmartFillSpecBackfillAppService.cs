using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
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

    public SmartFillSpecBackfillAppService(
        IUnitOfWork unitOfWork,
        IAuthDataScopeService authDataScopeService,
        IDocumentFileAccessService documentFileAccessService,
        IBusinessOrgScopeService businessOrgScopeService)
    {
        _unitOfWork = unitOfWork;
        _authDataScopeService = authDataScopeService;
        _documentFileAccessService = documentFileAccessService;
        _businessOrgScopeService = businessOrgScopeService;
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
            .Where(item => item.SpecId.HasValue)
            .Select(item => item.SpecId!.Value)
            .Distinct()
            .ToArray();

        var specLookup = specIds.Length == 0
            ? new Dictionary<int, AcceptanceSpec>()
            : (await _unitOfWork.AcceptanceSpecs.FindAsync(
                    spec => specIds.Contains(spec.Id),
                    cancellationToken))
                .ToDictionary(spec => spec.Id);

        foreach (var item in normalizedItems)
        {
            if (item.SpecId.HasValue)
            {
                if (!specLookup.TryGetValue(item.SpecId.Value, out var spec))
                {
                    throw Failure(404, "验收规格不存在");
                }

                if (!SpecDataScopeRules.CanAccess(spec, businessScope))
                {
                    throw Failure(403, "无权回填该验收规格");
                }
            }
            else
            {
                _ = RequireText(item.SourceProject, "新增规格的项目不能为空");
                _ = RequireText(item.SourceSpecification, "新增规格的规格不能为空");
            }
        }

        var response = new SmartFillSpecBackfillResponse();
        var manualWordFile = normalizedItems.Any(item => !item.SpecId.HasValue)
            ? await GetOrCreateManualWordFileAsync(businessScope, cancellationToken)
            : null;

        foreach (var item in normalizedItems)
        {
            if (item.SpecId.HasValue)
            {
                var spec = specLookup[item.SpecId.Value];
                if (item.OverrideAcceptance != null)
                {
                    spec.Acceptance = item.OverrideAcceptance;
                }

                if (item.OverrideRemark != null)
                {
                    spec.Remark = item.OverrideRemark;
                }
                spec.UpdatedAt = DateTime.UtcNow;
                _unitOfWork.AcceptanceSpecs.Update(spec);
                await RemoveEmbeddingCachesAsync(spec.Id, cancellationToken);
                response.UpdatedCount++;
                continue;
            }

            await _unitOfWork.AcceptanceSpecs.AddAsync(new AcceptanceSpec
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
            }, cancellationToken);
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
        if (acceptance == null && remark == null)
        {
            throw Failure(400, "回填项缺少编辑后的验收标准或备注");
        }

        return new NormalizedBackfillItem(
            item.SpecId,
            NormalizeOptionalText(item.SourceProject),
            NormalizeOptionalText(item.SourceSpecification),
            acceptance,
            remark);
    }

    private static void EnsureDistinctBackfillSpecIds(IReadOnlyCollection<NormalizedBackfillItem> items)
    {
        var duplicateSpecId = items
            .Where(item => item.SpecId.HasValue)
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
        string? OverrideRemark);
}
