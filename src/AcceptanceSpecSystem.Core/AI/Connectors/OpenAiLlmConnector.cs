using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.Interfaces;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Core.AI.Connectors;

/// <summary>
/// OpenAI / OpenAI-compatible 聊天模型连接器
/// </summary>
public class OpenAiLlmConnector : IAiLlmConnector
{
    private readonly AiServiceConfig _config;
    private const int StreamChunkSize = 40;

    public OpenAiLlmConnector(AiServiceConfig config)
    {
        _config = config;
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_config.LlmModel))
            throw new InvalidOperationException("LlmModel 未配置");

        var baseUrl = string.IsNullOrWhiteSpace(_config.Endpoint) ? "https://api.openai.com" : _config.Endpoint!.Trim();
        baseUrl = baseUrl.TrimEnd('/');
        if (!baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            baseUrl += "/v1";

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (!string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.ApiKey);
        }

        var payload = JsonSerializer.Serialize(new
        {
            model = _config.LlmModel,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            temperature = 0.2
        });

        using var resp = await http.PostAsync(
            $"{baseUrl}/chat/completions",
            new StringContent(payload, Encoding.UTF8, "application/json"),
            cancellationToken);

        var respText = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"LLM 请求失败: HTTP {(int)resp.StatusCode} {respText}");

        using var doc = JsonDocument.Parse(respText);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

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
