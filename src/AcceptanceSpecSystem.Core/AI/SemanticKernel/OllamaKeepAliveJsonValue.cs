using System.Globalization;
using System.Text.Json;

namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

/// <summary>
/// 将配置文本转换为 Ollama 原生 API 接受的 keep_alive JSON 值。
/// 纯整数必须作为 JSON 数字发送；带单位的时长保持字符串。
/// </summary>
internal static class OllamaKeepAliveJsonValue
{
    public static JsonElement Create(string value)
    {
        var normalized = value.Trim();
        return long.TryParse(
            normalized,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var seconds)
            ? JsonSerializer.SerializeToElement(seconds)
            : JsonSerializer.SerializeToElement(normalized);
    }
}
