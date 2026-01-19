using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 向量缓存Repository接口
/// </summary>
public interface IEmbeddingCacheRepository : IRepository<EmbeddingCache>
{
    /// <summary>
    /// 根据验收规格ID和模型名称获取缓存
    /// </summary>
    /// <param name="specId">验收规格ID</param>
    /// <param name="modelName">模型名称</param>
    /// <returns>向量缓存或null</returns>
    Task<EmbeddingCache?> GetBySpecAndModelAsync(int specId, string modelName);

    /// <summary>
    /// 根据验收规格ID获取所有缓存
    /// </summary>
    /// <param name="specId">验收规格ID</param>
    /// <returns>向量缓存列表</returns>
    Task<IReadOnlyList<EmbeddingCache>> GetBySpecIdAsync(int specId);

    /// <summary>
    /// 删除指定模型的所有缓存
    /// </summary>
    /// <param name="modelName">模型名称</param>
    Task DeleteByModelNameAsync(string modelName);

    /// <summary>
    /// 批量获取验收规格的向量缓存
    /// </summary>
    /// <param name="specIds">验收规格ID列表</param>
    /// <param name="modelName">模型名称</param>
    /// <returns>向量缓存列表</returns>
    Task<IReadOnlyList<EmbeddingCache>> GetBySpecIdsAndModelAsync(IEnumerable<int> specIds, string modelName);
}
