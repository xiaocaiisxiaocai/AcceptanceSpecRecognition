using AcceptanceSpecSystem.Application.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 机型用例服务。
/// </summary>
public sealed class MachineModelAppService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly AcceptanceSpecQueryService _acceptanceSpecQueryService;
    private readonly ILogger<MachineModelAppService> _logger;

    public MachineModelAppService(
        IUnitOfWork unitOfWork,
        AcceptanceSpecQueryService acceptanceSpecQueryService,
        ILogger<MachineModelAppService> logger)
    {
        _unitOfWork = unitOfWork;
        _acceptanceSpecQueryService = acceptanceSpecQueryService;
        _logger = logger;
    }

    public async Task<PagedResult<MachineModelSummary>> GetPagedAsync(
        SpecAccessContext scope,
        int page,
        int pageSize,
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _unitOfWork.MachineModels.Query();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var normalizedKeyword = keyword.Trim();
            query = query.Where(machineModel => machineModel.Name.Contains(normalizedKeyword));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(machineModel => machineModel.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(machineModel => new MachineModelSummary
            {
                Id = machineModel.Id,
                Name = machineModel.Name,
                CreatedAt = machineModel.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var specCountByModel = await _acceptanceSpecQueryService.GetSpecCountByMachineModelAsync(
            scope,
            rows.Select(item => item.Id).ToArray(),
            cancellationToken);

        foreach (var row in rows)
        {
            row.SpecCount = specCountByModel.TryGetValue(row.Id, out var specCount) ? specCount : 0;
        }

        return new PagedResult<MachineModelSummary>
        {
            Items = rows,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<MachineModelSummary?> GetByIdAsync(
        SpecAccessContext scope,
        int id,
        CancellationToken cancellationToken = default)
    {
        var model = await _unitOfWork.MachineModels.GetByIdAsync(id, cancellationToken);
        if (model == null)
            return null;

        var specCountByModel = await _acceptanceSpecQueryService.GetSpecCountByMachineModelAsync(
            scope,
            [id],
            cancellationToken);
        return new MachineModelSummary
        {
            Id = model.Id,
            Name = model.Name,
            CreatedAt = model.CreatedAt,
            SpecCount = specCountByModel.TryGetValue(id, out var specCount) ? specCount : 0
        };
    }

    public async Task<MachineModelSummary> CreateAsync(
        string machineModelName,
        CancellationToken cancellationToken = default)
    {
        var name = NormalizeRequiredName(machineModelName, "机型名称不能为空");
        var model = new MachineModel
        {
            Name = name,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.MachineModels.AddAsync(model);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("创建机型成功: {MachineModelId} - {MachineModelName}", model.Id, model.Name);

        return new MachineModelSummary
        {
            Id = model.Id,
            Name = model.Name,
            CreatedAt = model.CreatedAt,
            SpecCount = 0
        };
    }

    public async Task<MachineModelSummary?> UpdateAsync(
        SpecAccessContext scope,
        int id,
        string machineModelName,
        CancellationToken cancellationToken = default)
    {
        var model = await _unitOfWork.MachineModels.GetByIdAsync(id);
        if (model == null)
            return null;

        model.Name = NormalizeRequiredName(machineModelName, "机型名称不能为空");
        _unitOfWork.MachineModels.Update(model);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("更新机型成功: {MachineModelId} - {MachineModelName}", model.Id, model.Name);

        var specCountByModel = await _acceptanceSpecQueryService.GetSpecCountByMachineModelAsync(
            scope,
            [id],
            cancellationToken);
        return new MachineModelSummary
        {
            Id = model.Id,
            Name = model.Name,
            CreatedAt = model.CreatedAt,
            SpecCount = specCountByModel.TryGetValue(id, out var specCount) ? specCount : 0
        };
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var model = await _unitOfWork.MachineModels.GetByIdAsync(id);
        if (model == null)
            return false;

        var specCount = await _unitOfWork.AcceptanceSpecs.CountAsync(
            spec => spec.MachineModelId == id,
            cancellationToken);
        if (specCount > 0)
        {
            throw new ApplicationServiceException(
                409,
                $"该机型下还有 {specCount} 条关联验收规格，无法删除，请先清理关联数据");
        }

        _unitOfWork.MachineModels.Remove(model);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw DeleteConflict("该机型下新增了关联验收规格，无法删除，请刷新后重试");
        }

        _logger.LogInformation("删除机型成功: {MachineModelId} - {MachineModelName}", model.Id, model.Name);
        return true;
    }

    /// <summary>
    /// 批量删除机型：整体在一个事务内执行，逐项校验关联规格并单独回报失败原因。
    /// </summary>
    public async Task<BatchDeleteResultModel> BatchDeleteAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default)
    {
        var result = new BatchDeleteResultModel();
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var id in ids)
            {
                var model = await _unitOfWork.MachineModels.GetByIdAsync(id, cancellationToken);
                if (model == null)
                {
                    result.Failures.Add(new BatchDeleteFailureModel { Id = id, Reason = "机型不存在" });
                    continue;
                }

                var specCount = await _unitOfWork.AcceptanceSpecs.CountAsync(
                    spec => spec.MachineModelId == id,
                    cancellationToken);
                if (specCount > 0)
                {
                    result.Failures.Add(new BatchDeleteFailureModel
                    {
                        Id = id,
                        Reason = $"存在 {specCount} 条关联验收规格，无法删除"
                    });
                    continue;
                }

                _unitOfWork.MachineModels.Remove(model);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                result.SucceededIds.Add(id);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw DeleteConflict("删除期间关联验收规格发生变化，请刷新后重试");
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        if (result.SucceededIds.Count > 0)
        {
            _logger.LogInformation("批量删除机型成功: {MachineModelIds}", string.Join(",", result.SucceededIds));
        }

        return result;
    }

    private static ApplicationServiceException DeleteConflict(string message) => new(409, message);

    private static string NormalizeRequiredName(string? value, string message)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ApplicationServiceException(400, message);

        return normalized;
    }
}
