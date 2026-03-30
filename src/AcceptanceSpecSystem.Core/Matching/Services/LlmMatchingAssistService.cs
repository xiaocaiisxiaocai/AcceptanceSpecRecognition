using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AcceptanceSpecSystem.Core.Matching.Services;

/// <summary>
/// LLM 匹配辅助服务（复核 + 生成建议）
/// </summary>
public class LlmMatchingAssistService : ILlmReviewService, ILlmSuggestionService, ILlmEntityResolutionService
{
    private readonly IPromptTemplateProvider _promptTemplateProvider;
    private readonly IAiServiceSelector _selector;
    private readonly ISemanticKernelServiceFactory _factory;
    private readonly ILogger<LlmMatchingAssistService> _logger;

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

        var scene = request.ReviewScene == LlmReviewScene.ImportDuplicateReview
            ? PromptTemplateScene.ImportDuplicateReview
            : PromptTemplateScene.MatchingReview;
        var template = await GetOrCreateTemplateAsync(PromptTemplateCatalog.GetByScene(scene), cancellationToken);
        var prompt = BuildReviewPrompt(template.Content, request);

        _logger.LogInformation("[LLM复核] 源: {Src} | 匹配: {Match} | 基础得分: {Score}",
            $"{request.SourceProject}/{request.SourceSpecification}",
            $"{request.BestMatchProject}/{request.BestMatchSpecification}",
            request.BaseScore?.ToString("0.#") ?? "N/A");
        _logger.LogDebug("[LLM复核] 完整Prompt:\n{Prompt}", prompt);

        var sw = Stopwatch.StartNew();
        var raw = await GenerateWithFallbackAsync(prompt, request.LlmServiceId, "LLM 复核失败", cancellationToken);
        _logger.LogInformation("[LLM复核] LLM原始输出 ({Elapsed}ms): {Raw}", sw.ElapsedMilliseconds, raw);

        if (TryParseReviewResult(raw, out var result))
        {
            _logger.LogInformation("[LLM复核] 解析结果: score={Score}, reason={Reason}", result.Score, result.Reason);
            return result;
        }

