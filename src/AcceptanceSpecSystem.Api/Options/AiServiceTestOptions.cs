namespace AcceptanceSpecSystem.Api.Options;

/// <summary>
/// AI 服务连接测试配置。
/// </summary>
public class AiServiceTestOptions
{
    public const string SectionName = "AiServiceTest";

    /// <summary>
    /// LLM 服务测试超时（秒）。
    /// </summary>
    public int LlmTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Embedding 服务测试超时（秒）。
    /// </summary>
    public int EmbeddingTimeoutSeconds { get; set; } = 15;
}
