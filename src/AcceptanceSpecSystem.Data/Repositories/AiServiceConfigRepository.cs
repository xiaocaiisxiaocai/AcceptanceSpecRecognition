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
    /// <remarks>
    /// 用途归一化逻辑统一复用 <see cref="AiServiceConfig.GetEffectivePurpose"/>，
    /// 避免与实体方法出现两套互相独立、容易分叉的业务判断。
    /// 由于 AI 服务配置表规模很小，这里先按 <c>IsDisabled</c> 在数据库侧过滤，
    /// 再在内存中按有效用途精确匹配。
    /// </remarks>
    /// <param name="purpose">用途</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>配置列表</returns>
    public async Task<IReadOnlyList<AiServiceConfig>> GetByPurposeAsync(
        AiServicePurpose purpose,
        CancellationToken cancellationToken = default)
    {
        if (purpose != AiServicePurpose.Llm && purpose != AiServicePurpose.Embedding)
        {
            return [];
        }

        var enabledConfigs = await _dbSet
            .Where(c => !c.IsDisabled)
            .ToListAsync(cancellationToken);

        return enabledConfigs
            .Where(c => HasConfiguredPurpose(c, purpose))
            .ToList();
    }

    private static bool HasConfiguredPurpose(AiServiceConfig config, AiServicePurpose purpose)
    {
        if (config.Purpose == purpose)
        {
            return true;
        }

        return config.Purpose == AiServicePurpose.None &&
               config.GetEffectivePurpose() == purpose &&
               (purpose == AiServicePurpose.Llm ? config.HasLlmModel() : config.HasEmbeddingModel());
    }
}
