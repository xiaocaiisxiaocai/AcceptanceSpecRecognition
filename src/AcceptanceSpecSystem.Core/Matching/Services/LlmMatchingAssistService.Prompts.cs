using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Diagnostics;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;


namespace AcceptanceSpecSystem.Core.Matching.Services;

public partial class LlmMatchingAssistService
{
    private const string ColumnSemanticRecallCorrectionInstruction =
        """
        上一次输出未通过结构化校验或业务校验。请修正后只返回符合既定 JSON Schema 的 JSON 对象，不要附加解释或 Markdown。
        columnIndex 只能来自 unmappedHeaders；targetField 不能是 mappedFields 中已占用的字段；同一列和同一非 Unknown 字段只能建议一次；验收方法列不得映射到 Acceptance。
        如果没有任何合法建议，返回 {"suggestions":[]}。
        """;

    private static readonly JsonSerializerOptions PromptJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string BuildReviewPrompt(string template, LlmReviewRequest request)
    {
        return ApplyTemplate(template, new Dictionary<string, string>
        {
            ["workflowScene"] = GetWorkflowSceneLabel(request.ReviewScene),
            ["sourceProject"] = request.SourceProject,
            ["sourceSpecification"] = request.SourceSpecification,
            ["bestMatchProject"] = request.BestMatchProject,
            ["bestMatchSpecification"] = request.BestMatchSpecification,
            ["bestMatchAcceptance"] = request.BestMatchAcceptance ?? "(无)",
            ["bestMatchRemark"] = request.BestMatchRemark ?? "(无)",
            ["baseScore"] = request.BaseScore?.ToString("0.##") ?? "N/A",
            ["scoreDetailsJson"] = JsonSerializer.Serialize(request.ScoreDetails),
            ["currentDecision"] = request.CurrentDecision,
            ["reviewTrigger"] = request.ReviewTrigger ?? "证据不足，需要复核",
            ["evidenceSummaryJson"] = JsonSerializer.Serialize(request.EvidenceSummary),
            ["conflictSummaryJson"] = JsonSerializer.Serialize(request.ConflictSummary)
        });
    }

    private static string GetWorkflowSceneLabel(LlmReviewScene scene)
    {
        return scene switch
        {
            LlmReviewScene.ImportDuplicateReview => "导入重复复核",
            _ => "智能填充复核"
        };
    }

    private static PromptTemplateScene GetReviewPromptScene(LlmReviewScene scene)
    {
        return scene switch
        {
            LlmReviewScene.ImportDuplicateReview => PromptTemplateScene.ImportDuplicateReview,
            _ => PromptTemplateScene.MatchingReview
        };
    }

    private static string BuildEquivalenceAdjudicationPrompt(
        string template,
        LlmEquivalenceAdjudicationRequest request)
    {
        return ApplyTemplate(template, new Dictionary<string, string>
        {
            ["sourceProject"] = request.SourceProject,
            ["sourceSpecification"] = request.SourceSpecification,
            ["candidateProject"] = request.CandidateProject,
            ["candidateSpecification"] = request.CandidateSpecification,
            ["candidateAcceptance"] = string.IsNullOrWhiteSpace(request.CandidateAcceptance) ? "(无)" : request.CandidateAcceptance,
            ["candidateRemark"] = string.IsNullOrWhiteSpace(request.CandidateRemark) ? "(无)" : request.CandidateRemark,
            ["currentDecision"] = request.CurrentDecision,
            ["scoreDetailsJson"] = JsonSerializer.Serialize(request.ScoreDetails),
            ["evidenceSummaryJson"] = JsonSerializer.Serialize(request.EvidenceSummary),
            ["conflictSummaryJson"] = JsonSerializer.Serialize(request.ConflictSummary)
        });
    }

    private static string BuildCandidateRerankPrompt(
        string template,
        LlmCandidateRerankRequest request)
    {
        var candidatePayload = request.Candidates.Select(candidate => new
        {
            candidate.Rank,
            candidate.SpecId,
            candidate.Project,
            candidate.Specification,
            candidate.EmbeddingScore,
            candidate.FinalScore,
            candidate.ScoreDetails,
            candidate.EvidenceSummary,
            candidate.ConflictSummary
        });

        return ApplyTemplate(template, new Dictionary<string, string>
        {
            ["sourceProject"] = request.SourceProject,
            ["sourceSpecification"] = request.SourceSpecification,
            ["currentTopCandidateSpecId"] = request.CurrentTopCandidateSpecId.ToString(),
            ["candidatesJson"] = JsonSerializer.Serialize(candidatePayload)
        });
    }

    private static string BuildDocumentStructurePrompt(
        string template,
        LlmDocumentStructureAdjudicationRequest request)
    {
        return ApplyTemplate(template, new Dictionary<string, string>
        {
            ["workflowScene"] = "智能结构识别",
            ["documentTablesJson"] = request.DocumentTablesJson,
            ["ruleCandidatesJson"] = JsonSerializer.Serialize(request.RuleCandidates, PromptJsonOptions),
            ["referenceCasesJson"] = JsonSerializer.Serialize(request.ReferenceCases, PromptJsonOptions)
        });
    }

    private static string BuildColumnSemanticRecallPrompt(
        string template,
        LlmColumnSemanticRecallRequest request)
    {
        var payload = new
        {
            request.TableIndex,
            request.TableName,
            request.Headers,
            request.MappedFields,
            request.UnmappedHeaders,
            request.SampleRows
        };

        return ApplyTemplate(template, new Dictionary<string, string>
        {
            ["inputJson"] = JsonSerializer.Serialize(payload, PromptJsonOptions)
        });
    }

    // ── LLM 调用 ──

}
