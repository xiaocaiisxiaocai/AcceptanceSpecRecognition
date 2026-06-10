using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// Word 列映射规则仓储实现。
/// </summary>
public class ColumnMappingRuleRepository : Repository<ColumnMappingRule>, IColumnMappingRuleRepository
{
    public ColumnMappingRuleRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<ColumnMappingRule>> GetEnabledOrderedAsync()
    {
        return await _dbSet
            .Where(rule => rule.Enabled)
            .OrderBy(rule => rule.TargetField)
            .ThenByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.Id)
            .ToListAsync();
    }
}
