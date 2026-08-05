using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcceptanceSpecSystem.Core.AI.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Services;

namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

/// <summary>
/// Ollama LLM 原生 Chat 服务，直接调用 /api/chat，确保 think=false 等原生参数生效。
/// </summary>
internal sealed class OllamaNativeChatCompletionService : IChatCompletionService, IDisposable
{
    private const string KeepAlive = "30m";

    /// <summary>
    /// 默认随机种子。temperature=0 仍可能因 GPU 浮点累加顺序在临界样本上摆动，
    /// 固定 seed 进一步锁定采样起点以提升裁决结果的可复现性。
    /// </summary>
    private const int DefaultSeed = 42;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaNativeChatCompletionService> _logger;
    private readonly string _baseUrl;
    private readonly string _modelId;
    private readonly bool _disableThinking;

    public OllamaNativeChatCompletionService(
        AiServiceConfigModel config,
        HttpClient httpClient,
        ILogger<OllamaNativeChatCompletionService> logger)
    {
        _modelId = string.IsNullOrWhiteSpace(config.LlmModel)
            ? throw new InvalidOperationException("LLM 模型未配置")
            : config.LlmModel.Trim();

        _baseUrl = NormalizeOllamaBaseUrl(config.Endpoint);
        _disableThinking = config.DisableThinking;
        _httpClient = httpClient;
        _logger = logger;

        Attributes = new Dictionary<string, object?>
        {
            ["service"] = "ollama-native-chat",
            ["endpoint"] = _baseUrl,
            ["model_id"] = _modelId,
            ["disable_thinking"] = _disableThinking
        };
    }

