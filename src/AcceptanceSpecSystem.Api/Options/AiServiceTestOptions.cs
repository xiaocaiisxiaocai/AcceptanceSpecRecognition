namespace AcceptanceSpecSystem.Api.Options;

/// <summary>
/// AI 服务连接测试配置。
/// </summary>
public class AiServiceTestOptions
{
    public const string SectionName = "AiServiceTest";

    /// <summary>
    /// 单项服务测试超时（秒）。
    /// </summary>
    public int TimeoutSeconds { get; set; } = 15;
}
