using AcceptanceSpecSystem.Core.AI.Models;

namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

/// <summary>
/// AI 服务候选选择器，根据用途和偏好服务返回按优先级排序的可用配置。
/// </summary>
public interface IAiServiceSelector
{
    /// <summary>
    /// 获取满足用途要求的候选服务列表。
    /// </summary>
    Task<IReadOnlyList<AiServiceConfigModel>> GetCandidatesAsync(
        AiServicePurpose purpose,
        int? preferredId = null,
        CancellationToken cancellationToken = default);
}
