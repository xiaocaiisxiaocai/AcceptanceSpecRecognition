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
        var sourceConstraint = _numericParser.Parse($"{source.Project} {source.Specification}", knowledge);
        var candidateConstraint = _numericParser.Parse($"{candidate.Project} {candidate.Specification}", knowledge);
        if (sourceConstraint == null || candidateConstraint == null)
            return;

        if (!string.Equals(sourceConstraint.FieldName, candidateConstraint.FieldName, StringComparison.OrdinalIgnoreCase))
            return;

        var relation = CompareConstraints(sourceConstraint, candidateConstraint);
        evidence.NumericConstraints.Add(new NumericConstraintEvidence
        {
            FieldName = sourceConstraint.FieldName,
            SourceExpression = sourceConstraint.Expression,
            CandidateExpression = candidateConstraint.Expression,
            Relation = relation
        });

        if (relation == EvidenceRelation.Conflict)
        {
            evidence.HasHardConflict = true;
            evidence.Conflicts.Add($"数值约束冲突：{sourceConstraint.Expression} vs {candidateConstraint.Expression}");
        }
        else if (relation == EvidenceRelation.Overlap)
        {
            evidence.Warnings.Add($"数值约束存在重叠但不够精确：{sourceConstraint.FieldName}");
            evidence.Summary.Add($"数值约束存在重叠：{sourceConstraint.FieldName}");
        }
        else
        {
            evidence.Summary.Add($"数值约束{(relation == EvidenceRelation.Compatible ? "相容" : "一致")}：{sourceConstraint.FieldName}");
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
        var sourceIdentifier = IdentifierRegex.Match($"{source.Project} {source.Specification}");
        var candidateIdentifier = IdentifierRegex.Match($"{candidate.Project} {candidate.Specification}");
        if (!sourceIdentifier.Success || !candidateIdentifier.Success)
            return;

        var relation = sourceIdentifier.Value.Equals(candidateIdentifier.Value, StringComparison.OrdinalIgnoreCase)
            ? EvidenceRelation.Exact
            : EvidenceRelation.Conflict;

        evidence.Identifiers.Add(new IdentifierEvidence
        {
            SourceValue = sourceIdentifier.Value,
            CandidateValue = candidateIdentifier.Value,
            Relation = relation
        });

        if (relation == EvidenceRelation.Conflict)
        {
            evidence.HasHardConflict = true;
            evidence.Conflicts.Add($"型号冲突：{sourceIdentifier.Value} vs {candidateIdentifier.Value}");
            return;
        }

        evidence.Summary.Add($"型号一致：{sourceIdentifier.Value}");
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
}
