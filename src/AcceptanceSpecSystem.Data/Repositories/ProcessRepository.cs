using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 制程Repository实现
/// </summary>
public class ProcessRepository : Repository<Process>, IProcessRepository
{
    /// <summary>
    /// 创建ProcessRepository实例
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public ProcessRepository(AppDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 获取制程并包含其验收规格集合（<see cref="Process.AcceptanceSpecs"/>），用于统计规格数量等场景。
    /// </summary>
    /// <param name="id">制程ID</param>
    /// <returns>制程（包含验收规格）或 null</returns>
    public async Task<Process?> GetWithSpecCountAsync(int id)
    {
        return await _dbSet
            .Include(p => p.AcceptanceSpecs)
            .FirstOrDefaultAsync(p => p.Id == id);
    }
}
