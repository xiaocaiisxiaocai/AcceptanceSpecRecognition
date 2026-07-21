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
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>向量缓存列表</returns>
    Task<IReadOnlyList<EmbeddingCache>> GetBySpecIdAsync(
        int specId,
        CancellationToken cancellationToken = default);

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

    /// <summary>
    /// 按用途批量获取验收规格的向量缓存
    /// </summary>
    /// <param name="specIds">验收规格ID列表</param>
    /// <param name="modelName">模型名称</param>
    /// <param name="usage">向量用途</param>
    /// <returns>向量缓存列表</returns>
    Task<IReadOnlyList<EmbeddingCache>> GetBySpecIdsAndModelAndUsageAsync(
        IEnumerable<int> specIds,
        string modelName,
        string usage);

    /// <summary>
    /// 删除指定时间之前过期的缓存
    /// </summary>
    /// <param name="beforeTime">过期时间阈值</param>
    /// <returns>删除的记录数</returns>
    Task<int> DeleteExpiredAsync(DateTime beforeTime);

    /// <summary>
    /// 删除指定模型版本的缓存（用于模型升级时批量失效）
    /// </summary>
    /// <param name="modelName">模型名称</param>
    /// <param name="modelVersion">模型版本</param>
    /// <returns>删除的记录数</returns>
    Task<int> DeleteByModelVersionAsync(string modelName, string modelVersion);
}
