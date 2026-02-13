using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// AI服务配置Repository接口
/// </summary>
public interface IAiServiceConfigRepository : IRepository<AiServiceConfig>
{
    /// <summary>
    /// 根据名称获取配置
    /// </summary>
    /// <param name="name">配置名称</param>
    /// <returns>配置或null</returns>
    Task<AiServiceConfig?> GetByNameAsync(string name);

    /// <summary>
    /// 根据服务类型获取配置列表
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    /// <returns>配置列表</returns>
    Task<IReadOnlyList<AiServiceConfig>> GetByServiceTypeAsync(AiServiceType serviceType);

    /// <summary>
    /// 根据用途获取配置列表
    /// </summary>
    /// <param name="purpose">用途</param>
    /// <returns>配置列表</returns>
    Task<IReadOnlyList<AiServiceConfig>> GetByPurposeAsync(AiServicePurpose purpose);
}
