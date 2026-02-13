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

    private const string DefaultReviewTemplate =
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

    private const string DefaultSuggestTemplate =
        "你是验收规格助手。请根据“源项目/规格”生成验收标准与备注建议。\n" +
        "仅返回严格 JSON：\n" +
        "{\"acceptance\":\"...\",\"remark\":\"...\",\"reason\":\"...\"}\n" +
        "要求：\n" +
        "- 用中文\n" +
        "- 内容简洁、可执行\n" +
        "- 不确定时可返回空字符串\n" +
        "源项目：{{sourceProject}}\n" +
        "源规格：{{sourceSpecification}}";

    private readonly IUnitOfWork _unitOfWork;
    private readonly AiServiceSelector _selector;
    private readonly ISemanticKernelServiceFactory _factory;
    private readonly ILogger<LlmMatchingAssistService> _logger;

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

        var template = await GetOrCreateTemplateAsync(ReviewTemplateName, DefaultReviewTemplate);
        var prompt = ApplyTemplate(template.Content, new Dictionary<string, string>
        {
            ["sourceProject"] = request.SourceProject,
            ["sourceSpecification"] = request.SourceSpecification,
            ["bestMatchProject"] = request.BestMatchProject,
            ["bestMatchSpecification"] = request.BestMatchSpecification,
            ["bestMatchAcceptance"] = request.BestMatchAcceptance ?? string.Empty,
            ["baseScore"] = request.BaseScore?.ToString("0.##") ?? string.Empty,
            ["scoreDetailsJson"] = JsonSerializer.Serialize(request.ScoreDetails)
        });

        var raw = await GenerateWithFallbackAsync(prompt, request.LlmServiceId, "LLM 复核失败", cancellationToken);
        return TryParseReviewResult(raw, out var result) ? result : null;
    }

    public async IAsyncEnumerable<string> ReviewStreamAsync(
        LlmReviewRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BestMatchProject) &&
            string.IsNullOrWhiteSpace(request.BestMatchSpecification))
            yield break;

        var template = await GetOrCreateTemplateAsync(ReviewTemplateName, DefaultReviewTemplate);
        var prompt = ApplyTemplate(template.Content, new Dictionary<string, string>
        {
            ["sourceProject"] = request.SourceProject,
            ["sourceSpecification"] = request.SourceSpecification,
            ["bestMatchProject"] = request.BestMatchProject,
            ["bestMatchSpecification"] = request.BestMatchSpecification,
            ["bestMatchAcceptance"] = request.BestMatchAcceptance ?? string.Empty,
            ["baseScore"] = request.BaseScore?.ToString("0.##") ?? string.Empty,
            ["scoreDetailsJson"] = JsonSerializer.Serialize(request.ScoreDetails)
        });

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
        var template = await GetOrCreateTemplateAsync(SuggestTemplateName, DefaultSuggestTemplate);
        var prompt = ApplyTemplate(template.Content, new Dictionary<string, string>
        {
            ["sourceProject"] = request.SourceProject,
            ["sourceSpecification"] = request.SourceSpecification
        });

        var raw = await GenerateWithFallbackAsync(prompt, request.LlmServiceId, "LLM 生成失败", cancellationToken);
        return TryParseSuggestionResult(raw, out var result) ? result : null;
    }

    public async IAsyncEnumerable<string> GenerateSuggestionStreamAsync(
        LlmSuggestionRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var template = await GetOrCreateTemplateAsync(SuggestTemplateName, DefaultSuggestTemplate);
        var prompt = ApplyTemplate(template.Content, new Dictionary<string, string>
        {
            ["sourceProject"] = request.SourceProject,
            ["sourceSpecification"] = request.SourceSpecification
        });

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
                var chat = _factory.CreateChatCompletionService(cfg);
                var history = new ChatHistory();
                history.AddUserMessage(prompt);
                var settings = new OpenAIPromptExecutionSettings
                {
                    Temperature = 0.2
                };

                var message = await chat.GetChatMessageContentAsync(history, settings, cancellationToken: cancellationToken);
                return message.Content ?? string.Empty;
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
            var produced = false;
            var channel = System.Threading.Channels.Channel.CreateUnbounded<string>();

            _ = Task.Run(async () =>
            {
                try
                {
                    var chat = _factory.CreateChatCompletionService(cfg);
                    var history = new ChatHistory();
                    history.AddUserMessage(prompt);
                    var settings = new OpenAIPromptExecutionSettings
                    {
                        Temperature = 0.2
                    };

                    await foreach (var chunk in chat.GetStreamingChatMessageContentsAsync(history, settings, cancellationToken: cancellationToken))
                    {
                        if (!string.IsNullOrWhiteSpace(chunk.Content))
                        {
                            await channel.Writer.WriteAsync(chunk.Content!, cancellationToken);
                        }
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

    private async Task<PromptTemplate> GetOrCreateTemplateAsync(string name, string defaultContent)
    {
        var template = await _unitOfWork.PromptTemplates.GetByNameAsync(name);
        if (template != null)
            return template;

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

        var text = ExtractJson(raw);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            doc = JsonDocument.Parse(text);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM 输出 JSON 解析失败，原始长度: {Length}", raw.Length);
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
