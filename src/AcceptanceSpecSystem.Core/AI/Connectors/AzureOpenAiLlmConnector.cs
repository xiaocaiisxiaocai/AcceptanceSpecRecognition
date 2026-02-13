using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.Interfaces;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Core.AI.Connectors;

/// <summary>
/// Azure OpenAI 聊天模型连接器
/// 约定：
/// - Endpoint: https://{resource}.openai.azure.com
/// - LlmModel: 部署名 (deployment)
/// </summary>
public class AzureOpenAiLlmConnector : IAiLlmConnector
{
    private const string ApiVersion = "2024-02-01";
    private readonly AiServiceConfig _config;
    private const int StreamChunkSize = 40;

    public AzureOpenAiLlmConnector(AiServiceConfig config)
    {
        _config = config;
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_config.Endpoint))
            throw new InvalidOperationException("Endpoint 未配置");
        if (string.IsNullOrWhiteSpace(_config.LlmModel))
            throw new InvalidOperationException("LlmModel(部署名) 未配置");
        if (string.IsNullOrWhiteSpace(_config.ApiKey))
            throw new InvalidOperationException("ApiKey 未配置");

        var baseUrl = _config.Endpoint!.Trim().TrimEnd('/');
        var url = $"{baseUrl}/openai/deployments/{_config.LlmModel!.Trim()}/chat/completions?api-version={ApiVersion}";

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("api-key", _config.ApiKey);

        var payload = JsonSerializer.Serialize(new
        {
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            temperature = 0.2
        });

        using var resp = await http.PostAsync(
            url,
            new StringContent(payload, Encoding.UTF8, "application/json"),
            cancellationToken);

        var respText = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Azure OpenAI 请求失败: HTTP {(int)resp.StatusCode} {respText}");

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
