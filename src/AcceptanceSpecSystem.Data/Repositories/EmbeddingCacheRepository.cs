using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 向量缓存Repository实现
/// </summary>
public class EmbeddingCacheRepository : Repository<EmbeddingCache>, IEmbeddingCacheRepository
{
    /// <summary>
    /// 创建EmbeddingCacheRepository实例
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public EmbeddingCacheRepository(AppDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 根据规格ID与模型名称获取向量缓存记录。
    /// </summary>
    /// <param name="specId">验收规格ID</param>
    /// <param name="modelName">模型名称</param>
    /// <returns>向量缓存记录或 null</returns>
    public async Task<EmbeddingCache?> GetBySpecAndModelAsync(int specId, string modelName)
    {
        return await _dbSet
            .FirstOrDefaultAsync(e =>
                e.SpecId == specId &&
                e.ModelName == modelName &&
                e.Usage == EmbeddingCache.DefaultUsage);
    }

    /// <summary>
    /// 根据规格ID、模型名称与用途精确获取不跟踪的向量缓存记录。
    /// </summary>
    public async Task<EmbeddingCache?> GetBySpecModelUsageAsync(
        int specId,
        string modelName,
        string usage,
        CancellationToken cancellationToken)
    {
        return await _dbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(
                cache =>
                    cache.SpecId == specId &&
                    cache.ModelName == modelName &&
                    cache.Usage == usage,
                cancellationToken);
    }

    /// <summary>
    /// 根据规格ID获取该规格的所有向量缓存记录。
    /// </summary>
    /// <param name="specId">验收规格ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>向量缓存列表</returns>
    public async Task<IReadOnlyList<EmbeddingCache>> GetBySpecIdAsync(
        int specId,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(e => e.SpecId == specId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 删除指定模型名称的所有向量缓存记录。
    /// </summary>
    /// <param name="modelName">模型名称</param>
    public async Task DeleteByModelNameAsync(string modelName)
    {
        await _dbSet
            .Where(e => e.ModelName == modelName)
            .ExecuteDeleteAsync();
    }

    /// <summary>
    /// 根据多个规格ID与模型名称批量获取向量缓存记录。
    /// </summary>
    /// <param name="specIds">验收规格ID集合</param>
    /// <param name="modelName">模型名称</param>
    /// <returns>向量缓存列表</returns>
    public async Task<IReadOnlyList<EmbeddingCache>> GetBySpecIdsAndModelAsync(IEnumerable<int> specIds, string modelName)
    {
        return await GetBySpecIdsAndModelAndUsageAsync(specIds, modelName, EmbeddingCache.DefaultUsage);
    }

    /// <summary>
    /// 根据多个规格ID、模型名称与用途批量获取向量缓存记录。
    /// </summary>
    /// <param name="specIds">验收规格ID集合</param>
    /// <param name="modelName">模型名称</param>
    /// <param name="usage">向量用途</param>
    /// <returns>向量缓存列表</returns>
    public async Task<IReadOnlyList<EmbeddingCache>> GetBySpecIdsAndModelAndUsageAsync(
        IEnumerable<int> specIds,
        string modelName,
        string usage)
    {
        var idList = specIds.ToList();
        return await _dbSet
            .Where(e => idList.Contains(e.SpecId) && e.ModelName == modelName && e.Usage == usage)
            .ToListAsync();
    }

    /// <summary>
    /// 删除指定时间之前过期的缓存。
    /// </summary>
    /// <param name="beforeTime">过期时间阈值</param>
    /// <returns>删除的记录数</returns>
    public async Task<int> DeleteExpiredAsync(DateTime beforeTime)
    {
        return await _dbSet
            .Where(e => e.ExpiresAt != null && e.ExpiresAt < beforeTime)
            .ExecuteDeleteAsync();
    }

    /// <summary>
    /// 删除指定模型版本的缓存（用于模型升级时批量失效）。
    /// </summary>
    /// <param name="modelName">模型名称</param>
    /// <param name="modelVersion">模型版本</param>
    /// <returns>删除的记录数</returns>
    public async Task<int> DeleteByModelVersionAsync(string modelName, string modelVersion)
    {
        return await _dbSet
            .Where(e => e.ModelName == modelName && e.ModelVersion != modelVersion)
            .ExecuteDeleteAsync();
    }
}
