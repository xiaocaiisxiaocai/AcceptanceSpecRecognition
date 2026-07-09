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

    private static string BuildColumnSemanticRecallPrompt(LlmColumnSemanticRecallRequest request)
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

        return $$"""
        你是验收规格表头列语义召回助手。系统已经先执行确定性列映射，你只能对未映射表头给出候选建议，不能改写已识别列。

        【字段定义】
        - Project：验收项目、检查项目、对象、项目名称。
        - Specification：规格要求、标准要求、管控要求、允收范围、技术条件。
        - Acceptance：最终验收结果、确认结果、是否响应、OK/NG、供应商确认结论。
        - Remark：备注、说明、供应商说明、补充说明。
        - Unknown：无法稳定归类或只是方法、编号、版本、责任人、空白、业务元数据。

        【禁止映射】
        - “验收方式/验收方法/检查方式/测试方法”不是 Acceptance，除非表头同时明确包含“结果/结论/确认/判定/OK/NG”。
        - 不得把序号、编号、编码、等级、分类、可修改标识映射为四个业务字段。
        - 已在 mappedFields 中存在的字段不要重复建议。

        【输入 JSON】
        {{JsonSerializer.Serialize(payload, PromptJsonOptions)}}

        仅返回严格 JSON：
        {"suggestions":[{"columnIndex":1,"header":"管控要求","targetField":"Specification","confidence":0.0,"reason":"一句话依据"}]}
        """;
    }

    // ── LLM 调用 ──

}
