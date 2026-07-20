namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 列映射规则去重策略。历史库可能存在手动全局词与内置词同名，读取时统一折叠。
/// </summary>
public static class ColumnMappingRuleDeduplicator
{
    public static IReadOnlyList<ColumnMappingRule> ForConfigurationList(IEnumerable<ColumnMappingRule> rules)
    {
        return rules
            .GroupBy(rule => (
                rule.CustomerId,
                rule.TargetField,
                Pattern: NormalizePattern(rule.Pattern)))
            .Select(group => PickBestRule(group, preferCustomerId: null))
            .OrderBy(rule => rule.TargetField)
            .ThenByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.Id)
            .ToList();
    }

    public static IReadOnlyList<ColumnMappingRule> ForEffectiveRules(
        IEnumerable<ColumnMappingRule> rules,
        int? customerId)
    {
        return rules
            .GroupBy(rule => (
                rule.TargetField,
                Pattern: NormalizePattern(rule.Pattern)))
            .Select(group => PickBestRule(group, customerId))
            .OrderBy(rule => rule.TargetField)
            .ThenByDescending(rule => customerId.HasValue && rule.CustomerId == customerId.Value)
            .ThenByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.Id)
            .ToList();
    }

    private static ColumnMappingRule PickBestRule(
        IEnumerable<ColumnMappingRule> rules,
        int? preferCustomerId)
    {
        return rules
            .OrderByDescending(rule => preferCustomerId.HasValue && rule.CustomerId == preferCustomerId.Value)
            .ThenByDescending(rule => rule.Enabled)
            .ThenByDescending(rule => SourceRank(rule.Source))
            .ThenByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.Id)
            .First();
    }

    private static int SourceRank(ColumnMappingRuleSource source) => source switch
    {
        ColumnMappingRuleSource.Manual => 3,
        ColumnMappingRuleSource.Learned => 2,
        ColumnMappingRuleSource.Builtin => 1,
        _ => 0
    };

    private static string NormalizePattern(string? pattern) => ColumnMappingRule.NormalizePattern(pattern);
}
