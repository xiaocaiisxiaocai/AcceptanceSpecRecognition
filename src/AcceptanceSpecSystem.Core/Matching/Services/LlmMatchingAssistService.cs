using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
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

/// <summary>
/// LLM 匹配辅助服务（复核 + 等价裁决）
/// </summary>
public partial class LlmMatchingAssistService :
    ILlmReviewService,
    ILlmEquivalenceAdjudicationService,
    ILlmCandidateRerankService,
    ILlmDocumentStructureAdjudicationService
{
    private readonly IPromptTemplateProvider _promptTemplateProvider;
    private readonly IAiServiceSelector _selector;
    private readonly ISemanticKernelServiceFactory _factory;
    private readonly ILogger<LlmMatchingAssistService> _logger;
    private readonly ConcurrentDictionary<string, PromptTemplateModel> _promptTemplateCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<AiServicePurpose, IReadOnlyList<AiServiceConfigModel>> _aiServiceCandidateCache = [];
    private readonly SemaphoreSlim _promptTemplateCacheLock = new(1, 1);
    private readonly SemaphoreSlim _aiServiceCandidateCacheLock = new(1, 1);
    private readonly SemaphoreSlim _dbBackedCacheInitializationLock = new(1, 1);

    public LlmMatchingAssistService(
        IPromptTemplateProvider promptTemplateProvider,
        IAiServiceSelector selector,
        ISemanticKernelServiceFactory factory,
        ILogger<LlmMatchingAssistService> logger)
    {
        _promptTemplateProvider = promptTemplateProvider;
        _selector = selector;
        _factory = factory;
        _logger = logger;
    }

    public async Task<LlmReviewResult?> ReviewAsync(LlmReviewRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BestMatchProject) &&
            string.IsNullOrWhiteSpace(request.BestMatchSpecification))
            return null;

        var template = await GetOrCreateTemplateAsync(
            PromptTemplateCatalog.GetByScene(GetReviewPromptScene(request.ReviewScene)),
            cancellationToken);
        var prompt = BuildReviewPrompt(template.Content, request);

        _logger.LogInformation("[LLM复核] 源: {Src} | 匹配: {Match} | 基础得分: {Score}",
            $"{request.SourceProject}/{request.SourceSpecification}",
            $"{request.BestMatchProject}/{request.BestMatchSpecification}",
            request.BaseScore?.ToString("0.#") ?? "N/A");
        _logger.LogDebug("[LLM复核] 完整Prompt:\n{Prompt}", prompt);

        var sw = Stopwatch.StartNew();
        var raw = await GenerateWithFallbackAsync(
            prompt,
            request.LlmServiceId,
            "LLM 复核失败",
            static (service, payload) => service.TryParseReviewResult(payload, out _),
            cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            _logger.LogWarning("[LLM复核] 所有候选服务均未返回可解析 JSON");
            return null;
        }

        _logger.LogInformation("[LLM复核] LLM输出摘要 ({Elapsed}ms): {Summary}",
            sw.ElapsedMilliseconds,
            SensitiveLogFormatter.DescribePayload(raw));

        if (TryParseReviewResult(raw, out var result))
        {
            _logger.LogInformation("[LLM复核] 解析结果: score={Score}, reason={Reason}", result.Score, result.Reason);
            return result;
        }

        _logger.LogWarning("[LLM复核] JSON解析失败, 输出摘要: {Summary}",
            SensitiveLogFormatter.DescribePayload(raw));
        return null;
    }

    public async IAsyncEnumerable<string> ReviewStreamAsync(
        LlmReviewRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BestMatchProject) &&
            string.IsNullOrWhiteSpace(request.BestMatchSpecification))
            yield break;

        var template = await GetOrCreateTemplateAsync(
            PromptTemplateCatalog.GetByScene(GetReviewPromptScene(request.ReviewScene)),
            cancellationToken);
        var prompt = BuildReviewPrompt(template.Content, request);

        _logger.LogInformation("[LLM复核-Stream] 源: {Src} | 匹配: {Match} | 基础得分: {Score}",
            $"{request.SourceProject}/{request.SourceSpecification}",
            $"{request.BestMatchProject}/{request.BestMatchSpecification}",
            request.BaseScore?.ToString("0.#") ?? "N/A");
        _logger.LogDebug("[LLM复核-Stream] 完整Prompt:\n{Prompt}", prompt);

        await foreach (var chunk in GenerateStreamWithFallbackAsync(prompt, request.LlmServiceId, "LLM 复核失败", cancellationToken))
        {
            yield return chunk;
        }
    }

    public bool TryParseReviewResult(string raw, out LlmReviewResult result)
    {
        result = null!;
        if (!TryParseJson(raw, out var doc))
            return false;

        if (!TryGetDouble(doc.RootElement, "score", out var score))
            return false;

        score = Math.Clamp(score, 0, 100);
        var reason = TryGetString(doc.RootElement, "reason");
        var commentary = TryGetString(doc.RootElement, "commentary");

        result = new LlmReviewResult
        {
            Score = score,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason,
            Commentary = string.IsNullOrWhiteSpace(commentary) ? null : commentary
        };
        return true;
    }

    public async Task<LlmEquivalenceAdjudicationResult?> AdjudicateAsync(
        LlmEquivalenceAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        if ((string.IsNullOrWhiteSpace(request.SourceProject) &&
             string.IsNullOrWhiteSpace(request.SourceSpecification)) ||
            (string.IsNullOrWhiteSpace(request.CandidateProject) &&
             string.IsNullOrWhiteSpace(request.CandidateSpecification)))
        {
            return null;
        }

        var template = await GetOrCreateTemplateAsync(
            PromptTemplateCatalog.GetByScene(PromptTemplateScene.MatchingEquivalenceAdjudication),
            cancellationToken);
        var prompt = BuildEquivalenceAdjudicationPrompt(template.Content, request);

        _logger.LogInformation(
            "[LLM等价裁决] 源: {Source} | 候选: {Candidate} | 当前决策: {Decision}",
            $"{request.SourceProject}/{request.SourceSpecification}",
            $"{request.CandidateProject}/{request.CandidateSpecification}",
            request.CurrentDecision);
        _logger.LogDebug("[LLM等价裁决] 完整Prompt:\n{Prompt}", prompt);

        var sw = Stopwatch.StartNew();
        var raw = await GenerateWithFallbackAsync(
            prompt,
            request.LlmServiceId,
            "LLM 等价裁决失败",
            static (service, payload) => service.TryParseAdjudicationResult(payload, out _),
            cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            _logger.LogWarning("[LLM等价裁决] 所有候选服务均未返回可解析 JSON");
            return null;
        }

        _logger.LogInformation("[LLM等价裁决] LLM输出摘要 ({Elapsed}ms): {Summary}",
            sw.ElapsedMilliseconds,
            SensitiveLogFormatter.DescribePayload(raw));

        if (TryParseAdjudicationResult(raw, out var result))
        {
            _logger.LogInformation("[LLM等价裁决] 解析结果: verdict={Verdict}, reasonType={ReasonType}, confidence={Confidence}",
                result.Verdict, result.ReasonType, result.Confidence);
            return result;
        }

        _logger.LogWarning("[LLM等价裁决] JSON解析失败, 输出摘要: {Summary}",
            SensitiveLogFormatter.DescribePayload(raw));
        return null;
    }

    public async Task<LlmCandidateRerankResult?> RerankAsync(
        LlmCandidateRerankRequest request,
        CancellationToken cancellationToken = default)
    {
        if ((string.IsNullOrWhiteSpace(request.SourceProject) &&
             string.IsNullOrWhiteSpace(request.SourceSpecification)) ||
            request.Candidates.Count == 0)
        {
            return null;
        }

        var template = await GetOrCreateTemplateAsync(
            PromptTemplateCatalog.GetByScene(PromptTemplateScene.MatchingCandidateRerank),
            cancellationToken);
        var prompt = BuildCandidateRerankPrompt(template.Content, request);

        _logger.LogInformation(
            "[LLM候选重排] 源: {Source} | 当前Top1: {Top1SpecId} | 候选数: {Count}",
            $"{request.SourceProject}/{request.SourceSpecification}",
            request.CurrentTopCandidateSpecId,
            request.Candidates.Count);
        _logger.LogDebug("[LLM候选重排] 完整Prompt:\n{Prompt}", prompt);

        var sw = Stopwatch.StartNew();
        var raw = await GenerateWithFallbackAsync(
            prompt,
            request.LlmServiceId,
            "LLM 候选重排失败",
            static (service, payload) => service.TryParseRerankResult(payload, out _),
            cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            _logger.LogWarning("[LLM候选重排] 所有候选服务均未返回可解析 JSON");
            return null;
        }

        _logger.LogInformation("[LLM候选重排] LLM输出摘要 ({Elapsed}ms): {Summary}",
            sw.ElapsedMilliseconds,
            SensitiveLogFormatter.DescribePayload(raw));

        if (TryParseRerankResult(raw, out var result))
        {
            _logger.LogInformation(
                "[LLM候选重排] 解析结果: selectedSpecId={SelectedSpecId}, confidence={Confidence}",
                result.SelectedSpecId,
                result.Confidence);
            return result;
        }

        _logger.LogWarning("[LLM候选重排] JSON解析失败, 输出摘要: {Summary}",
            SensitiveLogFormatter.DescribePayload(raw));
        return null;
    }

    public async Task<LlmDocumentStructureAdjudicationResult?> AdjudicateAsync(
        LlmDocumentStructureAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        var template = await GetOrCreateTemplateAsync(
            PromptTemplateCatalog.GetByScene(PromptTemplateScene.SmartConfigStructureRecognition),
            cancellationToken);
        var prompt = BuildDocumentStructurePrompt(template.Content, request);

        var raw = await GenerateWithFallbackAsync(
            prompt,
            request.LlmServiceId,
            "LLM 结构裁决失败",
            static (service, payload) => service.TryParseDocumentStructureResult(payload, out _),
            cancellationToken);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return TryParseDocumentStructureResult(raw, out var result)
            ? result
            : null;
    }

    public bool TryParseAdjudicationResult(string raw, out LlmEquivalenceAdjudicationResult result)
    {
        result = null!;
        if (!TryParseJson(raw, out var doc))
            return false;

        var verdictText = TryGetString(doc.RootElement, "verdict");
        var reasonTypeText = TryGetString(doc.RootElement, "reasonType");
        if (!TryParseEquivalenceVerdict(verdictText, out var verdict) ||
            !TryParseEquivalenceReasonType(reasonTypeText, out var reasonType))
        {
            return false;
        }

        if (!IsCompatibleEquivalenceReasonType(verdict, reasonType))
        {
            return false;
        }

        if (!TryGetDouble(doc.RootElement, "confidence", out var confidence))
            return false;

        result = new LlmEquivalenceAdjudicationResult
        {
            Verdict = verdict,
            ReasonType = reasonType,
            Confidence = Math.Clamp(confidence, 0, 1),
            Reason = TryGetString(doc.RootElement, "reason")
        };
        return true;
    }

    public bool TryParseRerankResult(string raw, out LlmCandidateRerankResult result)
    {
        result = null!;
        if (!TryParseJson(raw, out var doc))
            return false;

        if (!TryGetInt32(doc.RootElement, "selectedSpecId", out var selectedSpecId) ||
            selectedSpecId <= 0)
        {
            return false;
        }

        if (!TryGetDouble(doc.RootElement, "confidence", out var confidence))
            return false;

        result = new LlmCandidateRerankResult
        {
            SelectedSpecId = selectedSpecId,
            Confidence = Math.Clamp(confidence, 0, 1),
            Reason = TryGetString(doc.RootElement, "reason")
        };
        return true;
    }

    public bool TryParseDocumentStructureResult(
        string raw,
        out LlmDocumentStructureAdjudicationResult result)
    {
        result = null!;
        if (!TryParseJson(raw, out var doc))
            return false;

        if (!doc.RootElement.TryGetProperty("tables", out var tablesElement) ||
            tablesElement.ValueKind != JsonValueKind.Array ||
            !TryGetDouble(doc.RootElement, "confidence", out var confidence))
        {
            return false;
        }

        var tables = new List<DocumentStructureCandidate>();
        foreach (var item in tablesElement.EnumerateArray())
        {
            if (!TryGetInt32(item, "tableIndex", out var tableIndex))
            {
                return false;
            }

            tables.Add(new DocumentStructureCandidate
            {
                TableIndex = tableIndex,
                TableName = TryGetString(item, "tableName"),
                HeaderRowIndex = TryGetInt32(item, "headerRowIndex", out var headerRowIndex) ? headerRowIndex : 0,
                HeaderRowCount = TryGetInt32(item, "headerRowCount", out var headerRowCount) ? Math.Max(1, headerRowCount) : 1,
                DataStartRowIndex = TryGetInt32(item, "dataStartRowIndex", out var dataStartRowIndex) ? dataStartRowIndex : 1,
                DataEndRowIndex = TryGetNullableInt32(item, "dataEndRowIndex"),
                ProjectColumnIndex = TryGetNullableInt32(item, "projectColumnIndex"),
                SpecificationColumnIndex = TryGetNullableInt32(item, "specificationColumnIndex"),
                AcceptanceColumnIndex = TryGetNullableInt32(item, "acceptanceColumnIndex"),
                RemarkColumnIndex = TryGetNullableInt32(item, "remarkColumnIndex"),
                IsSpecificationOnly = TryGetBool(item, "isSpecificationOnly", out var isSpecificationOnly) && isSpecificationOnly,
                Confidence = TryGetDouble(item, "confidence", out var tableConfidence)
                    ? Math.Clamp(tableConfidence, 0, 1)
                    : Math.Clamp(confidence, 0, 1),
                Source = DocumentStructureCandidateSource.Llm
            });
        }

        result = new LlmDocumentStructureAdjudicationResult
        {
            Tables = tables,
            Confidence = Math.Clamp(confidence, 0, 1),
            Decision = TryGetString(doc.RootElement, "decision") ?? "needConfirm",
            Reason = TryGetString(doc.RootElement, "reason") ?? string.Empty
        };
        return true;
    }

}
