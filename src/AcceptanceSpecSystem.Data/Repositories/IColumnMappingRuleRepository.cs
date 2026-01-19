using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 导入列映射规则 Repository 接口（全局）
/// </summary>
public interface IColumnMappingRuleRepository : IRepository<ColumnMappingRule>
{
    /// <summary>
    /// 获取所有启用的规则（按目标字段与优先级排序）。
    /// </summary>
    /// <returns>规则列表</returns>
    Task<IReadOnlyList<ColumnMappingRule>> GetEnabledOrderedAsync();
}