    public IReadOnlyDictionary<string, object?> Attributes { get; }

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(chatHistory, stream: false, executionSettings);
        using var httpRequest = CreateHttpRequestMessage(request);
        using var requestCts = CreateRequestCancellationTokenSource(cancellationToken);
        var stopwatch = Stopwatch.StartNew();

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, requestCts.Token);
        var body = await response.Content.ReadAsStringAsync(requestCts.Token);
        EnsureSuccess(response, body);

        var payload = JsonSerializer.Deserialize<OllamaChatResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Ollama 返回内容为空");

        LogTiming(payload, stopwatch.ElapsedMilliseconds);

        return
        [
            new ChatMessageContent(
                AuthorRole.Assistant,
                payload.Message?.Content ?? string.Empty,
                _modelId,
                payload,
                Encoding.UTF8,
                BuildMetadata(payload))
        ];
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(chatHistory, stream: true, executionSettings);
        using var httpRequest = CreateHttpRequestMessage(request);
        using var requestCts = CreateRequestCancellationTokenSource(cancellationToken);
        var stopwatch = Stopwatch.StartNew();

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, requestCts.Token);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(requestCts.Token);
            EnsureSuccess(response, body);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(requestCts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (true)
        {
            var line = await reader.ReadLineAsync(requestCts.Token);
            if (line == null)
                yield break;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var payload = JsonSerializer.Deserialize<OllamaChatResponse>(line, JsonOptions)
                ?? throw new InvalidOperationException("Ollama 流式返回内容为空");

            if (!string.IsNullOrEmpty(payload.Message?.Content))
            {
                yield return new StreamingChatMessageContent(
                    AuthorRole.Assistant,
                    payload.Message.Content,
                    payload,
                    0,
                    _modelId,
                    Encoding.UTF8,
                    BuildMetadata(payload));
            }

            if (payload.Done)
            {
                LogTiming(payload, stopwatch.ElapsedMilliseconds);
                yield break;
            }
        }
    }

    private HttpRequestMessage CreateHttpRequestMessage(OllamaChatRequest request)
    {
        return new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
    }

    private OllamaChatRequest BuildRequest(ChatHistory chatHistory, bool stream, PromptExecutionSettings? executionSettings)
    {
        var messages = chatHistory
            .Select(ToOllamaMessage)
            .Where(static message => !string.IsNullOrWhiteSpace(message.Content))
            .ToList();

        if (messages.Count == 0)
            throw new InvalidOperationException("聊天内容不能为空");

        return new OllamaChatRequest
        {
            Model = _modelId,
            Stream = stream,
            KeepAlive = KeepAlive,
            Think = _disableThinking ? false : null,
            Messages = messages,
            Options = BuildOptions(executionSettings),
            Format = BuildFormat(executionSettings, stream)
        };
    }

    private static JsonElement? BuildFormat(
        PromptExecutionSettings? executionSettings,
        bool stream)
    {
        if (stream ||
            executionSettings is not Microsoft.SemanticKernel.Connectors.OpenAI.OpenAIPromptExecutionSettings openAi)
        {
            return null;
        }

        return openAi.ResponseFormat switch
        {
            Type responseType => AIJsonUtilities.TransformSchema(
                AIJsonUtilities.CreateJsonSchema(
                    responseType,
                    serializerOptions: JsonOptions),
                new AIJsonSchemaTransformOptions
                {
                    DisallowAdditionalProperties = true,
                    RequireAllProperties = true
                }),
            string value when string.Equals(value, "json_object", StringComparison.OrdinalIgnoreCase) =>
                JsonSerializer.SerializeToElement("json", JsonOptions),
            _ => null
        };
    }

    /// <summary>
    /// 构造 Ollama 采样选项。默认走确定性配置（temperature=0 + 固定 seed），
    /// 保证同一输入在同一模型上的裁决结果可复现；上层可通过 PromptExecutionSettings 覆盖。
    /// </summary>
    private static OllamaOptions BuildOptions(PromptExecutionSettings? executionSettings)
    {
        // 默认：贪心解码 + 固定随机种子，最大化可复现性
        var temperature = 0d;
        var seed = DefaultSeed;
        double? topP = null;

        if (executionSettings is Microsoft.SemanticKernel.Connectors.OpenAI.OpenAIPromptExecutionSettings openAi)
        {
            if (openAi.Temperature.HasValue)
                temperature = openAi.Temperature.Value;
            if (openAi.Seed.HasValue)
                seed = (int)openAi.Seed.Value;
            if (openAi.TopP.HasValue)
                topP = openAi.TopP.Value;
        }

        return new OllamaOptions
        {
            Temperature = temperature,
            Seed = seed,
            TopP = topP
        };
    }

    private static OllamaMessage ToOllamaMessage(ChatMessageContent message)
    {
        var role = message.Role == AuthorRole.System || message.Role == AuthorRole.Developer
            ? "system"
            : message.Role == AuthorRole.Assistant
                ? "assistant"
                : message.Role == AuthorRole.Tool
                    ? "tool"
                    : "user";

        return new OllamaMessage
        {
            Role = role,
            Content = message.Content ?? string.Empty
        };
    }

    private static IReadOnlyDictionary<string, object?> BuildMetadata(OllamaChatResponse payload)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["done"] = payload.Done,
            ["done_reason"] = payload.DoneReason
        };

        if (payload.TotalDuration.HasValue)
            metadata["total_duration"] = payload.TotalDuration.Value;
        if (payload.LoadDuration.HasValue)
            metadata["load_duration"] = payload.LoadDuration.Value;
        if (payload.PromptEvalDuration.HasValue)
            metadata["prompt_eval_duration"] = payload.PromptEvalDuration.Value;
        if (payload.EvalDuration.HasValue)
            metadata["eval_duration"] = payload.EvalDuration.Value;
        if (payload.PromptEvalCount.HasValue)
            metadata["prompt_eval_count"] = payload.PromptEvalCount.Value;
        if (payload.EvalCount.HasValue)
            metadata["eval_count"] = payload.EvalCount.Value;

        return metadata;
    }

    private void LogTiming(OllamaChatResponse payload, long elapsedMs)
    {
        _logger.LogInformation(
            "Ollama 原生聊天完成: model={Model}, elapsedMs={ElapsedMs}, totalDuration={TotalDuration}, loadDuration={LoadDuration}, evalDuration={EvalDuration}",
            _modelId,
            elapsedMs,
            payload.TotalDuration,
            payload.LoadDuration,
            payload.EvalDuration);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode)
            return;

        throw new InvalidOperationException($"Ollama 返回 {((int)response.StatusCode)}: {TrimMessage(body)}");
    }

    private static CancellationTokenSource CreateRequestCancellationTokenSource(CancellationToken cancellationToken)
    {
        // 慢模型可能运行很久，这里不再做固定硬超时，真正取消交给外层请求链路。
        return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    private static string NormalizeOllamaBaseUrl(string? endpoint)
    {
        var value = AiEndpointNormalizer.NormalizeRequiredEndpoint(
            endpoint,
            "Ollama Endpoint").TrimEnd('/');
        if (value.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            value = value[..^4];
        if (value.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            value = value[..^3];
        return value.TrimEnd('/');
    }

    private static string TrimMessage(string message)
    {
        const int maxLength = 300;
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        message = message.Trim();
        return message.Length <= maxLength ? message : $"{message[..maxLength]}...";
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private sealed class OllamaChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; init; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<OllamaMessage> Messages { get; init; } = [];

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }

        [JsonPropertyName("think")]
        public bool? Think { get; init; }

        [JsonPropertyName("keep_alive")]
        public string KeepAlive { get; init; } = string.Empty;

        [JsonPropertyName("options")]
        public OllamaOptions? Options { get; init; }

        [JsonPropertyName("format")]
        public JsonElement? Format { get; init; }
    }

    /// <summary>
    /// Ollama /api/chat 的采样选项。对应 Ollama API 的 options 对象。
    /// </summary>
    private sealed class OllamaOptions
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; init; }

        [JsonPropertyName("seed")]
        public int Seed { get; init; }

        [JsonPropertyName("top_p")]
        public double? TopP { get; init; }
    }

    private sealed class OllamaMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; init; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; init; } = string.Empty;
    }

    private sealed class OllamaChatResponse
    {
        [JsonPropertyName("message")]
        public OllamaMessage? Message { get; init; }

        [JsonPropertyName("done")]
        public bool Done { get; init; }

        [JsonPropertyName("done_reason")]
        public string? DoneReason { get; init; }

        [JsonPropertyName("total_duration")]
        public long? TotalDuration { get; init; }

        [JsonPropertyName("load_duration")]
        public long? LoadDuration { get; init; }

        [JsonPropertyName("prompt_eval_duration")]
        public long? PromptEvalDuration { get; init; }

        [JsonPropertyName("eval_duration")]
        public long? EvalDuration { get; init; }

        [JsonPropertyName("prompt_eval_count")]
        public int? PromptEvalCount { get; init; }

        [JsonPropertyName("eval_count")]
        public int? EvalCount { get; init; }
    }
}
