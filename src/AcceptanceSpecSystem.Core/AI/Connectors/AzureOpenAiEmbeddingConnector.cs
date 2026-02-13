using AcceptanceSpecSystem.Core.AI.Interfaces;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Core.AI.Connectors;

/// <summary>
/// Azure OpenAI Embeddings 连接器（Semantic Kernel）
/// 约定：
/// - Endpoint: https://{resource}.openai.azure.com/
/// - EmbeddingModel: 部署名 (deployment)
/// - ApiKey: 资源 key
/// </summary>
public class AzureOpenAiEmbeddingConnector : IAiEmbeddingConnector
{
    private readonly AiServiceConfig _config;

    public AzureOpenAiEmbeddingConnector(AiServiceConfig config)
    {
        _config = config;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_config.Endpoint))
            throw new InvalidOperationException("Endpoint 未配置");
        if (string.IsNullOrWhiteSpace(_config.ApiKey))
            throw new InvalidOperationException("ApiKey 未配置");
        if (string.IsNullOrWhiteSpace(_config.EmbeddingModel))
            throw new InvalidOperationException("EmbeddingModel(部署名) 未配置");

        var endpoint = _config.Endpoint!.Trim().TrimEnd('/');
        var deploymentName = _config.EmbeddingModel!.Trim();
        var url = $"{endpoint}/openai/deployments/{deploymentName}/embeddings?api-version=2024-02-15-preview";

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Add("api-key", _config.ApiKey!);

        var payload = JsonSerializer.Serialize(new
        {
            input = text
        });

        using var resp = await http.PostAsync(url,
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

