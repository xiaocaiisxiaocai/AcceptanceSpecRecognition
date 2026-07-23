namespace AcceptanceSpecSystem.Core.Documents.Intelligence.Models;

/// <summary>
/// 运行时列头映射规则。
/// </summary>
public sealed record ColumnHeaderMappingRule(
    ColumnType ColumnType,
    ColumnHeaderMatchMode MatchMode,
    string Pattern,
    int Priority = 0,
    bool IsCustomerSpecific = false);

/// <summary>
/// 列头规则匹配模式。
/// </summary>
public enum ColumnHeaderMatchMode
{
    Contains = 1,
    Equals = 2,
    Regex = 3
}
