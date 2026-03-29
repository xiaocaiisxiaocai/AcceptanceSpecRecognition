using System.Globalization;
using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Core.Matching.Services;

internal sealed class NumericConstraintParser
{
    private static readonly Regex ConstraintRegex = new(
        @"(?<field>[\u4e00-\u9fffA-Za-z]{1,20})(?<operator>小于等于|不大于|<=|≤|小于|<|等于|=|大于等于|不小于|>=|≥|大于|>)(?<value>\d+(?:\.\d+)?)(?<unit>[\u4e00-\u9fffA-Za-zµμ]+)",
        RegexOptions.Compiled);
    private static readonly Dictionary<string, decimal> InternalUnitFactors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["m"] = 1000m,
        ["mm"] = 1m,
        ["cm"] = 10m,
        ["um"] = 0.001m,
        ["nm"] = 0.000001m,
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
        ["mohm"] = 1000000m
    };

    public ParsedConstraint? Parse(string? text, MatchingKnowledge knowledge)
    {
        return ParseAll(text, knowledge).FirstOrDefault();
    }

    public IReadOnlyList<ParsedConstraint> ParseAll(string? text, MatchingKnowledge knowledge)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var matches = ConstraintRegex.Matches(text);
        if (matches.Count == 0)
            return [];

        var constraints = new List<ParsedConstraint>(matches.Count);
        foreach (Match match in matches)
        {
            var field = NormalizeFieldName(match.Groups["field"].Value.Trim(), knowledge);
            var op = NormalizeOperator(match.Groups["operator"].Value);
            var unit = NormalizeUnit(match.Groups["unit"].Value.Trim(), knowledge);
            if (string.IsNullOrWhiteSpace(unit))
                continue;

            var value = decimal.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
            var normalizedValue = NormalizeValue(value, unit);
            constraints.Add(new ParsedConstraint(field, op, value, unit, normalizedValue, match.Value));
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

        return knowledge.UnitAliases.TryGetValue(rawUnit, out var canonicalUnit)
            ? canonicalUnit
            : string.Empty;
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
    string Expression);
