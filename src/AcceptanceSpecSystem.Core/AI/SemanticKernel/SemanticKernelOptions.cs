namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

public sealed class SemanticKernelOptions
{
    public const string SectionName = "SemanticKernel";

    public const string DefaultOllamaKeepAlive = "-1";

    /// <summary>
    /// Azure OpenAI API 版本，默认使用稳定版。
    /// </summary>
    public string AzureOpenAIApiVersion { get; set; } = "2024-10-21";

    /// <summary>
    /// Ollama 原生请求的模型驻留时长。-1 表示永久驻留，0 表示请求完成后立即卸载，
    /// 也可使用 30m、1h 等 Ollama duration 值。
    /// </summary>
    public string OllamaKeepAlive { get; set; } = DefaultOllamaKeepAlive;
}
