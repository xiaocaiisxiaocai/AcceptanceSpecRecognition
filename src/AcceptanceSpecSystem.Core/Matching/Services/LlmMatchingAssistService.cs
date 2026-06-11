using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Diagnostics;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AcceptanceSpecSystem.Core.Matching.Services;

/// <summary>
/// LLM 匹配辅助服务（复核 + 等价裁决）
/// </summary>
public class LlmMatchingAssistService : ILlmReviewService, ILlmEquivalenceAdjudicationService, ILlmCandidateRerankService
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

    private sealed class ThinkContentFilter
    {
        private const string ThinkOpen = "<think>";
        private const string ThinkClose = "</think>";
        private readonly StringBuilder _buffer = new();
        private bool _insideThinkBlock;

        public string Push(string? chunk)
        {
            if (string.IsNullOrEmpty(chunk))
            {
                return string.Empty;
            }

            _buffer.Append(chunk);
            return DrainBuffer(finalize: false);
        }

        public string Flush()
        {
            return DrainBuffer(finalize: true);
        }

        private string DrainBuffer(bool finalize)
        {
            if (_buffer.Length == 0)
            {
                return string.Empty;
            }

            var output = new StringBuilder();
            var text = _buffer.ToString();
            var index = 0;

            while (index < text.Length)
            {
                if (_insideThinkBlock)
                {
                    var closeIndex = text.IndexOf(ThinkClose, index, StringComparison.OrdinalIgnoreCase);
                    if (closeIndex < 0)
                    {
                        if (finalize)
                        {
                            index = text.Length;
                        }
                        else
                        {
                            KeepTail(text, index);
                            return output.ToString();
                        }
                    }
                    else
                    {
                        index = closeIndex + ThinkClose.Length;
                        _insideThinkBlock = false;
                    }

                    continue;
                }

                var openIndex = text.IndexOf(ThinkOpen, index, StringComparison.OrdinalIgnoreCase);
                if (openIndex < 0)
                {
                    if (finalize)
                    {
                        output.Append(text.AsSpan(index));
                        index = text.Length;
                    }
                    else
                    {
                        var safeLength = GetSafeOutputLength(text, index, ThinkOpen.Length);
                        if (safeLength > 0)
                        {
                            output.Append(text.AsSpan(index, safeLength));
                            index += safeLength;
                        }

                        KeepTail(text, index);
                        return output.ToString();
                    }
                }
                else
                {
                    output.Append(text.AsSpan(index, openIndex - index));
                    index = openIndex + ThinkOpen.Length;
                    _insideThinkBlock = true;
                }
            }

            _buffer.Clear();
            return output.ToString();
        }

        private void KeepTail(string text, int index)
        {
            _buffer.Clear();
            if (index < text.Length)
            {
                _buffer.Append(text.AsSpan(index));
            }
        }

        private static int GetSafeOutputLength(string text, int startIndex, int markerLength)
        {
            var remaining = text.Length - startIndex;
            if (remaining <= markerLength - 1)
            {
                return 0;
            }

            return remaining - (markerLength - 1);
        }
    }

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

    // ── Prompt 构建 ──

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

    // ── LLM 调用 ──

    private async Task<string?> GenerateWithFallbackAsync(
        string prompt,
        int? serviceId,
        string errorMessage,
        Func<LlmMatchingAssistService, string, bool> isAcceptablePayload,
        CancellationToken cancellationToken)
    {
        var candidates = await GetCachedCandidatesAsync(AiServicePurpose.Llm, serviceId, cancellationToken);
        if (candidates.Count == 0)
            throw new AiServiceUnavailableException(errorMessage);

        var errors = new List<string>();
        var sawRawResponse = false;
        foreach (var cfg in candidates)
        {
            try
            {
                _logger.LogDebug("调用 LLM 服务: {Name} ({Model})", cfg.Name, cfg.LlmModel);
                var chat = _factory.CreateChatCompletionService(cfg);
                var history = new ChatHistory();
                history.AddUserMessage(prompt);
                var settings = CreatePromptExecutionSettings(cfg);

                var message = await chat.GetChatMessageContentAsync(history, settings, cancellationToken: cancellationToken);
                var raw = SanitizeLlmOutput(message.Content);
                sawRawResponse = true;

                if (isAcceptablePayload(this, raw))
                {
                    return raw;
                }

                errors.Add($"{cfg.Name}: invalid_payload");
                _logger.LogWarning("LLM 输出未通过解析校验，尝试下一个服务: {Name}; 输出摘要: {Summary}",
                    cfg.Name,
                    SensitiveLogFormatter.DescribePayload(raw));
            }
            catch (Exception ex)
            {
                errors.Add($"{cfg.Name}: {ex.Message}");
                _logger.LogWarning(ex, "LLM 调用失败: {Name}", cfg.Name);
            }
        }

        if (sawRawResponse)
        {
            return null;
        }

        throw new AiServiceUnavailableException(errorMessage, errors);
    }

    private async IAsyncEnumerable<string> GenerateStreamWithFallbackAsync(
        string prompt,
        int? serviceId,
        string errorMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var candidates = await GetCachedCandidatesAsync(AiServicePurpose.Llm, serviceId, cancellationToken);
        if (candidates.Count == 0)
            throw new AiServiceUnavailableException(errorMessage);

        var errors = new List<string>();
        foreach (var cfg in candidates)
        {
            _logger.LogDebug("流式调用 LLM 服务: {Name} ({Model})", cfg.Name, cfg.LlmModel);
            var produced = false;
            var channel = System.Threading.Channels.Channel.CreateUnbounded<string>();

            _ = Task.Run(async () =>
            {
                try
                {
                    var chat = _factory.CreateChatCompletionService(cfg);
                    var history = new ChatHistory();
                    history.AddUserMessage(prompt);
                    var settings = CreatePromptExecutionSettings(cfg);
                    var thinkFilter = new ThinkContentFilter();

                    await foreach (var chunk in chat.GetStreamingChatMessageContentsAsync(history, settings, cancellationToken: cancellationToken))
                    {
                        if (!string.IsNullOrWhiteSpace(chunk.Content))
                        {
                            var sanitized = thinkFilter.Push(chunk.Content);
                            if (!string.IsNullOrWhiteSpace(sanitized))
                            {
                                await channel.Writer.WriteAsync(sanitized, cancellationToken);
                            }
                        }
                    }

                    var tail = thinkFilter.Flush();
                    if (!string.IsNullOrWhiteSpace(tail))
                    {
                        await channel.Writer.WriteAsync(tail, cancellationToken);
                    }

                    channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            }, cancellationToken);

            while (await channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (channel.Reader.TryRead(out var item))
                {
                    produced = true;
                    yield return item;
                }
            }

            try
            {
                await channel.Reader.Completion;
                if (produced)
                {
                    yield break;
                }

                errors.Add($"{cfg.Name}: empty_stream");
                _logger.LogWarning("LLM 流式调用返回空流，尝试下一个服务: {Name}", cfg.Name);
            }
            catch (Exception ex)
            {
                errors.Add($"{cfg.Name}: {ex.Message}");
                _logger.LogWarning(ex, "LLM 流式调用失败: {Name}", cfg.Name);
                if (produced)
                    throw new AiServiceUnavailableException(errorMessage, errors, ex);
            }
        }

        throw new AiServiceUnavailableException(errorMessage, errors);
    }

    private static OpenAIPromptExecutionSettings CreatePromptExecutionSettings(AiServiceConfigModel config)
    {
        return new OpenAIPromptExecutionSettings
        {
            Temperature = 0
        };
    }

    // ── 模板管理 ──

    /// <summary>
    /// 获取或创建 Prompt 模板；如果 DB 中存储的是旧版系统模板内容则自动升级
    /// </summary>
    private async Task<PromptTemplateModel> GetOrCreateTemplateAsync(
        SystemPromptTemplateDefinition definition,
        CancellationToken cancellationToken)
    {
        var cacheKey = GetTemplateCacheKey(definition);
        if (_promptTemplateCache.TryGetValue(cacheKey, out var cachedTemplate))
        {
            return cachedTemplate;
        }

        await _dbBackedCacheInitializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_promptTemplateCache.TryGetValue(cacheKey, out cachedTemplate))
            {
                return cachedTemplate;
            }

            return await GetOrCreateTemplateCoreAsync(definition, cacheKey, cancellationToken);
        }
        finally
        {
            _dbBackedCacheInitializationLock.Release();
        }
    }

    private async Task<PromptTemplateModel> GetOrCreateTemplateCoreAsync(
        SystemPromptTemplateDefinition definition,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        await _promptTemplateCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_promptTemplateCache.TryGetValue(cacheKey, out var cachedTemplate))
            {
                return cachedTemplate;
            }

            var loadedTemplate = await LoadTemplateAsync(definition, cancellationToken);
            _promptTemplateCache[cacheKey] = loadedTemplate;
            return loadedTemplate;
        }
        finally
        {
            _promptTemplateCacheLock.Release();
        }
    }

    private async Task<PromptTemplateModel> LoadTemplateAsync(
        SystemPromptTemplateDefinition definition,
        CancellationToken cancellationToken)
    {
        var template = await _promptTemplateProvider.GetOrCreateSystemAsync(
            definition.Scene,
            definition.Name,
            definition.DisplayName,
            definition.DefaultContent,
            cancellationToken);

        var content = template.Content;
        var changed = false;

        // 与 SystemPromptTemplateInitializer 保持一致：历任旧版默认内容（LegacyDefaultContent
        // 与 AdditionalLegacyContents）都视为可自动升级，避免两条升级链行为不一致。
        var isLegacyContent =
            (definition.LegacyDefaultContent != null &&
             string.Equals(content.Trim(), definition.LegacyDefaultContent.Trim(), StringComparison.Ordinal)) ||
            (definition.AdditionalLegacyContents != null &&
             definition.AdditionalLegacyContents.Any(legacy =>
                 string.Equals(content.Trim(), legacy.Trim(), StringComparison.Ordinal)));

        if (isLegacyContent)
        {
            _logger.LogInformation("自动升级 LLM Prompt 模板 [{Name}]：检测到旧版默认内容，更新为新版", definition.Name);
            content = definition.DefaultContent;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            content = definition.DefaultContent;
            changed = true;
        }

        if (changed)
        {
            await _promptTemplateProvider.SaveContentAsync(template.Id, content, cancellationToken);
            template.Content = content;
        }

        _logger.LogInformation("确保系统 LLM Prompt 模板可用: {Name}", definition.Name);
        return template;
    }

    private async Task<IReadOnlyList<AiServiceConfigModel>> GetCachedCandidatesAsync(
        AiServicePurpose purpose,
        int? preferredId,
        CancellationToken cancellationToken)
    {
        if (!_aiServiceCandidateCache.TryGetValue(purpose, out var cachedCandidates))
        {
            await _dbBackedCacheInitializationLock.WaitAsync(cancellationToken);
            try
            {
                if (!_aiServiceCandidateCache.TryGetValue(purpose, out cachedCandidates))
                {
                    cachedCandidates = await GetCachedCandidatesCoreAsync(purpose, cancellationToken);
                }
            }
            finally
            {
                _dbBackedCacheInitializationLock.Release();
            }
        }

        return FilterCandidatesForPreferredService(cachedCandidates, preferredId);
    }

    private async Task<IReadOnlyList<AiServiceConfigModel>> GetCachedCandidatesCoreAsync(
        AiServicePurpose purpose,
        CancellationToken cancellationToken)
    {
        await _aiServiceCandidateCacheLock.WaitAsync(cancellationToken);
        try
        {
            if (!_aiServiceCandidateCache.TryGetValue(purpose, out var cachedCandidates))
            {
                cachedCandidates = await _selector.GetCandidatesAsync(purpose, preferredId: null, cancellationToken);
                _aiServiceCandidateCache[purpose] = cachedCandidates;
            }

            return cachedCandidates;
        }
        finally
        {
            _aiServiceCandidateCacheLock.Release();
        }
    }

    private static IReadOnlyList<AiServiceConfigModel> FilterCandidatesForPreferredService(
        IReadOnlyList<AiServiceConfigModel> candidates,
        int? preferredId)
    {
        if (!preferredId.HasValue)
        {
            return candidates;
        }

        var preferred = candidates.FirstOrDefault(candidate => candidate.Id == preferredId.Value);
        if (preferred == null)
        {
            return [];
        }

        // 用户显式选择的 LLM 只使用该服务，避免高显存模型作为自动兜底触发 Ollama 驱逐。
        return [preferred];
    }

    private static string GetTemplateCacheKey(SystemPromptTemplateDefinition definition)
    {
        return $"{(int)definition.Scene}:{definition.Name}";
    }

    // ── 工具方法 ──

    private static string ApplyTemplate(string template, Dictionary<string, string> values)
    {
        return PromptTemplatePlaceholderRenderer.ReplacePlaceholders(template, values);
    }

    private bool TryParseJson(string raw, out JsonDocument doc)
    {
        doc = null!;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var text = ExtractJson(SanitizeLlmOutput(raw));
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            doc = JsonDocument.Parse(text);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM 输出 JSON 解析失败，输出摘要: {Summary}",
                SensitiveLogFormatter.DescribePayload(raw));
            return false;
        }
    }

    private static string ExtractJson(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = text.IndexOf('\n');
            if (firstLineEnd >= 0)
                text = text[(firstLineEnd + 1)..];
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
                text = text[..lastFence];
        }

        var candidates = ExtractJsonObjects(text).ToList();
        if (candidates.Count > 0)
            return candidates[^1].Trim();

        return text.Trim();
    }

    private static IEnumerable<string> ExtractJsonObjects(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        for (var start = 0; start < text.Length; start++)
        {
            if (text[start] != '{')
            {
                continue;
            }

            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var index = start; index < text.Length; index++)
            {
                var current = text[index];
                if (current == '"' && !escaped)
                {
                    inString = !inString;
                }

                if (!inString)
                {
                    if (current == '{')
                    {
                        depth++;
                    }
                    else if (current == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            yield return text[start..(index + 1)];
                            start = index;
                            break;
                        }
                    }
                }

                if (current == '\\' && !escaped)
                {
                    escaped = true;
                }
                else
                {
                    escaped = false;
                }
            }
        }
    }

    private static string SanitizeLlmOutput(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var filter = new ThinkContentFilter();
        var sanitized = filter.Push(raw) + filter.Flush();
        return sanitized.Trim();
    }

    private static bool TryGetDouble(JsonElement element, string name, out double value)
    {
        value = 0;
        if (!element.TryGetProperty(name, out var prop))
            return false;

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out value))
            return true;

        if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), out value))
            return true;

        return false;
    }

    private static bool TryGetInt32(JsonElement element, string name, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(name, out var prop))
            return false;

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out value))
            return true;

        if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out value))
            return true;

        return false;
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop))
            return null;

        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
    }

    private static bool TryParseEquivalenceVerdict(string? value, out LlmEquivalenceVerdict verdict)
    {
        verdict = LlmEquivalenceVerdict.Uncertain;
        return value?.Trim().ToLowerInvariant() switch
        {
            "equivalent" => SetVerdict(LlmEquivalenceVerdict.Equivalent, out verdict),
            "different" => SetVerdict(LlmEquivalenceVerdict.Different, out verdict),
            "uncertain" => SetVerdict(LlmEquivalenceVerdict.Uncertain, out verdict),
            _ => false
        };
    }

    private static bool TryParseEquivalenceReasonType(string? value, out LlmEquivalenceReasonType reasonType)
    {
        reasonType = LlmEquivalenceReasonType.Uncertain;
        return value?.Trim().ToLowerInvariant() switch
        {
            "format_only" => SetReasonType(LlmEquivalenceReasonType.FormatOnly, out reasonType),
            "punctuation_only" => SetReasonType(LlmEquivalenceReasonType.PunctuationOnly, out reasonType),
            "equivalent_expression" => SetReasonType(LlmEquivalenceReasonType.EquivalentExpression, out reasonType),
            "symbol_equivalent" => SetReasonType(LlmEquivalenceReasonType.SymbolEquivalent, out reasonType),
            "semantic_difference" => SetReasonType(LlmEquivalenceReasonType.SemanticDifference, out reasonType),
            "symbol_conflict" => SetReasonType(LlmEquivalenceReasonType.SymbolConflict, out reasonType),
            "uncertain" => SetReasonType(LlmEquivalenceReasonType.Uncertain, out reasonType),
            _ => false
        };
    }

    private static bool SetVerdict(LlmEquivalenceVerdict value, out LlmEquivalenceVerdict verdict)
    {
        verdict = value;
        return true;
    }

    private static bool SetReasonType(LlmEquivalenceReasonType value, out LlmEquivalenceReasonType reasonType)
    {
        reasonType = value;
        return true;
    }

    private static bool IsCompatibleEquivalenceReasonType(
        LlmEquivalenceVerdict verdict,
        LlmEquivalenceReasonType reasonType)
    {
        return verdict switch
        {
            LlmEquivalenceVerdict.Equivalent => reasonType is
                LlmEquivalenceReasonType.FormatOnly or
                LlmEquivalenceReasonType.PunctuationOnly or
                LlmEquivalenceReasonType.EquivalentExpression or
                LlmEquivalenceReasonType.SymbolEquivalent,
            LlmEquivalenceVerdict.Different => reasonType is
                LlmEquivalenceReasonType.SemanticDifference or
                LlmEquivalenceReasonType.SymbolConflict,
            _ => reasonType == LlmEquivalenceReasonType.Uncertain
        };
    }
}
