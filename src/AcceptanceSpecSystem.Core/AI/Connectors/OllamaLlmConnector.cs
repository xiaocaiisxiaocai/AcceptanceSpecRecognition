using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.Interfaces;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Core.AI.Connectors;

/// <summary>
/// Ollama 聊天模型连接器（HTTP）
/// 约定：
/// - Endpoint: http://localhost:11434
/// - LlmModel: ollama 模型名（如 qwen2:7b）
/// </summary>
public class OllamaLlmConnector : IAiLlmConnector
{
    private readonly AiServiceConfig _config;
    private const int StreamChunkSize = 40;

    public OllamaLlmConnector(AiServiceConfig config)
    {
        _config = config;
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_config.Endpoint))
            throw new InvalidOperationException("Endpoint 未配置");
        if (string.IsNullOrWhiteSpace(_config.LlmModel))
            throw new InvalidOperationException("LlmModel 未配置");

        var baseUrl = _config.Endpoint!.Trim().TrimEnd('/');

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var payload = JsonSerializer.Serialize(new
        {
            model = _config.LlmModel,
            prompt,
            stream = false,
            options = new { temperature = 0.2 }
        });

        using var resp = await http.PostAsync(
            $"{baseUrl}/api/generate",
            new StringContent(payload, Encoding.UTF8, "application/json"),
            cancellationToken);

        var respText = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Ollama 请求失败: HTTP {(int)resp.StatusCode} {respText}");

        using var doc = JsonDocument.Parse(respText);
        var content = doc.RootElement.GetProperty("response").GetString();
        return content ?? string.Empty;
    }

    public async IAsyncEnumerable<string> GenerateStreamAsync(
        string prompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var full = await GenerateAsync(prompt, cancellationToken);
        if (string.IsNullOrEmpty(full))
            yield break;

        for (var i = 0; i < full.Length; i += StreamChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var len = Math.Min(StreamChunkSize, full.Length - i);
            yield return full.Substring(i, len);
        }
    }
}
