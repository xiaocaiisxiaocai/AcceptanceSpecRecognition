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
    /// 获取默认 AI 服务配置（IsDefault=true）。
    /// </summary>
    /// <returns>默认配置或 null</returns>
    public async Task<AiServiceConfig?> GetDefaultAsync()
    {
        return await _dbSet
            .FirstOrDefaultAsync(c => c.IsDefault);
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
    /// 将指定配置设置为默认配置，并取消当前默认配置（若存在）。
    /// </summary>
    /// <param name="id">配置ID</param>
    public async Task SetDefaultAsync(int id)
    {
        // 先取消当前默认
        var currentDefault = await _dbSet.FirstOrDefaultAsync(c => c.IsDefault);
        if (currentDefault != null)
        {
            currentDefault.IsDefault = false;
        }

        // 设置新默认
        var newDefault = await _dbSet.FindAsync(id);
        if (newDefault != null)
        {
            newDefault.IsDefault = true;
            newDefault.UpdatedAt = DateTime.Now;
        }
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
}
