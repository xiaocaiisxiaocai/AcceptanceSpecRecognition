using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 验收规格Repository实现
/// </summary>
public class AcceptanceSpecRepository : Repository<AcceptanceSpec>, IAcceptanceSpecRepository
{
    /// <summary>
    /// 创建AcceptanceSpecRepository实例
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public AcceptanceSpecRepository(AppDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 获取所有验收规格，并包含 <see cref="AcceptanceSpec.Customer"/> / <see cref="AcceptanceSpec.Process"/> /
    /// <see cref="AcceptanceSpec.MachineModel"/> 导航属性。
    /// 用途：列表页需要展示客户/制程/机型名称时，避免出现空值。
    /// </summary>
    /// <returns>验收规格列表</returns>
    public async Task<IReadOnlyList<AcceptanceSpec>> GetAllWithCustomerAndProcessAsync()
    {
        return await _dbSet
            .Include(s => s.Customer)
            .Include(s => s.Process)
            .Include(s => s.MachineModel)
            .ToListAsync();
    }

    /// <summary>
    /// 根据ID获取验收规格，并包含 <see cref="AcceptanceSpec.Customer"/> / <see cref="AcceptanceSpec.Process"/> /
    /// <see cref="AcceptanceSpec.MachineModel"/> 导航属性。
    /// </summary>
    /// <param name="id">验收规格ID</param>
    /// <returns>验收规格（包含客户与制程）或 null</returns>
    public async Task<AcceptanceSpec?> GetByIdWithCustomerAndProcessAsync(int id)
    {
        return await _dbSet
            .Include(s => s.Customer)
            .Include(s => s.Process)
            .Include(s => s.MachineModel)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    /// <summary>
    /// 根据制程ID获取验收规格列表。
    /// </summary>
    /// <param name="processId">制程ID</param>
    /// <returns>验收规格列表</returns>
    public async Task<IReadOnlyList<AcceptanceSpec>> GetByProcessIdAsync(int processId)
    {
        return await _dbSet
            .Where(s => s.ProcessId == processId)
            .ToListAsync();
    }

    /// <summary>
    /// 根据Word文件ID获取验收规格列表。
    /// </summary>
    /// <param name="wordFileId">Word文件ID</param>
    /// <returns>验收规格列表</returns>
    public async Task<IReadOnlyList<AcceptanceSpec>> GetByWordFileIdAsync(int wordFileId)
    {
        return await _dbSet
            .Where(s => s.WordFileId == wordFileId)
            .ToListAsync();
    }

    /// <summary>
    /// 按制程ID分页获取验收规格列表（按ID升序）。
    /// </summary>
    /// <param name="processId">制程ID</param>
    /// <param name="pageNumber">页码（从1开始）</param>
    /// <param name="pageSize">每页数量</param>
    /// <returns>验收规格列表</returns>
    public async Task<IReadOnlyList<AcceptanceSpec>> GetPagedAsync(int processId, int pageNumber, int pageSize)
    {
        return await _dbSet
            .Where(s => s.ProcessId == processId)
            .OrderBy(s => s.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    /// <summary>
    /// 获取验收规格及其来源Word文件信息（包含 <see cref="AcceptanceSpec.WordFile"/> 导航属性）。
    /// </summary>
    /// <param name="id">验收规格ID</param>
    /// <returns>验收规格（包含来源Word文件）或 null</returns>
    public async Task<AcceptanceSpec?> GetWithWordFileAsync(int id)
    {
        return await _dbSet
            .Include(s => s.WordFile)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    /// <summary>
    /// 在指定制程范围内按关键字搜索验收规格。
    /// </summary>
    /// <param name="processId">制程ID</param>
    /// <param name="searchTerm">搜索关键词</param>
    /// <returns>验收规格列表</returns>
    public async Task<IReadOnlyList<AcceptanceSpec>> SearchAsync(int processId, string searchTerm)
    {
        var term = searchTerm.ToLower();
        return await _dbSet
            .Where(s => s.ProcessId == processId &&
                       (s.Project.ToLower().Contains(term) ||
                        s.Specification.ToLower().Contains(term) ||
                        (s.Acceptance != null && s.Acceptance.ToLower().Contains(term)) ||
                        (s.Remark != null && s.Remark.ToLower().Contains(term))))
            .ToListAsync();
    }

    public async Task<(IReadOnlyList<AcceptanceSpec> Items, int Total)> GetPagedWithFilterAsync(AcceptanceSpecQueryOptions options)
    {
        var page = options.Page;
        var pageSize = options.PageSize;

        var query = CreateFilteredQuery(options, includeNavigation: true);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(s => s.ImportedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<IReadOnlyList<AcceptanceSpec>> GetFilteredWithIncludesAsync(AcceptanceSpecQueryOptions options)
    {
        return await CreateFilteredQuery(options, includeNavigation: true)
            .OrderByDescending(s => s.ImportedAt)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<AcceptanceSpecGroupSummaryItem>> GetGroupSummaryWithFilterAsync(AcceptanceSpecQueryOptions options)
    {
        var query = CreateFilteredQuery(options, includeNavigation: false);

        return await query
            .GroupBy(s => new
            {
                s.CustomerId,
                CustomerName = s.Customer.Name,
                s.MachineModelId,
                MachineModelName = s.MachineModel != null ? s.MachineModel.Name : null,
                s.ProcessId,
                ProcessName = s.Process != null ? s.Process.Name : null
            })
            .Select(g => new AcceptanceSpecGroupSummaryItem
            {
                CustomerId = g.Key.CustomerId,
                CustomerName = g.Key.CustomerName,
                MachineModelId = g.Key.MachineModelId,
                MachineModelName = g.Key.MachineModelName,
                ProcessId = g.Key.ProcessId,
                ProcessName = g.Key.ProcessName,
                SpecCount = g.Count()
            })
            .OrderBy(g => g.CustomerName)
            .ThenBy(g => g.MachineModelName)
            .ThenBy(g => g.ProcessName)
            .ToListAsync();
    }

    private IQueryable<AcceptanceSpec> CreateFilteredQuery(AcceptanceSpecQueryOptions options, bool includeNavigation)
    {
        var query = Query();
        if (includeNavigation)
        {
            query = query
                .Include(s => s.Customer)
                .Include(s => s.Process)
                .Include(s => s.MachineModel);
        }

        return ApplyFilters(ApplyScope(query, options), options);
    }

    private static IQueryable<AcceptanceSpec> ApplyScope(IQueryable<AcceptanceSpec> query, AcceptanceSpecQueryOptions options)
    {
        if (options.IsAll)
            return query;

        var scopedOrgUnitIds = options.OrgUnitIds.Distinct().ToArray();

        if (options.IncludeSelf && scopedOrgUnitIds.Length > 0)
        {
            return query.Where(s =>
                (s.CreatedByUserId.HasValue && s.CreatedByUserId.Value == options.UserId) ||
                (s.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(s.OwnerOrgUnitId.Value)));
        }

        if (options.IncludeSelf)
        {
            return query.Where(s =>
                s.CreatedByUserId.HasValue && s.CreatedByUserId.Value == options.UserId);
        }

        if (scopedOrgUnitIds.Length > 0)
        {
            return query.Where(s =>
                s.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(s.OwnerOrgUnitId.Value));
        }

        return query.Where(_ => false);
    }

    private static IQueryable<AcceptanceSpec> ApplyFilters(IQueryable<AcceptanceSpec> query, AcceptanceSpecQueryOptions options)
    {
        if (options.ProcessId.HasValue)
        {
            query = query.Where(spec => spec.ProcessId == options.ProcessId.Value);
        }
        else if (options.ProcessIdIsNull == true)
        {
            query = query.Where(spec => spec.ProcessId == null);
        }

        if (options.MachineModelId.HasValue)
        {
            query = query.Where(spec => spec.MachineModelId == options.MachineModelId.Value);
        }
        else if (options.MachineModelIdIsNull == true)
        {
            query = query.Where(spec => spec.MachineModelId == null);
        }

        if (options.CustomerId.HasValue)
        {
            query = query.Where(spec => spec.CustomerId == options.CustomerId.Value);
        }

        if (!string.IsNullOrWhiteSpace(options.Keyword))
        {
            var keyword = options.Keyword.Trim();
            query = query.Where(spec =>
                spec.Project.Contains(keyword) ||
                spec.Specification.Contains(keyword) ||
                (spec.Acceptance != null && spec.Acceptance.Contains(keyword)) ||
                (spec.Remark != null && spec.Remark.Contains(keyword)) ||
                spec.Customer.Name.Contains(keyword) ||
                (spec.MachineModel != null && spec.MachineModel.Name.Contains(keyword)) ||
                (spec.Process != null && spec.Process.Name.Contains(keyword)));
        }

        if (options.ImportedFrom.HasValue)
        {
            query = query.Where(spec => spec.ImportedAt >= options.ImportedFrom.Value);
        }

        if (options.ImportedTo.HasValue)
        {
            query = query.Where(spec => spec.ImportedAt <= options.ImportedTo.Value);
        }

        return query;
    }
}
