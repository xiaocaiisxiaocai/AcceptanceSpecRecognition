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
            .FirstOrDefaultAsync(e => e.SpecId == specId && e.ModelName == modelName);
    }

    /// <summary>
    /// 根据规格ID获取该规格的所有向量缓存记录。
    /// </summary>
    /// <param name="specId">验收规格ID</param>
    /// <returns>向量缓存列表</returns>
    public async Task<IReadOnlyList<EmbeddingCache>> GetBySpecIdAsync(int specId)
    {
        return await _dbSet
            .Where(e => e.SpecId == specId)
            .ToListAsync();
    }

    /// <summary>
    /// 删除指定模型名称的所有向量缓存记录。
    /// </summary>
    /// <param name="modelName">模型名称</param>
    public async Task DeleteByModelNameAsync(string modelName)
    {
        var caches = await _dbSet
            .Where(e => e.ModelName == modelName)
            .ToListAsync();

        _dbSet.RemoveRange(caches);
    }

    /// <summary>
    /// 根据多个规格ID与模型名称批量获取向量缓存记录。
    /// </summary>
    /// <param name="specIds">验收规格ID集合</param>
    /// <param name="modelName">模型名称</param>
    /// <returns>向量缓存列表</returns>
    public async Task<IReadOnlyList<EmbeddingCache>> GetBySpecIdsAndModelAsync(IEnumerable<int> specIds, string modelName)
    {
        var idList = specIds.ToList();
        return await _dbSet
            .Where(e => idList.Contains(e.SpecId) && e.ModelName == modelName)
            .ToListAsync();
    }
}
