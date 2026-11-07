using System.Text.RegularExpressions;

namespace AcceptanceSpecSystem.Core.Matching.Services;

/// <summary>
/// 数值等价比较的工程容差规则（冲突扫描器与近似规范化层共用）。
/// 容差仅用于吸收"跨单位换算"带来的取整误差（如 1in 写成 25.42mm、100psi 写成 689kPa）；
/// 两侧原文单位完全相同、未发生换算时必须数值一致，否则"25.000mm vs 25.020mm"这类
/// 工程上有意义的差异会被 0.1% 容差吞掉后确定性放行。
/// </summary>
internal static class NumericToleranceRules
{
    /// <summary>
    /// 跨单位换算时允许的相对误差。
    /// </summary>
    public const double ConvertedUnitToleranceRatio = 1e-3;

    /// <summary>
    /// 同单位比较时允许的相对误差，只吸收浮点运算误差。
    /// </summary>
    public const double SameUnitToleranceRatio = 1e-9;

    private static readonly Regex LeadingNumberRegex = new(
        @"^[\s+\-]*\d+(?:[.,]\d+)?(?:e[+-]?\d+)?\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 判断两个归一值是否超出工程容差。
    /// 原始表达式的单位相同（区分大小写）时按同单位严格比较，否则按换算容差比较。
    /// </summary>
    public static bool IsOutsideTolerance(
        double left,
        double right,
        string? leftExpression,
        string? rightExpression)
    {
        var ratio = IsSameUnit(leftExpression, rightExpression)
            ? SameUnitToleranceRatio
            : ConvertedUnitToleranceRatio;
        return IsOutsideTolerance(left, right, ratio);
    }

    /// <summary>
    /// 按指定相对误差判断两个数值是否超出容差。
    /// </summary>
    public static bool IsOutsideTolerance(double left, double right, double ratio)
    {
        if (left == 0 && right == 0)
            return false;

        var maxAbs = Math.Max(Math.Abs(left), Math.Abs(right));
        return Math.Abs(left - right) / maxAbs > ratio;
    }

    private static bool IsSameUnit(string? leftExpression, string? rightExpression)
    {
        var leftUnit = ExtractUnit(leftExpression);
        var rightUnit = ExtractUnit(rightExpression);
        return leftUnit.Length > 0 && string.Equals(leftUnit, rightUnit, StringComparison.Ordinal);
    }

    private static string ExtractUnit(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return string.Empty;

        return LeadingNumberRegex.Replace(expression.Trim(), string.Empty).Trim();
    }
}
