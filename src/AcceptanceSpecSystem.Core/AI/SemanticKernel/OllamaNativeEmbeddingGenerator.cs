using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcceptanceSpecSystem.Core.AI.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

/// <summary>
/// Ollama 原生 Embedding 适配器。直接调用 /api/embed，使 Embedding 与 Chat
/// 使用相同的模型驻留策略，并保留 Ollama 的加载/推理耗时指标。
/// </summary>
internal sealed class OllamaNativeEmbeddingGenerator
    : IEmbeddingGenerator<string, Embedding<float>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaNativeEmbeddingGenerator> _logger;
    private readonly string _baseUrl;
    private readonly string _modelId;
    private readonly string _keepAlive;
    private readonly int _serviceId;

    public OllamaNativeEmbeddingGenerator(
        AiServiceConfigModel config,
        HttpClient httpClient,
        string keepAlive,
        ILogger<OllamaNativeEmbeddingGenerator> logger)
    {
        _serviceId = config.Id;
        _modelId = string.IsNullOrWhiteSpace(config.EmbeddingModel)
            ? throw new InvalidOperationException("Embedding 模型未配置")
            : config.EmbeddingModel.Trim();
        _baseUrl = NormalizeOllamaBaseUrl(config.Endpoint);
        _keepAlive = string.IsNullOrWhiteSpace(keepAlive)
            ? SemanticKernelOptions.DefaultOllamaKeepAlive
            : keepAlive.Trim();
        _httpClient = httpClient;
        _logger = logger;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        var inputs = values.ToArray();
        if (inputs.Length == 0)
            return new GeneratedEmbeddings<Embedding<float>>([]);

        var request = new OllamaEmbedRequest
        {
            Model = _modelId,
            Input = inputs,
            KeepAlive = _keepAlive
        };
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/embed")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        var stopwatch = Stopwatch.StartNew();
        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, body);

        var payload = JsonSerializer.Deserialize<OllamaEmbedResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Ollama Embedding 返回内容为空");
        if (payload.Embeddings.Count != inputs.Length)
        {
            throw new InvalidOperationException(
                $"Ollama Embedding 返回数量不一致: expected={inputs.Length}, actual={payload.Embeddings.Count}");
        }

        var result = new GeneratedEmbeddings<Embedding<float>>(
            payload.Embeddings.Select(vector => new Embedding<float>(vector.ToArray())));

        _logger.LogInformation(
            "Ollama 原生 Embedding 完成: serviceId={ServiceId}, model={Model}, inputCount={InputCount}, dimension={Dimension}, elapsedMs={ElapsedMs}, totalDuration={TotalDuration}, loadDuration={LoadDuration}, promptEvalCount={PromptEvalCount}",
            _serviceId,
            _modelId,
            inputs.Length,
            payload.Embeddings.FirstOrDefault()?.Count ?? 0,
            stopwatch.ElapsedMilliseconds,
            payload.TotalDuration,
            payload.LoadDuration,
            payload.PromptEvalCount);

        return result;
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

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode)
            return;

        throw new InvalidOperationException(
            $"Ollama Embedding 返回 {((int)response.StatusCode)}: {TrimMessage(body)}");
    }

    private static string TrimMessage(string message)
    {
        const int maxLength = 300;
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        message = message.Trim();
        return message.Length <= maxLength ? message : $"{message[..maxLength]}...";
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class OllamaEmbedRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; init; } = string.Empty;

        [JsonPropertyName("input")]
        public string[] Input { get; init; } = [];

        [JsonPropertyName("keep_alive")]
        public string KeepAlive { get; init; } = string.Empty;
    }

    private sealed class OllamaEmbedResponse
    {
        [JsonPropertyName("embeddings")]
        public List<List<float>> Embeddings { get; init; } = [];

        [JsonPropertyName("total_duration")]
        public long? TotalDuration { get; init; }

        [JsonPropertyName("load_duration")]
        public long? LoadDuration { get; init; }

        [JsonPropertyName("prompt_eval_count")]
        public int? PromptEvalCount { get; init; }
    }
}
