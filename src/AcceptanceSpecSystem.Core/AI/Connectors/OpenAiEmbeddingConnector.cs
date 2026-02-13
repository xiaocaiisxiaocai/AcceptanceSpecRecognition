using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.Interfaces;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Core.AI.Connectors;

/// <summary>
/// OpenAI Embeddings 连接器（优先走 Semantic Kernel；必要时回退为 OpenAI-compatible HTTP）
/// </summary>
public class OpenAiEmbeddingConnector : IAiEmbeddingConnector
{
    private readonly AiServiceConfig _config;

    public OpenAiEmbeddingConnector(AiServiceConfig config)
    {
        _config = config;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_config.EmbeddingModel))
            throw new InvalidOperationException("EmbeddingModel 未配置");

        // OpenAI-compatible：官方/LMStudio/Custom/指定了 Endpoint 的情况
        var baseUrl = string.IsNullOrWhiteSpace(_config.Endpoint) ? "https://api.openai.com" : _config.Endpoint!.Trim();
        baseUrl = baseUrl.TrimEnd('/');
        if (baseUrl.EndsWith("/v1/v1", StringComparison.OrdinalIgnoreCase))
            baseUrl = baseUrl[..^3];
        if (!baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            baseUrl += "/v1";

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        if (!string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.ApiKey);
        }

        var payload = JsonSerializer.Serialize(new
        {
            model = _config.EmbeddingModel,
            input = text
        });

        using var resp = await http.PostAsync($"{baseUrl}/embeddings",
            new StringContent(payload, Encoding.UTF8, "application/json"), cancellationToken);

        var respText = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Embedding 请求失败: HTTP {(int)resp.StatusCode} {respText}");

        using var doc = JsonDocument.Parse(respText);
        var root = doc.RootElement;
        var data = root.GetProperty("data")[0];
        var emb = data.GetProperty("embedding");
        var result = new float[emb.GetArrayLength()];
        var i = 0;
        foreach (var v in emb.EnumerateArray())
        {
            result[i++] = v.GetSingle();
        }
        return result;
    }
}

