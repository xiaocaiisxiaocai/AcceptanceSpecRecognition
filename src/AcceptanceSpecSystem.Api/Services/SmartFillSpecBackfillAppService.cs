using System.Security.Claims;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 智能填充编辑值回填验收规格服务。
/// </summary>
public sealed class SmartFillSpecBackfillAppService
{
    private const string ManualFileName = "__MANUAL_ENTRY__";
    private const string ManualFileHash = "manual_entry_placeholder";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthDataScopeService _authDataScopeService;

    public SmartFillSpecBackfillAppService(
        IUnitOfWork unitOfWork,
        IAuthDataScopeService authDataScopeService)
    {
        _unitOfWork = unitOfWork;
        _authDataScopeService = authDataScopeService;
    }

    public async Task<MatchingOperationResult<SmartFillSpecBackfillResponse>> BackfillAsync(
        ClaimsPrincipal user,
        SmartFillSpecBackfillRequest request)
    {
        var scope = await SpecDataScopeHelper.ResolveScopeAsync(user, _authDataScopeService);
        if (scope == null)
        {
            throw Failure(401, "会话缺少用户上下文");
        }

        if (request.Items.Count == 0)
        {
            throw Failure(400, "请选择要回填的编辑项");
        }

        if (!request.CustomerId.HasValue || request.CustomerId.Value <= 0)
        {
            throw Failure(400, "回填验收规格必须选择客户");
        }

        await EnsureBackfillScopeExistsAsync(request.CustomerId.Value, request.ProcessId, request.MachineModelId);

        // 先完成所有校验，再做写入，避免部分成功造成主数据不一致。
        var normalizedItems = request.Items.Select(NormalizeItem).ToList();
        var specIds = normalizedItems
            .Where(item => item.SpecId.HasValue)
            .Select(item => item.SpecId!.Value)
            .Distinct()
            .ToArray();

        var specLookup = specIds.Length == 0
            ? new Dictionary<int, AcceptanceSpec>()
            : (await _unitOfWork.AcceptanceSpecs.FindAsync(spec => specIds.Contains(spec.Id)))
                .ToDictionary(spec => spec.Id);

        foreach (var item in normalizedItems)
        {
            if (item.SpecId.HasValue)
            {
                if (!specLookup.TryGetValue(item.SpecId.Value, out var spec))
                {
                    throw Failure(404, "验收规格不存在");
                }

                if (!SpecDataScopeHelper.CanAccess(spec, scope))
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
            ? await GetOrCreateManualWordFileAsync(scope)
            : null;

        foreach (var item in normalizedItems)
        {
            if (item.SpecId.HasValue)
            {
                var spec = specLookup[item.SpecId.Value];
                spec.Acceptance = item.OverrideAcceptance;
                spec.Remark = item.OverrideRemark;
                _unitOfWork.AcceptanceSpecs.Update(spec);
                await RemoveEmbeddingCachesAsync(spec.Id);
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
                OwnerOrgUnitId = scope.OrgUnitId,
                CreatedByUserId = scope.UserId,
                WordFileId = manualWordFile!.Id,
                ImportedAt = DateTime.UtcNow
            });
            response.CreatedCount++;
        }

        await _unitOfWork.SaveChangesAsync();
        return new MatchingOperationResult<SmartFillSpecBackfillResponse>(response, "回填验收规格成功");
    }

    private async Task EnsureBackfillScopeExistsAsync(int customerId, int? processId, int? machineModelId)
    {
        if (await _unitOfWork.Customers.GetByIdAsync(customerId) == null)
        {
            throw Failure(400, "所选客户不存在");
        }

        if (processId.HasValue && await _unitOfWork.Processes.GetByIdAsync(processId.Value) == null)
        {
            throw Failure(400, "所选制程不存在");
        }

        if (machineModelId.HasValue && await _unitOfWork.MachineModels.GetByIdAsync(machineModelId.Value) == null)
        {
            throw Failure(400, "所选机型不存在");
        }
    }

    private async Task<WordFile> GetOrCreateManualWordFileAsync(DataScopeResult scope)
    {
        var existingFile = await _unitOfWork.WordFiles.FirstOrDefaultAsync(wordFile => wordFile.FileName == ManualFileName);
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

        await _unitOfWork.WordFiles.AddAsync(wordFile);
        await _unitOfWork.SaveChangesAsync();
        return wordFile;
    }

    private async Task RemoveEmbeddingCachesAsync(int specId)
    {
        var caches = await _unitOfWork.EmbeddingCaches.GetBySpecIdAsync(specId);
        if (caches.Count > 0)
        {
            _unitOfWork.EmbeddingCaches.RemoveRange(caches);
        }
    }

    private static NormalizedBackfillItem NormalizeItem(SmartFillSpecBackfillItem item)
    {
        var acceptance = NormalizeOptionalText(item.OverrideAcceptance);
        var remark = NormalizeOptionalText(item.OverrideRemark);
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

    private static MatchingApiException Failure(int code, string message)
    {
        return new MatchingApiException(code, message, isNotFound: code == 404);
    }

    private sealed record NormalizedBackfillItem(
        int? SpecId,
        string? SourceProject,
        string? SourceSpecification,
        string? OverrideAcceptance,
        string? OverrideRemark);
}
