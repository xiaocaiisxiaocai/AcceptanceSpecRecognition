using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// Word 列映射规则仓储接口。
/// </summary>
public interface IColumnMappingRuleRepository : IRepository<ColumnMappingRule>
{
    /// <summary>
    /// 获取启用中的规则，按目标字段和优先级排序。
    /// </summary>
    Task<IReadOnlyList<ColumnMappingRule>> GetEnabledOrderedAsync();
}
