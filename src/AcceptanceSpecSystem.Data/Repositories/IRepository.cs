using System.Linq.Expressions;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 通用Repository接口
/// </summary>
/// <typeparam name="TEntity">实体类型</typeparam>
public interface IRepository<TEntity> where TEntity : class
{
    /// <summary>
    /// 获取可组合查询（用于数据库侧筛选/分页）。
    /// </summary>
    /// <remarks>
    /// 应优先使用各仓储上已定义的专用查询方法（例如
    /// <c>IAcceptanceSpecRepository.GetPagedWithFilterAsync</c> /
    /// <c>GetProcessCountByCustomerAsync</c> 等封装了范围过滤、分页、分组等重复模式的方法），
    /// 便于集中维护过滤逻辑、复用索引友好的查询写法。
    /// <c>Query()</c> 仅作为过渡期兜底（当前仓库内仍有 20+ 处直接调用），
    /// 长期应逐步将高频重复的查询组合收敛为专用仓储方法，减少调用方各自拼接
    /// 范围/过滤条件导致的重复实现与潜在不一致。
    /// </remarks>
    /// <param name="asNoTracking">是否禁用跟踪（默认禁用）</param>
    IQueryable<TEntity> Query(bool asNoTracking = true);

    /// <summary>
    /// 根据ID获取实体
    /// </summary>
    /// <param name="id">实体ID</param>
    /// <param name="cancellationToken">请求取消令牌</param>
    /// <returns>实体或null</returns>
    Task<TEntity?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有实体
    /// </summary>
    /// <param name="cancellationToken">请求取消令牌</param>
    /// <returns>实体列表</returns>
    Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据条件查询实体
    /// </summary>
    /// <param name="predicate">查询条件</param>
    /// <param name="cancellationToken">请求取消令牌</param>
    /// <returns>符合条件的实体列表</returns>
    Task<IReadOnlyList<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据条件查询第一个实体
    /// </summary>
    /// <param name="predicate">查询条件</param>
    /// <param name="cancellationToken">请求取消令牌</param>
    /// <returns>实体或null</returns>
    Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加实体
    /// </summary>
    /// <param name="entity">要添加的实体</param>
    /// <param name="cancellationToken">请求取消令牌</param>
    /// <returns>添加后的实体</returns>
    Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量添加实体
    /// </summary>
    /// <param name="entities">要添加的实体列表</param>
    /// <param name="cancellationToken">请求取消令牌</param>
    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新实体
    /// </summary>
    /// <param name="entity">要更新的实体</param>
    void Update(TEntity entity);

    /// <summary>
    /// 删除实体
    /// </summary>
    /// <param name="entity">要删除的实体</param>
    void Remove(TEntity entity);

    /// <summary>
    /// 批量删除实体
    /// </summary>
    /// <param name="entities">要删除的实体列表</param>
    void RemoveRange(IEnumerable<TEntity> entities);

    /// <summary>
    /// 检查是否存在符合条件的实体
    /// </summary>
    /// <param name="predicate">查询条件</param>
    /// <param name="cancellationToken">请求取消令牌</param>
    /// <returns>是否存在</returns>
    Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取符合条件的实体数量
    /// </summary>
    /// <param name="predicate">查询条件（可选）</param>
    /// <param name="cancellationToken">请求取消令牌</param>
    /// <returns>数量</returns>
    Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);
}
