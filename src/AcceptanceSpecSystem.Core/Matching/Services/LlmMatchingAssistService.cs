using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AcceptanceSpecSystem.Core.Matching.Services;

/// <summary>
/// LLM 匹配辅助服务（复核 + 生成建议）
/// </summary>
public class LlmMatchingAssistService : ILlmReviewService, ILlmSuggestionService
{
    private const string ReviewTemplateName = "matching-review";
    private const string SuggestTemplateName = "matching-generate";

    // ── V1 旧版模板内容（用于自动升级检测） ──
    private const string OldReviewTemplateV1 =
        "你是验收规格匹配评审助手。给定源项目/规格与系统最佳匹配结果，请复核评分并说明原因。\n" +
        "仅返回严格 JSON：\n" +
        "{\"score\":0,\"reason\":\"...\",\"commentary\":\"...\"}\n" +
        "要求：\n" +
        "- score 取值 0~100\n" +
        "- reason 解释为什么评分高/低（重点说明低分原因）\n" +
        "- commentary 简短描述评论过程（对比了哪些关键信息）\n" +
        "源项目：{{sourceProject}}\n" +
        "源规格：{{sourceSpecification}}\n" +
        "最佳匹配项目：{{bestMatchProject}}\n" +
        "最佳匹配规格：{{bestMatchSpecification}}\n" +
        "验收标准：{{bestMatchAcceptance}}\n" +
        "基础得分：{{baseScore}}\n" +
        "得分明细(JSON)：{{scoreDetailsJson}}";

    private const string OldSuggestTemplateV1 =
        "你是验收规格助手。请根据\u201c源项目/规格\u201d生成验收标准与备注建议。\n" +
        "仅返回严格 JSON：\n" +
        "{\"acceptance\":\"...\",\"remark\":\"...\",\"reason\":\"...\"}\n" +
        "要求：\n" +
        "- 用中文\n" +
        "- 内容简洁、可执行\n" +
        "- 不确定时可返回空字符串\n" +
        "源项目：{{sourceProject}}\n" +
        "源规格：{{sourceSpecification}}";

    // ── V2 新版默认模板 ──
    private const string DefaultReviewTemplate =
        "你是验收规格匹配复核助手。系统通过 Embedding 向量相似度为源文档找到了最佳匹配的验收规格，请复核此匹配是否正确。\n\n" +
        "【任务】对比\"源文档\"与\"系统匹配结果\"的项目名称和规格描述，判断两者是否指向同一个验收项。\n\n" +
        "【核心约束】\n" +
        "- 仅基于下方提供的数据进行评判，严禁引入外部知识或自行推测\n" +
        "- 只对比项目名称和规格描述的语义相似性，不要评价验收标准的合理性\n\n" +
        "【评分标准】\n" +
        "- 90~100：项目和规格语义完全一致（允许星号、空格、繁简体等格式差异）\n" +
        "- 70~89：语义高度相关但有细微差异（如单位不同、数值范围略有不同）\n" +
        "- 40~69：有一定关联但不能确认为同一规格\n" +
        "- 0~39：明显不匹配\n\n" +
        "【源文档】\n" +
        "项目：{{sourceProject}}\n" +
        "规格：{{sourceSpecification}}\n\n" +
        "【系统匹配结果】\n" +
        "项目：{{bestMatchProject}}\n" +
        "规格：{{bestMatchSpecification}}\n" +
        "验收标准：{{bestMatchAcceptance}}\n" +
        "备注：{{bestMatchRemark}}\n\n" +
        "【Embedding 基础得分】{{baseScore}}（满分100，越高越相似）\n" +
        "【得分明细】{{scoreDetailsJson}}\n\n" +
        "仅返回严格 JSON：\n" +
        "{\"score\":0,\"reason\":\"...\",\"commentary\":\"...\"}\n" +
        "要求：\n" +
        "- score 取值 0~100\n" +
        "- reason 解释评分理由（重点说明项目/规格的语义对比结论）\n" +
        "- commentary 简述对比了哪些关键信息";

