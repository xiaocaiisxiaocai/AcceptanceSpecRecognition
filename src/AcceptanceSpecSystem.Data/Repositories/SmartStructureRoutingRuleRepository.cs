using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 智能结构识别表格路由规则仓储实现。
/// </summary>
public class SmartStructureRoutingRuleRepository : Repository<SmartStructureRoutingRule>, ISmartStructureRoutingRuleRepository
{
    public SmartStructureRoutingRuleRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<SmartStructureRoutingRule>> GetEffectiveForCustomerAsync(
        int? customerId,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(rule => rule.Enabled)
            .Where(rule => rule.CustomerId == null || rule.CustomerId == customerId)
            .OrderByDescending(rule => customerId.HasValue && rule.CustomerId == customerId.Value)
            .ThenByDescending(rule => rule.Priority)
            .ThenByDescending(rule => rule.Weight)
            .ThenBy(rule => rule.Id)
            .ToListAsync(cancellationToken);
    }
}
