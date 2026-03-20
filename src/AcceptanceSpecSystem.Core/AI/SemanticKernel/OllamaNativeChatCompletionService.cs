using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Services;

namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

/// <summary>
/// Ollama LLM 原生 Chat 服务，直接调用 /api/chat，确保 think=false 等原生参数生效。
/// </summary>
internal sealed class OllamaNativeChatCompletionService : IChatCompletionService
{
    private const string KeepAlive = "30m";
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
        AiServiceConfig config,
        HttpClient httpClient,
        ILogger<OllamaNativeChatCompletionService> logger)
    {
        _modelId = string.IsNullOrWhiteSpace(config.LlmModel)
            ? throw new InvalidOperationException("LLM 模型未配置")
            : config.LlmModel.Trim();

        _baseUrl = NormalizeOllamaBaseUrl(config.Endpoint);
        _disableThinking = config.DisableThinking;
        _httpClient = httpClient;
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
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
        var request = BuildRequest(chatHistory, stream: false);
        using var httpRequest = CreateHttpRequestMessage(request);
        var stopwatch = Stopwatch.StartNew();

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
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
        var request = BuildRequest(chatHistory, stream: true);
        using var httpRequest = CreateHttpRequestMessage(request);
        var stopwatch = Stopwatch.StartNew();

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, body);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
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

    private OllamaChatRequest BuildRequest(ChatHistory chatHistory, bool stream)
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
            Messages = messages
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

    private static string NormalizeOllamaBaseUrl(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("Ollama Endpoint 未配置");

        var value = endpoint.Trim().TrimEnd('/');
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
