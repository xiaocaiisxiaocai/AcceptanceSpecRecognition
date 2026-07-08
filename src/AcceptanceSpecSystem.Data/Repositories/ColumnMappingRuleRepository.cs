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
        var rules = await _dbSet
            .Where(rule => rule.Enabled)
            .Where(rule => rule.CustomerId == null)
            .OrderBy(rule => rule.TargetField)
            .ThenByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.Id)
            .ToListAsync();

        return ColumnMappingRuleDeduplicator.ForConfigurationList(rules);
    }

    public async Task<IReadOnlyList<ColumnMappingRule>> GetEffectiveForCustomerAsync(int? customerId)
    {
        var rules = await _dbSet
            .Where(rule => rule.Enabled)
            .Where(rule => rule.CustomerId == null || rule.CustomerId == customerId)
            .OrderBy(rule => rule.TargetField)
            .ThenByDescending(rule => customerId.HasValue && rule.CustomerId == customerId.Value)
            .ThenByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.Id)
            .ToListAsync();

        return ColumnMappingRuleDeduplicator.ForEffectiveRules(rules, customerId);
    }
}
