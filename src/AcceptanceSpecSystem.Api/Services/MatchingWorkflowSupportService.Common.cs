using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace AcceptanceSpecSystem.Api.Services;

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

        if (currentMatch.Decision == MatchDecision.Reject)
        {
            return false;
        }

        if (RequiresManualReviewByEquivalenceVerdict(currentMatch.LlmEquivalence?.Verdict.ToString()))
        {
            return false;
        }

        if (mapping.ManualConfirmed)
        {
            return true;
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

        if (result.LlmEquivalence?.Verdict == LlmEquivalenceVerdict.Equivalent ||
            result.Score >= NormalizeHighConfidenceThreshold(highConfidenceThreshold))
        {
            return "high";
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

}
