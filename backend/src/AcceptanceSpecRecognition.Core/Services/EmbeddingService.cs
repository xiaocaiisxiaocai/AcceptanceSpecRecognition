using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecRecognition.Core.Services;

/// <summary>
/// 向量嵌入服务实现 - 支持OpenAI兼容API，包含重试机制和缓存
/// </summary>
public class EmbeddingService : IEmbeddingService
{
    private readonly IConfigManager _configManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICacheService _cacheService;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(
        IConfigManager configManager,
        IHttpClientFactory httpClientFactory,
        ICacheService cacheService,
        ILogger<EmbeddingService> logger)
    {
        _configManager = configManager;
        _httpClientFactory = httpClientFactory;
        _cacheService = cacheService;
        _logger = logger;
    }

    /// <summary>
    /// 获取当前配置的模型名称
    /// </summary>
    private string CurrentModel => _configManager.GetAll().Embedding.Model;

    /// <summary>
    /// 获取当前配置的向量维度
    /// </summary>
    private int Dimension => _configManager.GetAll().Embedding.Dimension;

    public async Task<float[]> EmbedAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new float[Dimension];
        }

        // 使用缓存包装 (P0-2)
        var result = await _cacheService.GetOrCreateEmbeddingAsync(text, async () =>
        {
            return await EmbedAsyncInternal(text);
        });

        return result ?? new float[Dimension];
    }

    /// <summary>
    /// 内部实现：实际调用 API 生成向量
    /// </summary>
    private async Task<float[]> EmbedAsyncInternal(string text)
    {
        var config = _configManager.GetAll();
        var embeddingConfig = config.Embedding;

        // 获取API Key：优先使用配置文件，其次使用环境变量
        var apiKey = !string.IsNullOrEmpty(embeddingConfig.ApiKey)
            ? embeddingConfig.ApiKey
            : Environment.GetEnvironmentVariable("OPENAI_API_KEY")
              ?? Environment.GetEnvironmentVariable("EMBEDDING_API_KEY");

        // 如果没有配置API Key，返回模拟向量（用于测试）
        if (string.IsNullOrEmpty(apiKey))
        {
            return GenerateMockEmbedding(text);
        }

        // 使用重试机制调用API
        return await CallEmbeddingApiWithRetryAsync(text, embeddingConfig, apiKey);
    }

    /// <summary>
    /// 带重试机制的 Embedding API 调用
    /// </summary>
    private async Task<float[]> CallEmbeddingApiWithRetryAsync(string text, EmbeddingConfig embeddingConfig, string apiKey)
    {
        var config = _configManager.GetAll();
        var maxRetries = config.Batch.ApiMaxRetries;
        var baseDelayMs = config.Batch.ApiRetryBaseDelayMs;

        var baseUrl = embeddingConfig.BaseUrl.TrimEnd('/');
        var apiUrl = $"{baseUrl}/embeddings";
        Exception? lastException = null;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient("EmbeddingClient");

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                requestMessage.Headers.Add("Authorization", $"Bearer {apiKey}");
                requestMessage.Content = JsonContent.Create(new
                {
                    model = CurrentModel,
                    input = text
                });

                var response = await httpClient.SendAsync(requestMessage);

                // 对于可重试的状态码进行重试
                if (IsRetryableStatusCode(response.StatusCode) && attempt < maxRetries)
                {
                    var delayMs = baseDelayMs * (int)Math.Pow(2, attempt);
                    _logger.LogWarning("Embedding API 返回 {StatusCode}，第 {Attempt} 次重试...",
                        response.StatusCode, attempt + 1);
                    await Task.Delay(delayMs);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<OpenAIEmbeddingResponse>(new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var embedding = result?.Data?.FirstOrDefault()?.Embedding;
                if (embedding != null && embedding.Length > 0)
                {
                    return embedding;
                }

                return GenerateMockEmbedding(text);
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                lastException = ex;
                var delayMs = baseDelayMs * (int)Math.Pow(2, attempt);
                _logger.LogWarning(ex, "Embedding API 网络错误，第 {Attempt} 次重试...", attempt + 1);
                await Task.Delay(delayMs);
            }
            catch (TaskCanceledException ex) when (attempt < maxRetries)
            {
                lastException = ex;
                var delayMs = baseDelayMs * (int)Math.Pow(2, attempt);
                _logger.LogWarning("Embedding API 超时，第 {Attempt} 次重试...", attempt + 1);
                await Task.Delay(delayMs);
            }
            catch (Exception ex)
            {
                lastException = ex;
                break;
            }
        }

        _logger.LogWarning(lastException, "Embedding API 调用失败（已重试 {MaxRetries} 次），使用模拟向量", maxRetries);
        return GenerateMockEmbedding(text);
    }

    /// <summary>
    /// 判断是否为可重试的 HTTP 状态码
    /// </summary>
    private static bool IsRetryableStatusCode(System.Net.HttpStatusCode statusCode)
    {
        return statusCode == System.Net.HttpStatusCode.TooManyRequests ||  // 429
               statusCode == System.Net.HttpStatusCode.ServiceUnavailable || // 503
               statusCode == System.Net.HttpStatusCode.GatewayTimeout ||     // 504
               statusCode == System.Net.HttpStatusCode.InternalServerError;  // 500
    }

    public async Task<List<float[]>> EmbedBatchAsync(List<string> texts)
    {
        if (texts.Count == 0) return new List<float[]>();

        var config = _configManager.GetAll();
        var embeddingConfig = config.Embedding;

        // 获取API Key
        var apiKey = !string.IsNullOrEmpty(embeddingConfig.ApiKey)
            ? embeddingConfig.ApiKey
            : Environment.GetEnvironmentVariable("OPENAI_API_KEY")
              ?? Environment.GetEnvironmentVariable("EMBEDDING_API_KEY");

        // 1. 分离缓存命中和未命中的文本
        var results = new float[texts.Count][];
        var uncachedTexts = new List<(int index, string text)>();

        for (int i = 0; i < texts.Count; i++)
        {
            var text = texts[i];
            if (string.IsNullOrEmpty(text))
            {
                results[i] = new float[Dimension];
                continue;
            }

            // 尝试从缓存获取
            var cached = await _cacheService.GetOrCreateEmbeddingAsync(text, () => Task.FromResult<float[]>(null!));
            if (cached != null)
            {
                results[i] = cached;
            }
            else
            {
                uncachedTexts.Add((i, text));
            }
        }

        // 2. 批量调用 API 获取未缓存的向量
        if (uncachedTexts.Count > 0 && !string.IsNullOrEmpty(apiKey))
        {
            var batchSize = embeddingConfig.BatchSize;

            // 分批处理（OpenAI 限制每批最多 2048 条）
            for (int batch = 0; batch < uncachedTexts.Count; batch += batchSize)
            {
                var batchItems = uncachedTexts.Skip(batch).Take(batchSize).ToList();
                var batchTexts = batchItems.Select(x => x.text).ToList();

                try
                {
                    var batchResults = await CallBatchEmbeddingApiAsync(batchTexts, embeddingConfig, apiKey);

                    // 将结果放入对应位置并写入缓存
                    for (int j = 0; j < batchItems.Count && j < batchResults.Count; j++)
                    {
                        var (index, text) = batchItems[j];
                        results[index] = batchResults[j];

                        // 写入缓存
                        await _cacheService.GetOrCreateEmbeddingAsync(text, () => Task.FromResult(batchResults[j]));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "批量 Embedding API 调用失败，回退到逐个调用");

                    // 回退：逐个调用
                    foreach (var (index, text) in batchItems)
                    {
                        results[index] = await EmbedAsync(text);
                    }
                }
            }
        }
        else if (uncachedTexts.Count > 0)
        {
            // 无 API Key，使用模拟向量
            foreach (var (index, text) in uncachedTexts)
            {
                results[index] = GenerateMockEmbedding(text);
            }
        }

        return results.ToList();
    }

    /// <summary>
    /// 批量调用 Embedding API (P0-3)
    /// </summary>
    private async Task<List<float[]>> CallBatchEmbeddingApiAsync(List<string> texts, EmbeddingConfig embeddingConfig, string apiKey)
    {
        var config = _configManager.GetAll();
        var maxRetries = config.Batch.ApiMaxRetries;
        var baseDelayMs = config.Batch.ApiRetryBaseDelayMs;

        var baseUrl = embeddingConfig.BaseUrl.TrimEnd('/');
        var apiUrl = $"{baseUrl}/embeddings";

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient("EmbeddingClient");

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                requestMessage.Headers.Add("Authorization", $"Bearer {apiKey}");
                requestMessage.Content = JsonContent.Create(new
                {
                    model = CurrentModel,
                    input = texts  // 批量输入
                });

                var response = await httpClient.SendAsync(requestMessage);

                if (IsRetryableStatusCode(response.StatusCode) && attempt < maxRetries)
                {
                    var delayMs = baseDelayMs * (int)Math.Pow(2, attempt);
                    _logger.LogWarning("批量 Embedding API 返回 {StatusCode}，第 {Attempt} 次重试...",
                        response.StatusCode, attempt + 1);
                    await Task.Delay(delayMs);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<OpenAIEmbeddingResponse>(new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result?.Data != null && result.Data.Count > 0)
                {
                    // 按 index 排序确保顺序正确
                    var embeddings = result.Data
                        .OrderBy(d => d.Index)
                        .Select(d => d.Embedding ?? GenerateMockEmbedding(""))
                        .ToList();

                    return embeddings;
                }

                break;
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                var delayMs = baseDelayMs * (int)Math.Pow(2, attempt);
                _logger.LogWarning(ex, "批量 Embedding API 网络错误，第 {Attempt} 次重试...", attempt + 1);
                await Task.Delay(delayMs);
            }
            catch (TaskCanceledException) when (attempt < maxRetries)
            {
                var delayMs = baseDelayMs * (int)Math.Pow(2, attempt);
                _logger.LogWarning("批量 Embedding API 超时，第 {Attempt} 次重试...", attempt + 1);
                await Task.Delay(delayMs);
            }
        }

        // 失败时返回模拟向量
        return texts.Select(t => GenerateMockEmbedding(t)).ToList();
    }

    public float CosineSimilarity(float[] vec1, float[] vec2)
    {
        if (vec1.Length != vec2.Length || vec1.Length == 0)
        {
            return 0;
        }

        float dotProduct = 0;
        float norm1 = 0;
        float norm2 = 0;

        for (int i = 0; i < vec1.Length; i++)
        {
            dotProduct += vec1[i] * vec2[i];
            norm1 += vec1[i] * vec1[i];
            norm2 += vec2[i] * vec2[i];
        }

        if (norm1 == 0 || norm2 == 0)
        {
            return 0;
        }

        return dotProduct / (MathF.Sqrt(norm1) * MathF.Sqrt(norm2));
    }

    public ModelInfo GetModelInfo()
    {
        return new ModelInfo
        {
            Name = CurrentModel,
            Dimension = Dimension,
            Provider = "openai"
        };
    }

    public void SetModel(string modelName)
    {
        // 通过配置管理器更新模型设置
        var config = _configManager.GetAll();
        config.Embedding.Model = modelName;
        // 根据模型更新维度
        config.Embedding.Dimension = modelName switch
        {
            "text-embedding-3-small" => 1536,
            "text-embedding-3-large" => 3072,
            _ => 1536
        };
        _configManager.UpdateConfig(config);
    }

    /// <summary>
    /// 生成模拟向量（用于测试或无API Key时）
    /// </summary>
    private float[] GenerateMockEmbedding(string text)
    {
        var dimension = Dimension;
        var random = new Random(text.GetHashCode());
        var embedding = new float[dimension];

        for (int i = 0; i < dimension; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1);
        }

        // 归一化
        var norm = MathF.Sqrt(embedding.Sum(x => x * x));
        if (norm > 0)
        {
            for (int i = 0; i < dimension; i++)
            {
                embedding[i] /= norm;
            }
        }

        return embedding;
    }

    private class OpenAIEmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingData>? Data { get; set; }
        
        [JsonPropertyName("model")]
        public string? Model { get; set; }
        
        [JsonPropertyName("usage")]
        public UsageInfo? Usage { get; set; }
    }

    private class EmbeddingData
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
        
        [JsonPropertyName("index")]
        public int Index { get; set; }
    }
    
    private class UsageInfo
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }
        
        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }
}
