using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Core.Matching.Services;

/// <summary>
/// 匹配决策使用的问题分类判断（硬冲突、量纲型硬冲突、阻断性警告、型号/料号冲突），从 Decisions 拆出以控制单文件体量。
/// </summary>
public partial class SemanticKernelMatchingService : IMatchingService
{
    /// <summary>
    /// 判断候选的问题列表中是否存在硬冲突（数值/单位、比较符、温度跨温标、方向/极性反义）。
    /// 硬冲突一律强制人工，无视 Embedding 高分。
    /// </summary>
    private static bool HasHardConflict(IReadOnlyList<MatchIssue>? issues)
    {
        return issues != null && issues.Any(issue =>
            string.Equals(issue.Severity, "hard_conflict", StringComparison.Ordinal));
    }

    /// <summary>
    /// 判断候选的问题列表中是否存在量纲型硬冲突（数值/单位、温度跨温标、尺寸元组）。
    /// 这类冲突由确定性规则检出、与 LLM 语义判断无关，语义优先模式下也不允许被 LLM Equivalent 覆盖，
    /// 避免把"数值差一个量级/单位不同/尺寸分配不同"的规格自动填错。比较符、方向/极性、否定语义
    /// 等"语义方向"类硬冲突不在此列，仍保留给语义优先模式交由 LLM 裁决。
    /// </summary>
    private static bool HasDimensionalHardConflict(IReadOnlyList<MatchIssue>? issues)
    {
        return issues != null && issues.Any(issue =>
            string.Equals(issue.Severity, "hard_conflict", StringComparison.Ordinal) &&
            (string.Equals(issue.Code, "numeric_unit_conflict", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(issue.Code, "cross_temperature_scale", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(issue.Code, "dimension_tuple_conflict", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool HasAutoApplyBlockingWarning(IReadOnlyList<MatchIssue>? issues)
    {
        return issues != null && issues.Any(issue =>
            string.Equals(issue.Severity, "warning", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(issue.Code, "unknown_unit_token", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(issue.Code, "unknown_brand_token", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(issue.Code, "unsupported_format_token", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// 判断候选的问题列表中是否存在型号/料号冲突。
    /// 此类行的 LLM Equivalent 结论需满足更高置信度门槛才可自动通过。
    /// </summary>
    private static bool HasIdentifierConflict(IReadOnlyList<MatchIssue>? issues)
    {
        return issues != null && issues.Any(issue =>
            string.Equals(issue.Code, "identifier_conflict", StringComparison.OrdinalIgnoreCase));
    }
}
