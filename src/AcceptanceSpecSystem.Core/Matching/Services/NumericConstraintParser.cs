using System.Globalization;
using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Core.Matching.Services;

internal sealed class NumericConstraintParser
{
    private static readonly Regex ConstraintRegex = new(
        @"(?<field>[\u4e00-\u9fffA-Za-z]{1,20})(?<operator>小于等于|不大于|<=|≤|小于|<|等于|=|大于等于|不小于|>=|≥|大于|>)(?<value>\d+(?:\.\d+)?)(?<unit>[\u4e00-\u9fffA-Za-z]+)",
        RegexOptions.Compiled);

    public ParsedConstraint? Parse(string? text, MatchingKnowledge knowledge)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = ConstraintRegex.Match(text);
        if (!match.Success)
            return null;

        var field = NormalizeFieldName(match.Groups["field"].Value.Trim(), knowledge);
        var op = NormalizeOperator(match.Groups["operator"].Value);
        var unit = NormalizeUnit(match.Groups["unit"].Value.Trim(), knowledge);
        if (string.IsNullOrWhiteSpace(unit))
            return null;
        var value = decimal.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
        var normalizedValue = NormalizeValue(value, unit, knowledge);

        return new ParsedConstraint(field, op, value, unit, normalizedValue, match.Value);
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

    private static decimal NormalizeValue(decimal value, string unit, MatchingKnowledge knowledge)
    {
        return knowledge.UnitFactors.TryGetValue(unit, out var factor)
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
