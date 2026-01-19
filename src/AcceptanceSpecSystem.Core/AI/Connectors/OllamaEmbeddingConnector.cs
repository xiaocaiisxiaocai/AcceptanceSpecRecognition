using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.Interfaces;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Core.AI.Connectors;

/// <summary>
/// Ollama Embeddings 连接器（HTTP）
/// 约定：
/// - Endpoint: http://localhost:11434
/// - EmbeddingModel: ollama 模型名（如 nomic-embed-text）
/// </summary>
public class OllamaEmbeddingConnector : IAiEmbeddingConnector
{
    private readonly AiServiceConfig _config;

    public OllamaEmbeddingConnector(AiServiceConfig config)
    {
        _config = config;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_config.Endpoint))
            throw new InvalidOperationException("Endpoint 未配置");
        if (string.IsNullOrWhiteSpace(_config.EmbeddingModel))
            throw new InvalidOperationException("EmbeddingModel 未配置");

        var baseUrl = _config.Endpoint!.Trim().TrimEnd('/');
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        var payload = JsonSerializer.Serialize(new
        {
            model = _config.EmbeddingModel,
            prompt = text
        });

        using var resp = await http.PostAsync($"{baseUrl}/api/embeddings",
            new StringContent(payload, Encoding.UTF8, "application/json"), cancellationToken);

        var respText = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Ollama embedding 请求失败: HTTP {(int)resp.StatusCode} {respText}");

        using var doc = JsonDocument.Parse(respText);
        var emb = doc.RootElement.GetProperty("embedding");
        var result = new float[emb.GetArrayLength()];
        var i = 0;
        foreach (var v in emb.EnumerateArray())
        {
            result[i++] = v.GetSingle();
        }
        return result;
    }
}

