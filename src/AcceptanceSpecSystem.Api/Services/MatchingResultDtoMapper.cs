using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 匹配结果 DTO 映射器，统一预览与执行历史中的展示字段。
/// </summary>
public static class MatchingResultDtoMapper
{
    public static MatchResultDto ToMatchResultDto(
        MatchResult result,
        string? reviewApprovalToken = null)
    {
        return new MatchResultDto
        {
            SpecId = result.MatchedSpecId ?? 0,
            Project = result.MatchedProject ?? string.Empty,
            Specification = result.MatchedSpecification ?? string.Empty,
            Acceptance = result.MatchedAcceptance,
            Remark = result.MatchedRemark,
            Score = result.Score,
            EmbeddingScore = result.EmbeddingScore,
            ScoreDetails = result.ScoreDetails,
            Decision = ToDecisionKey(result.Decision),
            EvidenceSummary = [.. result.Evidence.Summary],
            ConflictSummary = [.. result.Evidence.Conflicts],
            Issues = result.Issues.Select(ToIssueDto).ToList(),
            Entities = result.Evidence.Entities.Select(ToEntityDto).ToList(),
            LlmEquivalence = ToLlmEquivalenceDto(result.LlmEquivalence),
            TopCandidates = result.TopCandidates
                .Select(candidate => new MatchCandidateDto
                {
                    Rank = candidate.Rank,
                    SpecId = candidate.SpecId,
                    Project = candidate.Project,
                    Specification = candidate.Specification,
                    Acceptance = candidate.Acceptance,
                    Remark = candidate.Remark,
                    Score = candidate.Score,
                    EmbeddingScore = candidate.EmbeddingScore,
                    ScoreDetails = candidate.ScoreDetails,
                    Decision = result.MatchedSpecId == candidate.SpecId
                        ? ToDecisionKey(result.Decision)
                        : "manualReview",
                    EvidenceSummary = [.. candidate.Evidence.Summary],
                    ConflictSummary = [.. candidate.Evidence.Conflicts],
                    Issues = candidate.Issues.Select(ToIssueDto).ToList(),
                    Entities = candidate.Evidence.Entities.Select(ToEntityDto).ToList(),
                    RerankSummary = candidate.RerankSummary,
                    SelectionMode = ToSelectionModeKey(candidate.SelectionMode),
                    SelectionSummary = candidate.SelectionSummary,
                    MatchBasis = ToMatchBasisKey(candidate.MatchBasis),
                    LlmEquivalence = ToLlmEquivalenceDto(candidate.LlmEquivalence)
                })
                .ToList(),
            RecalledCandidateCount = result.RecalledCandidateCount,
            IsAmbiguous = result.IsAmbiguous,
            ScoreGap = result.ScoreGap,
            RerankSummary = result.RerankSummary,
            SelectionMode = ToSelectionModeKey(result.SelectionMode),
            SelectionSummary = result.SelectionSummary,
            MatchBasis = ToMatchBasisKey(result.MatchBasis),
            ReviewApprovalToken = reviewApprovalToken
        };
    }

    private static LlmEquivalenceDto? ToLlmEquivalenceDto(LlmEquivalenceAdjudicationResult? result)
    {
        if (result == null)
        {
            return null;
        }

        return new LlmEquivalenceDto
        {
            Verdict = ToEquivalenceVerdictKey(result.Verdict),
            ReasonType = ToEquivalenceReasonTypeKey(result.ReasonType),
            Reason = result.Reason,
            Confidence = result.Confidence
        };
    }

    public static string ToEquivalenceVerdictKey(LlmEquivalenceVerdict verdict)
    {
        return verdict switch
        {
            LlmEquivalenceVerdict.Equivalent => "equivalent",
            LlmEquivalenceVerdict.Different => "different",
            _ => "uncertain"
        };
    }

    private static string ToEquivalenceReasonTypeKey(LlmEquivalenceReasonType reasonType)
    {
        return reasonType switch
        {
            LlmEquivalenceReasonType.FormatOnly => "format_only",
            LlmEquivalenceReasonType.PunctuationOnly => "punctuation_only",
            LlmEquivalenceReasonType.EquivalentExpression => "equivalent_expression",
            LlmEquivalenceReasonType.SymbolEquivalent => "symbol_equivalent",
            LlmEquivalenceReasonType.SemanticDifference => "semantic_difference",
            LlmEquivalenceReasonType.SymbolConflict => "symbol_conflict",
            _ => "uncertain"
        };
    }

    private static string ToSelectionModeKey(MatchSelectionMode selectionMode)
    {
        return selectionMode switch
        {
            MatchSelectionMode.ExactShortcut => "exactShortcut",
            MatchSelectionMode.AiRerank => "aiRerank",
            _ => "embeddingTop1"
        };
    }

    private static string ToMatchBasisKey(MatchBasis matchBasis)
    {
        return matchBasis switch
        {
            MatchBasis.Specification => "specification",
            _ => "projectSpecification"
        };
    }

    private static MatchEntityEvidenceDto ToEntityDto(EntityEvidence entity)
    {
        return new MatchEntityEvidenceDto
        {
            EntityType = entity.EntityType,
            SourceValue = entity.SourceValue,
            CandidateValue = entity.CandidateValue,
            NormalizedSourceValue = entity.NormalizedSourceValue,
            NormalizedCandidateValue = entity.NormalizedCandidateValue,
            Relation = ToEvidenceRelationKey(entity.Relation)
        };
    }

    private static MatchIssueDto ToIssueDto(MatchIssue issue)
    {
        return new MatchIssueDto
        {
            Code = issue.Code,
            Severity = issue.Severity,
            FieldName = issue.FieldName,
            SourceValue = issue.SourceValue,
            CandidateValue = issue.CandidateValue,
            Message = issue.Message,
            SuggestedAction = issue.SuggestedAction
        };
    }

    public static string ToDecisionKey(MatchDecision decision)
    {
        return decision switch
        {
            MatchDecision.AutoApply => "autoApply",
            MatchDecision.ManualReview => "manualReview",
            MatchDecision.Reject => "reject",
            _ => "manualReview"
        };
    }

    private static string ToEvidenceRelationKey(EvidenceRelation relation)
    {
        return relation switch
        {
            EvidenceRelation.Exact => "exact",
            EvidenceRelation.Compatible => "compatible",
            EvidenceRelation.Overlap => "overlap",
            EvidenceRelation.Conflict => "conflict",
            EvidenceRelation.AliasSame => "aliasSame",
            EvidenceRelation.ParentChild => "parentChild",
            EvidenceRelation.PossiblyRelated => "possiblyRelated",
            _ => "unknown"
        };
    }
}
