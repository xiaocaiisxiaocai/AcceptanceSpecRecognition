using System.Globalization;
using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Core.Matching.Services;

/// <summary>
/// 构建源项与候选项之间的结构化证据
/// </summary>
public sealed class MatchEvidenceBuilder : IMatchEvidenceBuilder
{
    private static readonly Regex IdentifierRegex = new(
        @"\b[A-Z]{2,}(?:-[A-Z0-9]+)+\b",
        RegexOptions.Compiled);

    private readonly NumericConstraintParser _numericParser = new();
    private readonly EntityAliasNormalizer _entityNormalizer = new();

    public MatchEvidence Build(MatchSource source, MatchCandidate candidate, MatchingKnowledge knowledge)
    {
        var evidence = new MatchEvidence();
        BuildNumericEvidence(evidence, source, candidate, knowledge);
        BuildEntityEvidence(evidence, source, candidate, knowledge);
        BuildIdentifierEvidence(evidence, source, candidate);

        return evidence;
    }

    private void BuildNumericEvidence(MatchEvidence evidence, MatchSource source, MatchCandidate candidate, MatchingKnowledge knowledge)
    {
        var sourceConstraints = _numericParser.ParseAll($"{source.Project} {source.Specification}", knowledge);
        var candidateConstraints = _numericParser.ParseAll($"{candidate.Project} {candidate.Specification}", knowledge);
        if (sourceConstraints.Count == 0 || candidateConstraints.Count == 0)
            return;

        var candidateLookup = candidateConstraints
            .GroupBy(item => item.FieldName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToList(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var sourceGroup in sourceConstraints.GroupBy(item => item.FieldName, StringComparer.OrdinalIgnoreCase))
        {
            if (!candidateLookup.TryGetValue(sourceGroup.Key, out var candidatesByField) ||
                candidatesByField.Count == 0)
            {
                continue;
            }

            var sourceItems = sourceGroup.ToList();
            var relation = AggregateConstraintRelation(sourceItems, candidatesByField);
            var sourceExpression = string.Join("；", sourceItems.Select(item => item.Expression).Distinct(StringComparer.OrdinalIgnoreCase));
            var candidateExpression = string.Join("；", candidatesByField.Select(item => item.Expression).Distinct(StringComparer.OrdinalIgnoreCase));

            evidence.NumericConstraints.Add(new NumericConstraintEvidence
            {
                FieldName = sourceGroup.Key,
                SourceExpression = sourceExpression,
                CandidateExpression = candidateExpression,
                Relation = relation
            });

            if (relation == EvidenceRelation.Conflict)
            {
                evidence.HasHardConflict = true;
                evidence.Conflicts.Add($"数值约束冲突：{sourceExpression} vs {candidateExpression}");
                evidence.Issues.Add(CreateNumericIssue(
                    sourceGroup.Key,
                    sourceItems,
                    candidatesByField,
                    severity: "high",
                    code: "numeric_value_conflict",
                    message: BuildNumericConflictMessage(sourceGroup.Key, sourceItems, candidatesByField),
                    suggestedAction: $"请人工确认{sourceGroup.Key}参数"));
            }
            else if (relation == EvidenceRelation.Overlap)
            {
                evidence.Warnings.Add($"数值约束存在重叠但不够精确：{sourceGroup.Key}");
                evidence.Summary.Add($"数值约束存在重叠：{sourceGroup.Key}");
                evidence.Issues.Add(CreateNumericIssue(
                    sourceGroup.Key,
                    sourceItems,
                    candidatesByField,
                    severity: "warning",
                    code: "evidence_insufficient",
                    message: BuildNumericOverlapMessage(sourceGroup.Key, sourceItems, candidatesByField),
                    suggestedAction: $"请人工确认{sourceGroup.Key}参数，避免错误自动带入"));
            }
            else
            {
                evidence.Summary.Add($"数值约束{(relation == EvidenceRelation.Compatible ? "相容" : "一致")}：{sourceGroup.Key}");
            }
        }
    }

    private void BuildEntityEvidence(MatchEvidence evidence, MatchSource source, MatchCandidate candidate, MatchingKnowledge knowledge)
    {
        var sourceEntity = _entityNormalizer.Extract($"{source.Project} {source.Specification}", knowledge);
        var candidateEntity = _entityNormalizer.Extract($"{candidate.Project} {candidate.Specification}", knowledge);
        if (sourceEntity == null || candidateEntity == null)
            return;

        var relation = sourceEntity.Value.Normalized.Equals(candidateEntity.Value.Normalized, StringComparison.OrdinalIgnoreCase)
            ? sourceEntity.Value.Raw.Equals(candidateEntity.Value.Raw, StringComparison.OrdinalIgnoreCase)
                ? EvidenceRelation.Exact
                : EvidenceRelation.AliasSame
            : EvidenceRelation.Conflict;

        evidence.Entities.Add(new EntityEvidence
        {
            SourceValue = sourceEntity.Value.Raw,
            CandidateValue = candidateEntity.Value.Raw,
            NormalizedSourceValue = sourceEntity.Value.Normalized,
            NormalizedCandidateValue = candidateEntity.Value.Normalized,
            Relation = relation
        });

        if (relation == EvidenceRelation.Conflict)
        {
            evidence.HasHardConflict = true;
            evidence.Conflicts.Add($"实体冲突：{sourceEntity.Value.Raw} vs {candidateEntity.Value.Raw}");
            evidence.Issues.Add(new MatchIssue
            {
                Code = "entity_conflict",
                Severity = "high",
                FieldName = "实体",
                SourceValue = sourceEntity.Value.Raw,
                CandidateValue = candidateEntity.Value.Raw,
                Message = BuildEntityConflictMessage(sourceEntity.Value.Raw, candidateEntity.Value.Raw),
                SuggestedAction = "请人工确认品牌或组织实体，避免带入错误对象"
            });
            return;
        }

        evidence.Summary.Add($"实体同一：{sourceEntity.Value.Normalized}");
    }

    private void BuildIdentifierEvidence(MatchEvidence evidence, MatchSource source, MatchCandidate candidate)
    {
        var sourceIdentifiers = ExtractIdentifiers($"{source.Project} {source.Specification}");
        var candidateIdentifiers = ExtractIdentifiers($"{candidate.Project} {candidate.Specification}");
        if (sourceIdentifiers.Count == 0 || candidateIdentifiers.Count == 0)
            return;

        var remainingCandidates = new List<string>(candidateIdentifiers);
        foreach (var sourceIdentifier in sourceIdentifiers)
        {
            var exactIndex = remainingCandidates.FindIndex(candidateIdentifier =>
                candidateIdentifier.Equals(sourceIdentifier, StringComparison.OrdinalIgnoreCase));
            if (exactIndex >= 0)
            {
                var candidateIdentifier = remainingCandidates[exactIndex];
                remainingCandidates.RemoveAt(exactIndex);
                evidence.Identifiers.Add(new IdentifierEvidence
                {
                    SourceValue = sourceIdentifier,
                    CandidateValue = candidateIdentifier,
                    Relation = EvidenceRelation.Exact
                });
                evidence.Summary.Add($"型号一致：{sourceIdentifier}");
                continue;
            }

            var familyIndex = remainingCandidates.FindIndex(candidateIdentifier =>
                BelongsToSameIdentifierFamily(sourceIdentifier, candidateIdentifier));
            if (familyIndex < 0)
                continue;

            var conflictingCandidate = remainingCandidates[familyIndex];
            remainingCandidates.RemoveAt(familyIndex);

            evidence.Identifiers.Add(new IdentifierEvidence
            {
                SourceValue = sourceIdentifier,
                CandidateValue = conflictingCandidate,
                Relation = EvidenceRelation.Conflict
            });
            evidence.HasHardConflict = true;
            evidence.Conflicts.Add($"型号冲突：{sourceIdentifier} vs {conflictingCandidate}");
            evidence.Issues.Add(new MatchIssue
            {
                Code = "identifier_conflict",
                Severity = "high",
                FieldName = "型号",
                SourceValue = sourceIdentifier,
                CandidateValue = conflictingCandidate,
                Message = BuildIdentifierConflictMessage(sourceIdentifier, conflictingCandidate),
                SuggestedAction = "请人工确认型号/料号，避免使用错误物料"
            });
        }

        if (evidence.Identifiers.Count == 0 && sourceIdentifiers.Count == 1 && candidateIdentifiers.Count == 1)
        {
            evidence.Identifiers.Add(new IdentifierEvidence
            {
                SourceValue = sourceIdentifiers[0],
                CandidateValue = candidateIdentifiers[0],
                Relation = EvidenceRelation.Conflict
            });
            evidence.HasHardConflict = true;
            evidence.Conflicts.Add($"型号冲突：{sourceIdentifiers[0]} vs {candidateIdentifiers[0]}");
            evidence.Issues.Add(new MatchIssue
            {
                Code = "identifier_conflict",
                Severity = "high",
                FieldName = "型号",
                SourceValue = sourceIdentifiers[0],
                CandidateValue = candidateIdentifiers[0],
                Message = BuildIdentifierConflictMessage(sourceIdentifiers[0], candidateIdentifiers[0]),
                SuggestedAction = "请人工确认型号/料号，避免使用错误物料"
            });
        }
    }

    private static MatchIssue CreateNumericIssue(
        string fieldName,
        IReadOnlyList<ParsedConstraint> sourceItems,
        IReadOnlyList<ParsedConstraint> candidateItems,
        string severity,
        string code,
        string message,
        string suggestedAction)
    {
        var sourceItem = sourceItems.FirstOrDefault();
        var candidateItem = candidateItems.FirstOrDefault();

        return new MatchIssue
        {
            Code = code,
            Severity = severity,
            FieldName = fieldName,
            SourceValue = sourceItem?.DisplayValue,
            CandidateValue = candidateItem?.DisplayValue,
            Message = message,
            SuggestedAction = suggestedAction
        };
    }

    private static string BuildNumericConflictMessage(
        string fieldName,
        IReadOnlyList<ParsedConstraint> sourceItems,
        IReadOnlyList<ParsedConstraint> candidateItems)
    {
        var sourceItem = sourceItems.FirstOrDefault();
        var candidateItem = candidateItems.FirstOrDefault();
        if (sourceItem == null || candidateItem == null)
        {
            return $"{fieldName}数值不一致，无法自动采用";
        }

        if (TryGetDecimalMagnitudeRatio(sourceItem, candidateItem, out var ratio))
        {
            return $"{fieldName}值不一致：源项为 {sourceItem.DisplayValue}，候选为 {candidateItem.DisplayValue}，疑似小数点错位或数量级错误（相差{ratio.ToString("0.###", CultureInfo.InvariantCulture)}倍）";
        }

        return $"{fieldName}值不一致：源项为 {sourceItem.DisplayValue}，候选为 {candidateItem.DisplayValue}，无法自动采用";
    }

    private static string BuildNumericOverlapMessage(
        string fieldName,
        IReadOnlyList<ParsedConstraint> sourceItems,
        IReadOnlyList<ParsedConstraint> candidateItems)
    {
        var sourceItem = sourceItems.FirstOrDefault();
        var candidateItem = candidateItems.FirstOrDefault();
        if (sourceItem == null || candidateItem == null)
        {
            return $"{fieldName}约束仅部分重叠，证据不足，需要人工确认";
        }

        return $"{fieldName}约束仅部分重叠：源项为 {sourceItem.Expression}，候选为 {candidateItem.Expression}，需要人工确认";
    }

    private static string BuildEntityConflictMessage(string sourceValue, string candidateValue)
    {
        return $"品牌/实体不一致：源项为 {sourceValue}，候选为 {candidateValue}，无法自动采用";
    }

    private static string BuildIdentifierConflictMessage(string sourceValue, string candidateValue)
    {
        return $"型号/料号不一致：源项为 {sourceValue}，候选为 {candidateValue}，无法自动采用";
    }

    private static bool TryGetDecimalMagnitudeRatio(ParsedConstraint source, ParsedConstraint candidate, out decimal ratio)
    {
        ratio = 0;
        if (!string.Equals(source.Operator, "=", StringComparison.Ordinal) ||
            !string.Equals(candidate.Operator, "=", StringComparison.Ordinal) ||
            !string.Equals(source.Unit, candidate.Unit, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (source.NormalizedValue == 0 || candidate.NormalizedValue == 0)
        {
            return false;
        }

        var larger = Math.Max(Math.Abs(source.NormalizedValue), Math.Abs(candidate.NormalizedValue));
        var smaller = Math.Min(Math.Abs(source.NormalizedValue), Math.Abs(candidate.NormalizedValue));
        if (smaller == 0)
        {
            return false;
        }

        ratio = larger / smaller;
        return ratio is 10m or 100m or 1000m;
    }

    private static EvidenceRelation CompareConstraints(ParsedConstraint source, ParsedConstraint candidate)
    {
        if (source.Operator == "=" && candidate.Operator == "=")
            return source.NormalizedValue == candidate.NormalizedValue ? EvidenceRelation.Exact : EvidenceRelation.Conflict;

        if (source.Operator == "<" && candidate.Operator == "=")
            return candidate.NormalizedValue < source.NormalizedValue ? EvidenceRelation.Compatible : EvidenceRelation.Conflict;

        if (source.Operator == "<=" && candidate.Operator == "=")
            return candidate.NormalizedValue <= source.NormalizedValue ? EvidenceRelation.Compatible : EvidenceRelation.Conflict;

        if (source.Operator == ">" && candidate.Operator == "=")
            return candidate.NormalizedValue > source.NormalizedValue ? EvidenceRelation.Compatible : EvidenceRelation.Conflict;

        if (source.Operator == ">=" && candidate.Operator == "=")
            return candidate.NormalizedValue >= source.NormalizedValue ? EvidenceRelation.Compatible : EvidenceRelation.Conflict;

        return source.NormalizedValue == candidate.NormalizedValue ? EvidenceRelation.Exact : EvidenceRelation.Overlap;
    }

    private static EvidenceRelation AggregateConstraintRelation(
        IReadOnlyCollection<ParsedConstraint> sourceConstraints,
        IReadOnlyCollection<ParsedConstraint> candidateConstraints)
    {
        var relations = sourceConstraints
            .Select(sourceConstraint =>
                candidateConstraints
                    .Select(candidateConstraint => CompareConstraints(sourceConstraint, candidateConstraint))
                    .OrderBy(GetRelationPriority)
                    .FirstOrDefault(EvidenceRelation.Conflict))
            .ToList();

        if (relations.Contains(EvidenceRelation.Conflict))
            return EvidenceRelation.Conflict;

        if (relations.Contains(EvidenceRelation.Overlap))
            return EvidenceRelation.Overlap;

        if (relations.Contains(EvidenceRelation.Compatible))
            return EvidenceRelation.Compatible;

        return EvidenceRelation.Exact;
    }

    private static int GetRelationPriority(EvidenceRelation relation)
    {
        return relation switch
        {
            EvidenceRelation.Exact => 0,
            EvidenceRelation.Compatible => 1,
            EvidenceRelation.Overlap => 2,
            EvidenceRelation.ParentChild => 3,
            EvidenceRelation.PossiblyRelated => 4,
            EvidenceRelation.AliasSame => 5,
            _ => 6
        };
    }

    private static List<string> ExtractIdentifiers(string text)
    {
        return IdentifierRegex.Matches(text ?? string.Empty)
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool BelongsToSameIdentifierFamily(string sourceIdentifier, string candidateIdentifier)
    {
        var sourceFamily = GetIdentifierFamily(sourceIdentifier);
        var candidateFamily = GetIdentifierFamily(candidateIdentifier);
        return !string.IsNullOrWhiteSpace(sourceFamily) &&
               sourceFamily.Equals(candidateFamily, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetIdentifierFamily(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return string.Empty;

        var segments = identifier.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length <= 1)
            return segments.Length == 1 ? segments[0] : string.Empty;

        return string.Join('-', segments[..^1]);
    }
}
