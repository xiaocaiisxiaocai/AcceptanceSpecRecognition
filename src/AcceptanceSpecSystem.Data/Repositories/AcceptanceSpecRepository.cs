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
    /// 获取所有验收规格，并包含 <see cref="AcceptanceSpec.Customer"/> 与 <see cref="AcceptanceSpec.Process"/> 导航属性。
    /// 用途：列表页需要展示客户/制程名称时，避免出现空值。
    /// </summary>
    /// <returns>验收规格列表</returns>
    public async Task<IReadOnlyList<AcceptanceSpec>> GetAllWithCustomerAndProcessAsync()
    {
        return await _dbSet
            .Include(s => s.Customer)
            .Include(s => s.Process)
            .ToListAsync();
    }

    /// <summary>
    /// 根据ID获取验收规格，并包含 <see cref="AcceptanceSpec.Customer"/> 与 <see cref="AcceptanceSpec.Process"/> 导航属性。
    /// </summary>
    /// <param name="id">验收规格ID</param>
    /// <returns>验收规格（包含客户与制程）或 null</returns>
    public async Task<AcceptanceSpec?> GetByIdWithCustomerAndProcessAsync(int id)
    {
        return await _dbSet
            .Include(s => s.Customer)
            .Include(s => s.Process)
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
}
