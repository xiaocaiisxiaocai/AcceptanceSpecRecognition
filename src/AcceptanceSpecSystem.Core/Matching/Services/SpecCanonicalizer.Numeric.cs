using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.Matching.Interfaces;


namespace AcceptanceSpecSystem.Core.Matching.Services;

public sealed partial class SpecCanonicalizer
{
    private string NormalizeNumericUnits(string text)
    {
        // 替换所有能归一的数值+单位为 "基准值基准单位" 形式
        return _numericUnitRegex.Replace(text, match =>
        {
            var numStr = match.Groups["num"].Value.Replace(",", ".");
            var unit = match.Groups["unit"].Value;

            if (!double.TryParse(numStr,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var num))
                return match.Value;

            if (!TryNormalizeToBaseUnit(num, unit, out var baseVal, out var dim))
                return match.Value;

            return $"{FormatNumber(baseVal)}[{dim}]";
        });
    }

    /// <summary>
    /// 区间归一：把公差型(A±B)与范围型(A~B / A到B / A至B / A-B)统一为同一通带 token
    /// 「lo~hi[dim]」，使等价区间（如 10±2 与 8到12）在规范化后变成精确命中。
    /// 端点各自按自身单位归一到基准量纲，仅当两端量纲一致（或皆无单位）时才输出区间 token，
    /// 否则保留原文，交由冲突扫描器处理。
    /// </summary>
    private string NormalizeIntervals(string text)
    {
        // 1. 公差型 A±B[unit]
        text = _toleranceIntervalRegex.Replace(text, match =>
        {
            if (!double.TryParse(match.Groups["center"].Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var center) ||
                !double.TryParse(match.Groups["tol"].Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var tol))
            {
                return match.Value;
            }

            var unit = match.Groups["unit"].Value;
            return BuildIntervalToken(center - tol, unit, center + tol, unit) ?? match.Value;
        });

        // 2. 范围型 A[u1](~|到|至)B[u2]
        text = _rangeIntervalRegex.Replace(text, match =>
        {
            if (!double.TryParse(match.Groups["lo"].Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var lo) ||
                !double.TryParse(match.Groups["hi"].Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var hi))
            {
                return match.Value;
            }

            var u1 = match.Groups["u1"].Value;
            var u2 = match.Groups["u2"].Value;
            // 缺失的一端单位用对端补（"8到12mm" → 两端都按 mm）
            var loUnit = string.IsNullOrEmpty(u1) ? u2 : u1;
            var hiUnit = string.IsNullOrEmpty(u2) ? u1 : u2;
            return BuildIntervalToken(lo, loUnit, hi, hiUnit) ?? match.Value;
        });

        // 3. 连字符型 A-B[unit]（保守：正则已限定两数非负，再要求 lo<hi 才视为区间）
        text = _hyphenRangeIntervalRegex.Replace(text, match =>
        {
            if (!double.TryParse(match.Groups["lo"].Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var lo) ||
                !double.TryParse(match.Groups["hi"].Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var hi))
            {
                return match.Value;
            }

            if (lo >= hi)
            {
                // 非递增：可能是负号或减法，保守保留原文
                return match.Value;
            }

            var unit = match.Groups["unit"].Value;
            return BuildIntervalToken(lo, unit, hi, unit) ?? match.Value;
        });

        return text;
    }

    /// <summary>
    /// 构建区间 token。两端各自按单位归一到基准量纲，量纲一致（或皆无单位）才返回 token，
    /// 否则返回 null（由调用方保留原文）。输出形如 "8~12[length]" 或无单位 "8~12"。
    /// </summary>
    private string? BuildIntervalToken(double lo, string loUnit, double hi, string hiUnit)
    {
        // 归一单个端点：无单位返回 (原值, "")；有单位且能归一返回 (基准值, 量纲)；无法归一返回 null
        (double Value, string Dim)? NormalizeEndpoint(double value, string unit)
        {
            if (string.IsNullOrEmpty(unit))
            {
                return (value, string.Empty);
            }

            return TryNormalizeToBaseUnit(value, unit, out var baseValue, out var dim)
                ? (baseValue, dim)
                : null;
        }

        var loResult = NormalizeEndpoint(lo, loUnit);
        var hiResult = NormalizeEndpoint(hi, hiUnit);
        if (loResult == null || hiResult == null)
        {
            return null;
        }

        // 两端量纲必须一致（包括"皆无单位"，此时 Dim 均为 ""）。
        // 一端有单位一端没有时 Dim 不相等，自然被拦截，保守保留原文。
        if (!string.Equals(loResult.Value.Dim, hiResult.Value.Dim, StringComparison.Ordinal))
        {
            return null;
        }

        var dimSuffix = string.IsNullOrEmpty(loResult.Value.Dim) ? string.Empty : $"[{loResult.Value.Dim}]";
        return $"{FormatNumber(loResult.Value.Value)}~{FormatNumber(hiResult.Value.Value)}{dimSuffix}";
    }

    /// <summary>
    /// 数值格式化：整数不带小数点（避免 1000.0 vs 1000 不等），其余用 G6 有效数字。
    /// </summary>
    private static string FormatNumber(double value)
    {
        return value == Math.Floor(value) && !double.IsInfinity(value)
            ? ((long)value).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
    }

}
