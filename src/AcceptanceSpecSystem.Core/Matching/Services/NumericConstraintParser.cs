using System.Globalization;
using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Core.Matching.Services;

internal sealed class NumericConstraintParser
{
    private static readonly Regex ConstraintRegex = new(
        @"(?<field>[\u4e00-\u9fffA-Za-z]{1,20})(?<operator>小于等于|不大于|<=|≤|小于|<|等于|=|大于等于|不小于|>=|≥|大于|>)(?<value>\d+(?:\.\d+)?)(?:\s*)(?<unit>kg\/cm[23]|kgf\/cm2|℃|°c|°C|度|[A-Za-zµμ]+(?:\/[A-Za-z]+\d?)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GenericMeasurementRegex = new(
        @"(?<value>\d+(?:\.\d+)?)(?:\s*)(?<unit>kg\/cm[23]|kgf\/cm2|℃|°c|°C|度|[A-Za-zµμ]+(?:\/[A-Za-z]+\d?)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Dictionary<string, decimal> InternalUnitFactors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["m"] = 1000m,
        ["mm"] = 1m,
        ["cm"] = 10m,
        ["um"] = 0.001m,
        ["nm"] = 0.000001m,
        ["degc"] = 1m,
        ["v"] = 1m,
        ["mv"] = 0.001m,
        ["kv"] = 1000m,
        ["a"] = 1m,
        ["ma"] = 0.001m,
        ["ua"] = 0.000001m,
        ["w"] = 1m,
        ["kw"] = 1000m,
        ["mw"] = 0.001m,
        ["hz"] = 1m,
        ["khz"] = 1000m,
        ["mhz"] = 1000000m,
        ["ghz"] = 1000000000m,
        ["kpa"] = 1m,
        ["mpa"] = 1000m,
        ["n"] = 1m,
        ["kn"] = 1000m,
        ["g"] = 1m,
        ["kg"] = 1000m,
        ["mg"] = 0.001m,
        ["s"] = 1m,
        ["ms"] = 0.001m,
        ["us"] = 0.000001m,
        ["ns"] = 0.000000001m,
        ["min"] = 60m,
        ["hr"] = 3600m,
        ["ohm"] = 1m,
        ["kohm"] = 1000m,
        ["mohm"] = 1000000m,
        ["kg/cm2"] = 1m,
        ["kg/cm3"] = 1m
    };
    private static readonly Dictionary<string, string> DefaultFieldByUnit = new(StringComparer.OrdinalIgnoreCase)
    {
        ["m"] = "长度",
        ["mm"] = "长度",
        ["cm"] = "长度",
        ["um"] = "长度",
        ["nm"] = "长度",
        ["degc"] = "温度",
        ["v"] = "电压",
        ["mv"] = "电压",
        ["kv"] = "电压",
        ["a"] = "电流",
        ["ma"] = "电流",
        ["ua"] = "电流",
        ["w"] = "功率",
        ["kw"] = "功率",
        ["mw"] = "功率",
        ["hz"] = "频率",
        ["khz"] = "频率",
        ["mhz"] = "频率",
        ["ghz"] = "频率",
        ["kpa"] = "压力",
        ["mpa"] = "压力",
        ["kg/cm2"] = "压力",
        ["kg/cm3"] = "压力",
        ["n"] = "压力",
        ["kn"] = "压力",
        ["g"] = "重量",
        ["kg"] = "重量",
        ["mg"] = "重量",
        ["s"] = "时间",
        ["ms"] = "时间",
        ["us"] = "时间",
        ["ns"] = "时间",
        ["min"] = "时间",
        ["hr"] = "时间",
        ["ohm"] = "阻值",
        ["kohm"] = "阻值",
        ["mohm"] = "阻值"
    };

    public ParsedConstraint? Parse(string? text, MatchingKnowledge knowledge)
    {
        return ParseAll(text, knowledge).FirstOrDefault();
    }

    public IReadOnlyList<ParsedConstraint> ParseAll(string? text, MatchingKnowledge knowledge)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var explicitMatches = ConstraintRegex.Matches(text);
        var constraints = new List<ParsedConstraint>(explicitMatches.Count);
        var occupiedRanges = new List<(int Start, int End)>(explicitMatches.Count);

