using AcceptanceSpecSystem.Application.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 制程用例服务。
/// </summary>
public sealed class ProcessAppService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly AcceptanceSpecQueryService _acceptanceSpecQueryService;
    private readonly ILogger<ProcessAppService> _logger;

    public ProcessAppService(
        IUnitOfWork unitOfWork,
        AcceptanceSpecQueryService acceptanceSpecQueryService,
        ILogger<ProcessAppService> logger)
    {
        _unitOfWork = unitOfWork;
        _acceptanceSpecQueryService = acceptanceSpecQueryService;
        _logger = logger;
    }

    public async Task<PagedResult<ProcessSummary>> GetPagedAsync(
        SpecAccessContext scope,
        int page,
        int pageSize,
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _unitOfWork.Processes.Query();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var normalizedKeyword = keyword.Trim();
            query = query.Where(process => process.Name.Contains(normalizedKeyword));
        }

        // 先分页取制程，再回填关联规格数量；这样列表查询只做当前页的轻量聚合。
        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(process => process.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(process => new ProcessSummary
            {
                Id = process.Id,
                Name = process.Name,
                CreatedAt = process.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var specCountByProcess = await _acceptanceSpecQueryService.GetSpecCountByProcessAsync(
            scope,
            rows.Select(item => item.Id).ToArray(),
            cancellationToken);

        foreach (var row in rows)
        {
            row.SpecCount = specCountByProcess.TryGetValue(row.Id, out var specCount) ? specCount : 0;
        }

        return new PagedResult<ProcessSummary>
        {
            Items = rows,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<ProcessSummary?> GetByIdAsync(
        SpecAccessContext scope,
        int id,
        CancellationToken cancellationToken = default)
    {
        var process = await _unitOfWork.Processes.GetByIdAsync(id, cancellationToken);
        if (process == null)
            return null;

        // 详情页复用同一套按数据范围统计规格数的逻辑，避免列表/详情口径不一致。
        var specCountByProcess = await _acceptanceSpecQueryService.GetSpecCountByProcessAsync(
            scope,
            [id],
            cancellationToken);
        return new ProcessSummary
        {
            Id = process.Id,
            Name = process.Name,
            CreatedAt = process.CreatedAt,
            SpecCount = specCountByProcess.TryGetValue(id, out var specCount) ? specCount : 0
        };
    }

    public async Task<ProcessSummary> CreateAsync(
        string processName,
        CancellationToken cancellationToken = default)
    {
        var name = NormalizeRequiredName(processName, "制程名称不能为空");
        var process = new Process
        {
            Name = name,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Processes.AddAsync(process, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("创建制程成功: {ProcessId} - {ProcessName}", process.Id, process.Name);

        return new ProcessSummary
        {
            Id = process.Id,
            Name = process.Name,
            CreatedAt = process.CreatedAt,
            SpecCount = 0
        };
    }

    public async Task<ProcessSummary?> UpdateAsync(
        SpecAccessContext scope,
        int id,
        string processName,
        CancellationToken cancellationToken = default)
    {
        var process = await _unitOfWork.Processes.GetByIdAsync(id, cancellationToken);
        if (process == null)
            return null;

        process.Name = NormalizeRequiredName(processName, "制程名称不能为空");
        _unitOfWork.Processes.Update(process);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("更新制程成功: {ProcessId} - {ProcessName}", process.Id, process.Name);

        var specCountByProcess = await _acceptanceSpecQueryService.GetSpecCountByProcessAsync(
            scope,
            [id],
            cancellationToken);
        return new ProcessSummary
        {
            Id = process.Id,
            Name = process.Name,
            CreatedAt = process.CreatedAt,
            SpecCount = specCountByProcess.TryGetValue(id, out var specCount) ? specCount : 0
        };
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var process = await _unitOfWork.Processes.GetByIdAsync(id, cancellationToken);
        if (process == null)
            return false;

        var specCount = await _unitOfWork.AcceptanceSpecs.CountAsync(
            spec => spec.ProcessId == id,
            cancellationToken);
        if (specCount > 0)
        {
            throw new ApplicationServiceException(
                409,
                $"该制程下还有 {specCount} 条关联验收规格，无法删除，请先清理关联数据");
        }

        _unitOfWork.Processes.Remove(process);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DatabaseConstraintClassifier.IsDeleteConflict(ex))
        {
            throw DeleteConflict("该制程下新增了关联验收规格，无法删除，请刷新后重试");
        }

        _logger.LogInformation("删除制程成功: {ProcessId} - {ProcessName}", process.Id, process.Name);
        return true;
    }

    /// <summary>
    /// 批量删除制程：整体在一个事务内执行，逐项校验关联规格并单独回报失败原因。
    /// </summary>
    public async Task<BatchDeleteResultModel> BatchDeleteAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default)
    {
        var normalizedIds = BatchDeleteInputNormalizer.Normalize(
            ids,
            "请选择要删除的制程",
            cancellationToken);
        var result = new BatchDeleteResultModel();
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var processes = await _unitOfWork.Processes
                .Query(asNoTracking: false)
                .Where(process => normalizedIds.Contains(process.Id))
                .ToListAsync(cancellationToken);
            var processById = processes.ToDictionary(process => process.Id);
            var referenceCountById = await _unitOfWork.AcceptanceSpecs
                .Query()
                .Where(spec => spec.ProcessId.HasValue && normalizedIds.Contains(spec.ProcessId.Value))
                .GroupBy(spec => spec.ProcessId!.Value)
                .Select(group => new { Id = group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.Id, item => item.Count, cancellationToken);
            var eligible = new List<Process>();

            foreach (var id in normalizedIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!processById.TryGetValue(id, out var process))
                {
                    result.Failures.Add(new BatchDeleteFailureModel { Id = id, Reason = "制程不存在" });
                    continue;
                }

                var specCount = referenceCountById.GetValueOrDefault(id);
                if (specCount > 0)
                {
                    result.Failures.Add(new BatchDeleteFailureModel
                    {
                        Id = id,
                        Reason = $"存在 {specCount} 条关联验收规格，无法删除"
                    });
                    continue;
                }

                eligible.Add(process);
                result.SucceededIds.Add(id);
            }

            if (eligible.Count > 0)
            {
                _unitOfWork.Processes.RemoveRange(eligible);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            await TransactionRollbackHelper.TryRollbackAsync(_unitOfWork);
            if (DatabaseConstraintClassifier.IsDeleteConflict(ex))
                throw DeleteConflict("删除期间关联验收规格发生变化，请刷新后重试");

            throw;
        }
        catch
        {
            await TransactionRollbackHelper.TryRollbackAsync(_unitOfWork);
            throw;
        }

        if (result.SucceededIds.Count > 0)
        {
            _logger.LogInformation("批量删除制程成功: {ProcessIds}", string.Join(",", result.SucceededIds));
        }

        return result;
    }

    private static ApplicationServiceException DeleteConflict(string message) => new(409, message);

    public async Task<PagedResult<AcceptanceSpecSummary>?> GetSpecsAsync(
        SpecAccessContext scope,
        int id,
        int page,
        int pageSize,
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        var process = await _unitOfWork.Processes.GetByIdAsync(id, cancellationToken);
        if (process == null)
            return null;

        return await _acceptanceSpecQueryService.GetPagedAsync(
            scope,
            page,
            pageSize,
            keyword,
            processId: id,
            cancellationToken: cancellationToken);
    }

    private static string NormalizeRequiredName(string? value, string message)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ApplicationServiceException(400, message);

        return normalized;
    }
}
