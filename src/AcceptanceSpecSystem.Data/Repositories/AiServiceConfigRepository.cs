using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// AI服务配置Repository实现
/// </summary>
public class AiServiceConfigRepository : Repository<AiServiceConfig>, IAiServiceConfigRepository
{
    /// <summary>
    /// 创建AiServiceConfigRepository实例
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public AiServiceConfigRepository(AppDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 根据名称获取 AI 服务配置。
    /// </summary>
    /// <param name="name">配置名称</param>
    /// <returns>配置或 null</returns>
    public async Task<AiServiceConfig?> GetByNameAsync(string name)
    {
        return await _dbSet
            .FirstOrDefaultAsync(c => c.Name == name);
    }

    /// <summary>
    /// 根据服务类型获取配置列表。
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    /// <returns>配置列表</returns>
    public async Task<IReadOnlyList<AiServiceConfig>> GetByServiceTypeAsync(AiServiceType serviceType)
    {
        return await _dbSet
            .Where(c => c.ServiceType == serviceType)
            .ToListAsync();
    }

    /// <summary>
    /// 根据用途获取配置列表。
    /// </summary>
    /// <param name="purpose">用途</param>
    /// <returns>配置列表</returns>
    public async Task<IReadOnlyList<AiServiceConfig>> GetByPurposeAsync(AiServicePurpose purpose)
    {
        return await _dbSet
            .Where(c => (c.Purpose & purpose) != AiServicePurpose.None)
            .ToListAsync();
    }
}
