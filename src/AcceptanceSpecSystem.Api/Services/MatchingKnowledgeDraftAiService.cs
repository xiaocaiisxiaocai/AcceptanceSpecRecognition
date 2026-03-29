using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.Matching.Services;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AcceptanceSpecSystem.Api.Services;

public interface IMatchingKnowledgeDraftAiService
{
    Task<IReadOnlyList<MatchingKnowledgeDraftCandidate>> GenerateAsync(
        MatchingKnowledgeDraftAiRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class MatchingKnowledgeDraftAiRequest
{
    public string Category { get; set; } = string.Empty;

    public string SourceText { get; set; } = string.Empty;

    public int? LlmServiceId { get; set; }
}

public sealed class MatchingKnowledgeDraftCandidate
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string EvidenceSnippet { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}

public sealed class MatchingKnowledgeDraftAiService : IMatchingKnowledgeDraftAiService
{
    private readonly IPromptTemplateProvider _promptTemplateProvider;
    private readonly IAiServiceSelector _selector;
    private readonly ISemanticKernelServiceFactory _factory;
    private readonly ILogger<MatchingKnowledgeDraftAiService> _logger;

    public MatchingKnowledgeDraftAiService(
        IPromptTemplateProvider promptTemplateProvider,
        IAiServiceSelector selector,
        ISemanticKernelServiceFactory factory,
        ILogger<MatchingKnowledgeDraftAiService> logger)
    {
        _promptTemplateProvider = promptTemplateProvider;
        _selector = selector;
        _factory = factory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MatchingKnowledgeDraftCandidate>> GenerateAsync(
        MatchingKnowledgeDraftAiRequest request,
        CancellationToken cancellationToken = default)
    {
        var definition = PromptTemplateCatalog.GetByScene(PromptTemplateScene.MatchingKnowledgeGenerate);
        var template = await _promptTemplateProvider.GetOrCreateSystemAsync(
            definition.Scene,
            definition.Name,
            definition.DisplayName,
            definition.DefaultContent,
            cancellationToken);

        var prompt = BuildPrompt(template.Content, request);
        var raw = await GenerateWithFallbackAsync(prompt, request.LlmServiceId, cancellationToken);

        if (!TryParse(raw, out var items))
        {
            _logger.LogWarning("匹配知识草稿 LLM 输出解析失败: {Raw}", raw);
            return [];
        }

        return items;
    }

    private static string BuildPrompt(string template, MatchingKnowledgeDraftAiRequest request)
    {
        var categoryDescription = request.Category switch
        {
            MatchingKnowledgeDraftGenerationService.CategoryEntityAliases => "实体别名：输出“别名 -> 标准实体”，例如 Panasonic -> 松下。",
            MatchingKnowledgeDraftGenerationService.CategoryUnitAliases => "单位规则：只输出单位别名映射，不允许输出倍率或换算系数，例如 公分 -> cm。",
            MatchingKnowledgeDraftGenerationService.CategoryFieldAliases => "字段别名：输出业务字段别名到标准字段的映射，例如 宽尺寸 -> 宽度。",
            MatchingKnowledgeDraftGenerationService.CategoryConflictPairs => "冲突词对：输出明确互斥的左右词，例如 正转 / 反转。",
            _ => "输出与当前分类对应的候选项。"
        };

        return template
            .Replace("{{category}}", request.Category)
            .Replace("{{categoryDescription}}", categoryDescription)
            .Replace("{{sourceText}}", request.SourceText);
    }

    private async Task<string> GenerateWithFallbackAsync(
        string prompt,
        int? serviceId,
        CancellationToken cancellationToken)
    {
        var candidates = await _selector.GetCandidatesAsync(AiServicePurpose.Llm, serviceId, cancellationToken);
        if (candidates.Count == 0)
        {
            throw new AiServiceUnavailableException("LLM 服务不可用");
        }

        var errors = new List<string>();
        foreach (var cfg in candidates)
        {
            try
            {
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
                _logger.LogWarning(ex, "匹配知识草稿生成调用失败: {Name}", cfg.Name);
            }
        }

        throw new AiServiceUnavailableException("LLM 服务不可用", errors);
    }

    private static OpenAIPromptExecutionSettings CreatePromptExecutionSettings(AiServiceConfigModel config)
    {
        return new OpenAIPromptExecutionSettings
        {
            Temperature = 0.2
        };
    }

    private static bool TryParse(string raw, out IReadOnlyList<MatchingKnowledgeDraftCandidate> items)
    {
        items = [];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            var jsonText = ExtractJson(SanitizeLlmOutput(raw));
            if (string.IsNullOrWhiteSpace(jsonText))
            {
                return false;
            }

            using var doc = JsonDocument.Parse(jsonText);
            if (!doc.RootElement.TryGetProperty("items", out var array) || array.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var result = new List<MatchingKnowledgeDraftCandidate>();
            foreach (var item in array.EnumerateArray())
            {
                var key = TryGetString(item, "key")?.Trim();
                var value = TryGetString(item, "value")?.Trim();
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                result.Add(new MatchingKnowledgeDraftCandidate
                {
                    Key = key,
                    Value = value,
                    EvidenceSnippet = TryGetString(item, "evidenceSnippet")?.Trim() ?? string.Empty,
                    Reason = TryGetString(item, "reason")?.Trim() ?? string.Empty
                });
            }

            items = result;
            return result.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static string ExtractJson(string text)
    {
        text = text.Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return string.Empty;
        }

        return text[start..(end + 1)];
    }

    private static string SanitizeLlmOutput(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var text = raw.Replace("<think>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</think>", string.Empty, StringComparison.OrdinalIgnoreCase);
        return text.Trim();
    }
}
