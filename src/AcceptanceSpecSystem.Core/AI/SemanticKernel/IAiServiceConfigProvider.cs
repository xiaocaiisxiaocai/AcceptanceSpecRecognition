using AcceptanceSpecSystem.Core.AI.Models;

namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

/// <summary>
/// AI 服务配置读取器，向 Core 层提供隔离后的服务配置模型。
/// </summary>
public interface IAiServiceConfigProvider
{
    /// <summary>
    /// 根据用途获取启用中的 AI 服务配置。
    /// </summary>
    Task<IReadOnlyList<AiServiceConfigModel>> GetByPurposeAsync(
        AiServicePurpose purpose,
        CancellationToken cancellationToken = default);
}
