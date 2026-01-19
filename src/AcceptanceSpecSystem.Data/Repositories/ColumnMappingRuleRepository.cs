using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 导入列映射规则 Repository 实现（全局）
/// </summary>
public class ColumnMappingRuleRepository : Repository<ColumnMappingRule>, IColumnMappingRuleRepository
{
    /// <summary>
    /// 创建 ColumnMappingRuleRepository 实例
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public ColumnMappingRuleRepository(AppDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 获取所有启用的规则（按目标字段、优先级倒序、ID 升序排序）。
    /// </summary>
    /// <returns>规则列表</returns>
    public async Task<IReadOnlyList<ColumnMappingRule>> GetEnabledOrderedAsync()
    {
        return await _dbSet
            .Where(r => r.Enabled)
            .OrderBy(r => r.TargetField)
            .ThenByDescending(r => r.Priority)
            .ThenBy(r => r.Id)
            .ToListAsync();
    }
}