    private const string DefaultSuggestTemplate =
        "你是验收规格助手。请根据源文档信息整理验收标准与备注。\n\n" +
        "【源文档】\n" +
        "项目：{{sourceProject}}\n" +
        "规格：{{sourceSpecification}}\n\n" +
        "【参考数据】\n" +
        "{{referenceInfo}}\n\n" +
        "【核心约束 - 必须严格遵守】\n" +
        "1. 严禁编造、虚构、猜测任何数值、标准、检验方法或技术参数\n" +
        "2. 只能从源文档的\"项目\"和\"规格\"字段中提取已明确写出的信息进行整理\n" +
        "3. 如有参考数据，仅可参考其格式和措辞风格，数值必须来自源文档\n" +
        "4. 如果源文档中没有明确的具体数值或可执行的验收要求，acceptance 和 remark 必须返回空字符串\n" +
        "5. 宁可返回空字符串，也绝不编造内容\n\n" +
        "【输出格式】\n" +
        "仅返回严格 JSON：\n" +
        "{\"acceptance\":\"...\",\"remark\":\"...\",\"reason\":\"...\"}\n" +
        "- acceptance：从源文档提取整理的验收标准，信息不足时返回空字符串\n" +
        "- remark：从源文档提取整理的备注，信息不足时返回空字符串\n" +
        "- reason：说明生成依据，或说明为何返回空字符串";

    private readonly IUnitOfWork _unitOfWork;
    private readonly AiServiceSelector _selector;
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
        IUnitOfWork unitOfWork,
        AiServiceSelector selector,
        ISemanticKernelServiceFactory factory,
        ILogger<LlmMatchingAssistService> logger)
    {
        _unitOfWork = unitOfWork;
        _selector = selector;
        _factory = factory;
        _logger = logger;
    }

    public async Task<LlmReviewResult?> ReviewAsync(LlmReviewRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BestMatchProject) &&
            string.IsNullOrWhiteSpace(request.BestMatchSpecification))
            return null;

        var template = await GetOrCreateTemplateAsync(ReviewTemplateName, DefaultReviewTemplate, OldReviewTemplateV1);
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

        var template = await GetOrCreateTemplateAsync(ReviewTemplateName, DefaultReviewTemplate, OldReviewTemplateV1);
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
        var template = await GetOrCreateTemplateAsync(SuggestTemplateName, DefaultSuggestTemplate, OldSuggestTemplateV1);
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
        var template = await GetOrCreateTemplateAsync(SuggestTemplateName, DefaultSuggestTemplate, OldSuggestTemplateV1);
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
            ["scoreDetailsJson"] = JsonSerializer.Serialize(request.ScoreDetails)
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

    private static OpenAIPromptExecutionSettings CreatePromptExecutionSettings(AiServiceConfig config)
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
    private async Task<PromptTemplate> GetOrCreateTemplateAsync(string name, string defaultContent, string? oldContent = null)
    {
        var template = await _unitOfWork.PromptTemplates.GetByNameAsync(name);
        if (template != null)
        {
            // 自动升级：DB 模板内容与旧版默认一致时，更新为新版
            if (oldContent != null && template.Content.Trim() == oldContent.Trim())
            {
                _logger.LogInformation("自动升级 LLM Prompt 模板 [{Name}]：检测到旧版默认内容，更新为新版", name);
                template.Content = defaultContent;
                await _unitOfWork.SaveChangesAsync();
            }
            return template;
        }

        template = new PromptTemplate
        {
            Name = name,
            Content = defaultContent,
            IsDefault = false,
            CreatedAt = DateTime.Now
        };

        await _unitOfWork.PromptTemplates.AddAsync(template);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("创建默认 LLM Prompt 模板: {Name}", name);
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
}
