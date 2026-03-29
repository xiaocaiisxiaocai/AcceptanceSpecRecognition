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
            }
            else if (relation == EvidenceRelation.Overlap)
            {
                evidence.Warnings.Add($"数值约束存在重叠但不够精确：{sourceGroup.Key}");
                evidence.Summary.Add($"数值约束存在重叠：{sourceGroup.Key}");
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
        }
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
