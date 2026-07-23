using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class MatchingWorkflowSupportService
{
    private static bool HasText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool CanApplyMatchedSpec(
        MatchingApprovalTokenService approvalTokenService,
        FillMapping mapping,
        AcceptanceSpec selectedSpec,
        MatchResult? currentMatch,
        string? sourceProject,
        string? sourceSpecification,
        MatchingApprovalTokenService.ApprovalTokenPayload? reviewApprovalToken)
    {
        if (reviewApprovalToken != null)
        {
            return MatchesPreviewApprovalToken(
                approvalTokenService,
                reviewApprovalToken,
                mapping.SpecId ?? 0,
                sourceProject,
                sourceSpecification,
                selectedSpec);
        }

        if (currentMatch == null || !currentMatch.MatchedSpecId.HasValue)
        {
            return false;
        }

        if (mapping.SpecId != currentMatch.MatchedSpecId)
        {
            return false;
        }

        // 人工确认表示用户已经复核并接受服务器当前重新计算出的最佳匹配。
        // 规格 ID 一致性检查必须先保留，避免客户端伪造确认其他候选；
        // 但确认后不应再被 AI 的拒绝/不确定结论阻止写入 Excel。
        if (mapping.ManualConfirmed)
        {
            return true;
        }

        if (currentMatch.Decision == MatchDecision.Reject)
        {
            return false;
        }

        if (RequiresManualReviewByEquivalenceVerdict(currentMatch.LlmEquivalence?.Verdict.ToString()))
        {
            return false;
        }

        if (currentMatch.Decision == MatchDecision.AutoApply)
        {
            return true;
        }

        return false;
    }

    private static bool MatchesPreviewApprovalToken(
        MatchingApprovalTokenService approvalTokenService,
        MatchingApprovalTokenService.ApprovalTokenPayload reviewApprovalToken,
        int selectedSpecId,
        string? sourceProject,
        string? sourceSpecification,
        AcceptanceSpec selectedSpec)
    {
        return approvalTokenService.MatchesToken(
            reviewApprovalToken,
            selectedSpecId,
            sourceProject,
            sourceSpecification,
            selectedSpec);
    }

    private static bool RequiresManualReviewByEquivalenceVerdict(string? verdict)
    {
        return string.Equals(verdict, "different", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(verdict, "uncertain", StringComparison.OrdinalIgnoreCase);
    }

    private static double NormalizeHighConfidenceThreshold(double? highConfidenceThreshold)
    {
        return MatchingThresholds.NormalizeHighConfidenceThreshold(highConfidenceThreshold);
    }

    private static string GetConfidenceLevel(MatchResult? result, double highConfidenceThreshold)
    {
        if (result == null || !result.MatchedSpecId.HasValue || result.Score <= 0)
        {
            return "none";
        }

        var minScoreThreshold = Math.Clamp(result.MinScoreThreshold, 0, 1);

        if (result.Decision == MatchDecision.Reject)
        {
            return "low";
        }

        if (result.Decision != MatchDecision.AutoApply)
        {
            return result.Score >= minScoreThreshold ? "medium" : "low";
        }

        if (result.Score >= NormalizeHighConfidenceThreshold(highConfidenceThreshold))
        {
            return "high";
        }

        // LLM 判等价的自动通过：高置信归 high，中置信（含低于阈值）归 medium 供审核员优先复查
        if (result.LlmEquivalence?.Verdict == LlmEquivalenceVerdict.Equivalent)
        {
            return result.LlmEquivalence.Confidence >= MatchingThresholds.HighConfidenceLlmEquivalenceMinConfidence
                ? "high"
                : "medium";
        }

        if (result.Score >= minScoreThreshold)
        {
            return "medium";
        }

        return "low";
    }

    private static double NormalizeLlmReviewScore(double? reviewScore)
    {
        if (!reviewScore.HasValue)
        {
            return 0;
        }

        var normalized = reviewScore.Value;
        if (normalized > 0 && normalized <= 1)
        {
            normalized *= 100;
        }

        return Math.Clamp(normalized, 0, 100);
    }

    private static MatchResult CreateNoMatchResult(MatchSource source, MatchingConfig config)
    {
        return new MatchResult
        {
            SourceText = source.CombinedText,
            MinScoreThreshold = config.MinScoreThreshold,
            HighConfidenceThreshold = config.HighConfidenceThreshold,
            Decision = MatchDecision.ManualReview
        };
    }

}
