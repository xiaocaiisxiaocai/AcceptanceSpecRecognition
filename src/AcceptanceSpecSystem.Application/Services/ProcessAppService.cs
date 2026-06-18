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
        var process = await _unitOfWork.Processes.GetByIdAsync(id);
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

        await _unitOfWork.Processes.AddAsync(process);
        await _unitOfWork.SaveChangesAsync();

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
        var process = await _unitOfWork.Processes.GetByIdAsync(id);
        if (process == null)
            return null;

        process.Name = NormalizeRequiredName(processName, "制程名称不能为空");
        _unitOfWork.Processes.Update(process);
        await _unitOfWork.SaveChangesAsync();

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
        var process = await _unitOfWork.Processes.GetByIdAsync(id);
        if (process == null)
            return false;

        _unitOfWork.Processes.Remove(process);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("删除制程成功: {ProcessId} - {ProcessName}", process.Id, process.Name);
        return true;
    }

    public async Task<PagedResult<AcceptanceSpecSummary>?> GetSpecsAsync(
        SpecAccessContext scope,
        int id,
        int page,
        int pageSize,
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        var process = await _unitOfWork.Processes.GetByIdAsync(id);
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