        _logger.LogWarning("[LLM复核] JSON解析失败, 原始输出: {Raw}", raw);
        return null;
    }

    public async IAsyncEnumerable<string> ReviewStreamAsync(
        LlmReviewRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BestMatchProject) &&
            string.IsNullOrWhiteSpace(request.BestMatchSpecification))
            yield break;

        var scene = request.ReviewScene == LlmReviewScene.ImportDuplicateReview
            ? PromptTemplateScene.ImportDuplicateReview
            : PromptTemplateScene.MatchingReview;
        var template = await GetOrCreateTemplateAsync(PromptTemplateCatalog.GetByScene(scene), cancellationToken);
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

    public async Task<LlmSuggestionResult?> GenerateSuggestionAsync(
        LlmSuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        var template = await GetOrCreateTemplateAsync(PromptTemplateCatalog.GetByScene(PromptTemplateScene.MatchingGenerate), cancellationToken);
        var prompt = BuildSuggestionPrompt(template.Content, request);

        _logger.LogInformation("[LLM建议] 源: {Src} | 参考: {Ref}",
            $"{request.SourceProject}/{request.SourceSpecification}",
            request.BestMatchProject != null ? $"{request.BestMatchProject}/{request.BestMatchSpecification} (得分{request.BestMatchScore:P0})" : "无");
        _logger.LogDebug("[LLM建议] 完整Prompt:\n{Prompt}", prompt);

        var sw = Stopwatch.StartNew();
        var raw = await GenerateWithFallbackAsync(prompt, request.LlmServiceId, "LLM 生成失败", cancellationToken);
        _logger.LogInformation("[LLM建议] LLM原始输出 ({Elapsed}ms): {Raw}", sw.ElapsedMilliseconds, raw);

        if (TryParseSuggestionResult(raw, out var result))
        {
            _logger.LogInformation("[LLM建议] 解析结果: acceptance={Acceptance}, remark={Remark}",
                result.Acceptance ?? "(空)", result.Remark ?? "(空)");
            return result;
        }

        _logger.LogWarning("[LLM建议] JSON解析失败, 原始输出: {Raw}", raw);
        return null;
    }

    public async IAsyncEnumerable<string> GenerateSuggestionStreamAsync(
        LlmSuggestionRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var template = await GetOrCreateTemplateAsync(PromptTemplateCatalog.GetByScene(PromptTemplateScene.MatchingGenerate), cancellationToken);
        var prompt = BuildSuggestionPrompt(template.Content, request);

        _logger.LogInformation("[LLM建议-Stream] 源: {Src} | 参考: {Ref}",
            $"{request.SourceProject}/{request.SourceSpecification}",
            request.BestMatchProject != null ? $"{request.BestMatchProject}/{request.BestMatchSpecification} (得分{request.BestMatchScore:P0})" : "无");
        _logger.LogDebug("[LLM建议-Stream] 完整Prompt:\n{Prompt}", prompt);

        await foreach (var chunk in GenerateStreamWithFallbackAsync(prompt, request.LlmServiceId, "LLM 生成失败", cancellationToken))
        {
            yield return chunk;
        }
    }

    public bool TryParseSuggestionResult(string raw, out LlmSuggestionResult result)
    {
        result = null!;
        if (!TryParseJson(raw, out var doc))
            return false;

        var acceptance = TryGetString(doc.RootElement, "acceptance");
        var remark = TryGetString(doc.RootElement, "remark");
        var reason = TryGetString(doc.RootElement, "reason");

        var hasAcceptance = !string.IsNullOrWhiteSpace(acceptance);
        var hasRemark = !string.IsNullOrWhiteSpace(remark);
        var hasReason = !string.IsNullOrWhiteSpace(reason);
        if (!hasAcceptance && !hasRemark && !hasReason)
            return false;

        result = new LlmSuggestionResult
        {
            Acceptance = hasAcceptance ? acceptance : null,
            Remark = hasRemark ? remark : null,
            Reason = hasReason ? reason : null
        };
        return true;
    }

    public async Task<LlmEntityResolutionResult?> ResolveAsync(
        LlmEntityResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SourceEntity) ||
            string.IsNullOrWhiteSpace(request.CandidateEntity))
        {
            return null;
        }

        var template = await GetOrCreateTemplateAsync(
            PromptTemplateCatalog.GetByScene(PromptTemplateScene.MatchingEntityResolution),
            cancellationToken);
        var prompt = BuildEntityResolutionPrompt(template.Content, request);

        _logger.LogInformation("[LLM实体判别] 源实体: {SourceEntity} | 候选实体: {CandidateEntity}",
            request.SourceEntity,
            request.CandidateEntity);
        _logger.LogDebug("[LLM实体判别] 完整Prompt:\n{Prompt}", prompt);

        var sw = Stopwatch.StartNew();
        var raw = await GenerateWithFallbackAsync(prompt, request.LlmServiceId, "LLM 实体判别失败", cancellationToken);
        _logger.LogInformation("[LLM实体判别] LLM原始输出 ({Elapsed}ms): {Raw}", sw.ElapsedMilliseconds, raw);

        if (TryParseEntityResolutionResult(raw, out var result))
        {
            _logger.LogInformation("[LLM实体判别] 解析结果: relation={Relation}, confidence={Confidence}",
                result.Relation,
                result.Confidence);
            return result;
        }

        _logger.LogWarning("[LLM实体判别] JSON解析失败, 原始输出: {Raw}", raw);
        return null;
    }

    public bool TryParseEntityResolutionResult(string raw, out LlmEntityResolutionResult result)
    {
        result = null!;
        if (!TryParseJson(raw, out var doc))
            return false;

        var relationText = TryGetString(doc.RootElement, "relation");
        if (!TryParseEntityRelation(relationText, out var relation))
            return false;

        if (!TryGetDouble(doc.RootElement, "confidence", out var confidence))
            return false;

        result = new LlmEntityResolutionResult
        {
            Relation = relation,
            Confidence = Math.Clamp(confidence, 0, 1),
            NormalizedEntity = TryGetString(doc.RootElement, "normalizedEntity"),
            Reason = TryGetString(doc.RootElement, "reason")
        };
        return true;
    }

    // ── Prompt 构建 ──

    private static string BuildReviewPrompt(string template, LlmReviewRequest request)
    {
        return ApplyTemplate(template, new Dictionary<string, string>
        {
            ["sourceProject"] = request.SourceProject,
            ["sourceSpecification"] = request.SourceSpecification,
            ["bestMatchProject"] = request.BestMatchProject,
            ["bestMatchSpecification"] = request.BestMatchSpecification,
            ["bestMatchAcceptance"] = request.BestMatchAcceptance ?? "(无)",
            ["bestMatchRemark"] = request.BestMatchRemark ?? "(无)",
            ["baseScore"] = request.BaseScore?.ToString("0.##") ?? "N/A",
            ["scoreDetailsJson"] = JsonSerializer.Serialize(request.ScoreDetails),
            ["currentDecision"] = request.CurrentDecision,
            ["hasHardConflict"] = request.HasHardConflict ? "是" : "否",
            ["reviewTrigger"] = request.ReviewTrigger ?? "证据不足，需要复核",
            ["evidenceSummaryJson"] = JsonSerializer.Serialize(request.EvidenceSummary),
            ["conflictSummaryJson"] = JsonSerializer.Serialize(request.ConflictSummary)
        });
    }

    private static string BuildSuggestionPrompt(string template, LlmSuggestionRequest request)
    {
        // 构建参考数据段
        string referenceInfo;
        if (!string.IsNullOrWhiteSpace(request.BestMatchProject))
        {
            var scorePct = request.BestMatchScore.HasValue
                ? $"{request.BestMatchScore.Value * 100:0.#}%"
                : "N/A";
            referenceInfo =
                $"（系统匹配到相似规格，得分 {scorePct}）\n" +
                $"项目：{request.BestMatchProject}\n" +
                $"规格：{request.BestMatchSpecification ?? "(无)"}\n" +
                $"验收标准：{request.BestMatchAcceptance ?? "(无)"}\n" +
                $"备注：{request.BestMatchRemark ?? "(无)"}";
        }
        else
        {
            referenceInfo = "无可参考的相似规格。只能从源文档的项目名称和规格描述中提取已有信息，严禁编造，信息不足时必须返回空字符串。";
        }

        return ApplyTemplate(template, new Dictionary<string, string>
        {
            ["sourceProject"] = request.SourceProject,
            ["sourceSpecification"] = request.SourceSpecification,
            ["referenceInfo"] = referenceInfo
        });
    }

    private static string BuildEntityResolutionPrompt(string template, LlmEntityResolutionRequest request)
    {
        return ApplyTemplate(template, new Dictionary<string, string>
        {
            ["sourceEntity"] = request.SourceEntity,
            ["candidateEntity"] = request.CandidateEntity,
            ["sourceText"] = request.SourceText,
            ["candidateText"] = request.CandidateText
        });
    }

    // ── LLM 调用 ──

    private async Task<string> GenerateWithFallbackAsync(
        string prompt,
        int? serviceId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var candidates = await _selector.GetCandidatesAsync(AiServicePurpose.Llm, serviceId);
        if (candidates.Count == 0)
            throw new AiServiceUnavailableException(errorMessage);

        var errors = new List<string>();
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
                return SanitizeLlmOutput(message.Content);
            }
            catch (Exception ex)
            {
                errors.Add($"{cfg.Name}: {ex.Message}");
                _logger.LogWarning(ex, "LLM 调用失败: {Name}", cfg.Name);
            }
        }

        throw new AiServiceUnavailableException(errorMessage, errors);
    }

    private async IAsyncEnumerable<string> GenerateStreamWithFallbackAsync(
        string prompt,
        int? serviceId,
        string errorMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var candidates = await _selector.GetCandidatesAsync(AiServicePurpose.Llm, serviceId);
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
                yield break;
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
            Temperature = 0.2
        };
    }

    // ── 模板管理 ──

    /// <summary>
    /// 获取或创建 Prompt 模板；如果 DB 中存储的是旧版默认模板则自动升级
    /// </summary>
    private async Task<PromptTemplateModel> GetOrCreateTemplateAsync(
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

        if (definition.LegacyDefaultContent != null &&
            string.Equals(content.Trim(), definition.LegacyDefaultContent.Trim(), StringComparison.Ordinal))
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

    // ── 工具方法 ──

    private static string ApplyTemplate(string template, Dictionary<string, string> values)
    {
        var result = template;
        foreach (var pair in values)
        {
            result = result.Replace($"{{{{{pair.Key}}}}}", pair.Value ?? string.Empty);
        }
        return result;
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
            _logger.LogWarning(ex, "LLM 输出 JSON 解析失败，原始内容: {Raw}", raw);
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

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
            return text.Substring(start, end - start + 1).Trim();

        return text.Trim();
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

    private static string? TryGetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop))
            return null;

        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
    }

    private static bool TryParseEntityRelation(string? value, out LlmEntityRelation relation)
    {
        relation = LlmEntityRelation.Unknown;
        return value?.Trim().ToLowerInvariant() switch
        {
            "same" => SetRelation(LlmEntityRelation.Same, out relation),
            "alias_same" => SetRelation(LlmEntityRelation.AliasSame, out relation),
            "conflict" => SetRelation(LlmEntityRelation.Conflict, out relation),
            "unknown" => SetRelation(LlmEntityRelation.Unknown, out relation),
            _ => false
        };
    }

    private static bool SetRelation(LlmEntityRelation value, out LlmEntityRelation relation)
    {
        relation = value;
        return true;
    }
}
