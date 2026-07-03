using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.Matching.Interfaces;

namespace AcceptanceSpecSystem.Core.Matching.Services;

/// <summary>
/// 规格文本规范化器。
/// 通过 SI 前缀引擎、品牌字典、同义表达字典消除等价差异，
/// 使"7.5kW vs 7500W"、"松下 vs Panasonic"等规范化后变成精确命中。
/// </summary>
public sealed partial class SpecCanonicalizer : ISpecCanonicalizer
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    // 移除汉字相邻处的空格：Word/Excel 复制的规格常在汉字间夹换行/空格，
    // "气缸 上升" 与 "气缸上升" 必须归一为同一文本，否则规范化精确匹配会漏。
    // 仅当空格至少一侧是汉字时移除，纯英文词间空格（如 "max load"）保留。
    private static readonly Regex CjkAdjacentSpaceRegex = new(
        @"(?<=[一-鿿])\s+|\s+(?=[一-鿿])",
        RegexOptions.Compiled);

    private readonly IReadOnlyDictionary<string, (string Dimension, double Factor)> _unitRoots;
    private readonly IReadOnlyDictionary<string, string> _brandNormMap;
    private readonly IReadOnlyList<string> _brandAdjacentDeviceWords;
    private readonly Regex _brandNormRegex;
    private readonly Regex _brandDeviceSpacingRegex;
    private readonly Regex _numericUnitRegex;
    private readonly Regex _unknownCompoundUnitRegex;
    private readonly Regex _toleranceIntervalRegex;
    private readonly Regex _rangeIntervalRegex;
    private readonly Regex _hyphenRangeIntervalRegex;

    public SpecCanonicalizer()
        : this(LoadDefaultExternalKnowledge())
    {
    }

    public SpecCanonicalizer(string? externalKnowledgePath)
        : this(LoadExternalKnowledge(externalKnowledgePath))
    {
    }

    private SpecCanonicalizer(ExternalMatchingKnowledge? externalKnowledge)
    {
        _unitRoots = BuildUnitRoots(externalKnowledge);
        _brandNormMap = BuildBrandNormMap(externalKnowledge);
        _brandAdjacentDeviceWords = BuildBrandAdjacentDeviceWords(externalKnowledge);

        var brandAlternation = BuildRegexAlternation(_brandNormMap.Keys);
        var brandDeviceAlternation = BuildRegexAlternation(_brandAdjacentDeviceWords);
        var brandValueAlternation = BuildRegexAlternation(_brandNormMap.Values);
        _brandNormRegex = new Regex(
            $@"(?<![A-Za-z0-9一-鿿])(?<brand>{brandAlternation})(?=(?![A-Za-z0-9一-鿿])|(?:{brandDeviceAlternation}))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        _brandDeviceSpacingRegex = new Regex(
            $@"(?<brand>{brandValueAlternation})(?<space>\s+)(?<device>{brandDeviceAlternation})(?![A-Za-z0-9一-鿿])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var numericUnitTokenPattern =
            $@"(?:{BuildRegexAlternation(_unitRoots.Keys.Concat(["%"]))}|[A-Za-z][A-Za-z0-9]*(?:/[A-Za-z0-9一-鿿]+)?|[一-鿿]{{1,4}}(?:/[A-Za-z0-9一-鿿]+)?)";

        _numericUnitRegex = new Regex(
            $@"(?<![A-Za-z0-9])(?<num>-?\d+(?:[.,]\d+)?(?:[eE][+-]?\d+)?)\s*(?<unit>{numericUnitTokenPattern})",
            RegexOptions.Compiled);
        _unknownCompoundUnitRegex = new Regex(
            $@"(?<![A-Za-z0-9])(?<expr>(?<num>-?\d+(?:[.,]\d+)?(?:[eE][+-]?\d+)?)\s*(?<known>{BuildRegexAlternation(_unitRoots.Keys)})/(?<unknown>[一-鿿A-Za-z][A-Za-z0-9一-鿿]*))(?![A-Za-z0-9一-鿿])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        _toleranceIntervalRegex = new Regex(
            $@"(?<![A-Za-z0-9])(?<center>-?\d+(?:\.\d+)?)\s*±\s*(?<tol>\d+(?:\.\d+)?)\s*(?<unit>{numericUnitTokenPattern})?",
            RegexOptions.Compiled);
        _rangeIntervalRegex = new Regex(
            $@"(?<![A-Za-z0-9])(?<lo>-?\d+(?:\.\d+)?)\s*(?<u1>{numericUnitTokenPattern})?\s*(?:~|到|至)\s*(?<hi>-?\d+(?:\.\d+)?)\s*(?<u2>{numericUnitTokenPattern})?",
            RegexOptions.Compiled);
        _hyphenRangeIntervalRegex = new Regex(
            $@"(?<![\d.~±A-Za-z-])(?<lo>\d+(?:\.\d+)?)\s*-\s*(?<hi>\d+(?:\.\d+)?)\s*(?<unit>{numericUnitTokenPattern})?",
            RegexOptions.Compiled);
    }

    public string Canonicalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var result = text
            .Replace(" ", " ")
            .Replace("​", string.Empty)
            .Replace("﻿", string.Empty)
            .Trim();

        // 全角数字/字母转半角
        result = FullWidthToHalfWidth(result);

        // 同义词替换（比较符 / 语气词 / 温度符号）
        result = ApplySynonymMap(result);

        // 品牌归一
        result = ApplyBrandNorm(result);
        result = NormalizeBrandDeviceSpacing(result);

        // 区间归一（必须在数值单位归一之前，否则单值会先被替换破坏区间识别）
        // 公差型 10±2 与范围型 8~12/8到12 统一为同一通带 token，使等价区间变成精确命中。
        result = NormalizeIntervals(result);

        // 数值+单位归一
        result = NormalizeNumericUnits(result);

        // 空白归一
        result = WhitespaceRegex.Replace(result, " ").Trim().ToLowerInvariant();

        // 移除汉字相邻处的空格（换行/分词差异归一）
        result = CjkAdjacentSpaceRegex.Replace(result, string.Empty);

        return result;
    }

    public bool TryNormalizeToBaseUnit(
        double value,
        string unitToken,
        out double baseValue,
        out string baseDimension)
    {
        baseValue = value;
        baseDimension = string.Empty;

        if (string.IsNullOrWhiteSpace(unitToken))
            return false;

        var unit = unitToken.Trim();

        // 直接查词根表（优先全词，处理 ms/us 等避免误前缀分解）
        if (_unitRoots.TryGetValue(unit, out var direct))
        {
            if (double.IsNaN(direct.Factor))
                return false; // 跨温标等不支持自动换算的

            baseValue = value * direct.Factor;
            baseDimension = direct.Dimension;
            return true;
        }

        // SI 前缀分解：尝试从最长前缀开始匹配
        foreach (var (prefix, prefixFactor) in SiPrefixFactors.OrderByDescending(p => p.Key.Length))
        {
            if (!unit.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var root = unit[prefix.Length..];
            if (!_unitRoots.TryGetValue(root, out var rootEntry))
                continue;

            if (double.IsNaN(rootEntry.Factor))
                return false;

            baseValue = value * prefixFactor * rootEntry.Factor;
            baseDimension = rootEntry.Dimension;
            return true;
        }

        return false;
    }

    public IReadOnlyList<(double BaseValue, string Dimension, string OriginalExpression)>
        ExtractNormalizedValues(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var results = new List<(double, string, string)>();
        foreach (Match match in _numericUnitRegex.Matches(text))
        {
            var numStr = match.Groups["num"].Value.Replace(",", ".");
            var unit = match.Groups["unit"].Value;

            if (!double.TryParse(numStr,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var num))
                continue;

            if (!TryNormalizeToBaseUnit(num, unit, out var baseVal, out var dim))
                continue;

            results.Add((baseVal, dim, match.Value));
        }

        return results;
    }

    public IReadOnlyList<(string UnitToken, string OriginalExpression)> ExtractUnknownUnitTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var results = new List<(string, string)>();
        foreach (Match match in _unknownCompoundUnitRegex.Matches(text))
        {
            var known = match.Groups["known"].Value;
            if (!TryNormalizeToBaseUnit(1, known, out _, out var knownDim) ||
                !string.Equals(knownDim, "time", StringComparison.Ordinal))
            {
                continue;
            }

            var unit = $"{match.Groups["known"].Value}/{match.Groups["unknown"].Value}";
            if (IsKnownCompoundDenominator(match.Groups["unknown"].Value))
                continue;

            if (TryNormalizeToBaseUnit(1, unit, out _, out _))
                continue;

            results.Add((unit, match.Groups["expr"].Value));
        }

        foreach (Match match in _numericUnitRegex.Matches(text))
        {
            var unit = match.Groups["unit"].Value;
            if (IsUnknownCjkUnitSeparatedByWhitespace(match))
                continue;

            if (IsKnownPlainSuffix(unit))
                continue;

            if (TryNormalizeToBaseUnit(1, unit, out _, out _))
                continue;

            // 经过上面的归一尝试仍无法识别，且 token 是纯汉字（无字母/数字）构成的多字词，
            // 判定为名词词组而非量纲单位（如"边吸取""米高""分钟可调"），跳过以避免误报。
            // 真正的中文单位（米/秒/毫米等）已在上面的 TryNormalizeToBaseUnit 命中并 continue。
            if (IsCjkNounPhraseUnit(unit))
                continue;

            results.Add((unit, match.Value));
        }

        return results
            .DistinctBy(item => $"{item.Item1}\u001F{item.Item2}", StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ── 私有辅助 ──────────────────────────────────────────────────

}
