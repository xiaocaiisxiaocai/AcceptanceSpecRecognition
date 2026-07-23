using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 智能结构识别表格路由规则仓储接口。
/// </summary>
public interface ISmartStructureRoutingRuleRepository : IRepository<SmartStructureRoutingRule>
{
    Task<IReadOnlyList<SmartStructureRoutingRule>> GetEffectiveForCustomerAsync(
        int? customerId,
        CancellationToken cancellationToken = default);
}