        foreach (Match match in explicitMatches)
        {
            var field = NormalizeFieldName(match.Groups["field"].Value.Trim(), knowledge);
            var op = NormalizeOperator(match.Groups["operator"].Value);
            var unit = NormalizeUnit(match.Groups["unit"].Value.Trim(), knowledge);
            if (string.IsNullOrWhiteSpace(unit))
                continue;

            var value = decimal.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
            var normalizedValue = NormalizeValue(value, unit);
            constraints.Add(new ParsedConstraint(
                field,
                op,
                value,
                unit,
                normalizedValue,
                match.Value,
                $"{match.Groups["value"].Value}{match.Groups["unit"].Value.Trim()}"));
            occupiedRanges.Add((match.Index, match.Index + match.Length));
        }

        foreach (Match match in GenericMeasurementRegex.Matches(text))
        {
            var start = match.Index;
            var end = match.Index + match.Length;
            if (occupiedRanges.Any(range => start < range.End && end > range.Start))
                continue;

            var unit = NormalizeUnit(match.Groups["unit"].Value.Trim(), knowledge);
            if (string.IsNullOrWhiteSpace(unit))
                continue;

            var value = decimal.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
            var normalizedValue = NormalizeValue(value, unit);
            var field = InferFieldName(text, match.Index, unit, knowledge);

            constraints.Add(new ParsedConstraint(
                field,
                "=",
                value,
                unit,
                normalizedValue,
                match.Value,
                $"{match.Groups["value"].Value}{match.Groups["unit"].Value.Trim()}"));
        }

        return constraints;
    }

    private static string NormalizeOperator(string value)
    {
        return value switch
        {
            "小于等于" or "不大于" or "<=" or "≤" => "<=",
            "小于" or "<" => "<",
            "等于" or "=" => "=",
            "大于等于" or "不小于" or ">=" or "≥" => ">=",
            "大于" or ">" => ">",
            _ => value
        };
    }

    private static string NormalizeFieldName(string rawField, MatchingKnowledge knowledge)
    {
        if (string.IsNullOrWhiteSpace(rawField))
            return rawField;

        if (knowledge.FieldAliases.TryGetValue(rawField, out var canonicalField))
            return canonicalField;

        var matchedAlias = knowledge.FieldAliases.Keys
            .Where(alias => rawField.Contains(alias, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(alias => alias.Length)
            .FirstOrDefault();

        return matchedAlias != null && knowledge.FieldAliases.TryGetValue(matchedAlias, out canonicalField)
            ? canonicalField
            : rawField;
    }

    private static string NormalizeUnit(string rawUnit, MatchingKnowledge knowledge)
    {
        if (string.IsNullOrWhiteSpace(rawUnit))
            return string.Empty;

        var normalized = rawUnit.Trim();
        return knowledge.UnitAliases.TryGetValue(normalized, out var canonicalUnit)
            ? canonicalUnit
            : string.Empty;
    }

    private static string InferFieldName(string text, int matchIndex, string normalizedUnit, MatchingKnowledge knowledge)
    {
        var prefixStart = Math.Max(0, matchIndex - 16);
        var prefix = text[prefixStart..matchIndex];
        var matchedAlias = knowledge.FieldAliases.Keys
            .Where(alias => prefix.Contains(alias, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(alias => alias.Length)
            .FirstOrDefault();

        if (matchedAlias != null && knowledge.FieldAliases.TryGetValue(matchedAlias, out var fieldName))
            return fieldName;

        return DefaultFieldByUnit.TryGetValue(normalizedUnit, out fieldName)
            ? fieldName
            : normalizedUnit;
    }

    private static decimal NormalizeValue(decimal value, string unit)
    {
        return InternalUnitFactors.TryGetValue(unit, out var factor)
            ? value * factor
            : value;
    }
}

internal sealed record ParsedConstraint(
    string FieldName,
    string Operator,
    decimal RawValue,
    string Unit,
    decimal NormalizedValue,
    string Expression,
    string DisplayValue);
