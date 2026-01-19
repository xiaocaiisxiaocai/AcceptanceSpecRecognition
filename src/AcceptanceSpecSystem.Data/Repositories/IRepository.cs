using System.Linq.Expressions;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 通用Repository接口
/// </summary>
/// <typeparam name="TEntity">实体类型</typeparam>
public interface IRepository<TEntity> where TEntity : class
{
    /// <summary>
    /// 根据ID获取实体
    /// </summary>
    /// <param name="id">实体ID</param>
    /// <returns>实体或null</returns>
    Task<TEntity?> GetByIdAsync(int id);

    /// <summary>
    /// 获取所有实体
    /// </summary>
    /// <returns>实体列表</returns>
    Task<IReadOnlyList<TEntity>> GetAllAsync();

    /// <summary>
    /// 根据条件查询实体
    /// </summary>
    /// <param name="predicate">查询条件</param>
    /// <returns>符合条件的实体列表</returns>
    Task<IReadOnlyList<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate);

    /// <summary>
    /// 根据条件查询第一个实体
    /// </summary>
    /// <param name="predicate">查询条件</param>
    /// <returns>实体或null</returns>
    Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate);

    /// <summary>
    /// 添加实体
    /// </summary>
    /// <param name="entity">要添加的实体</param>
    /// <returns>添加后的实体</returns>
    Task<TEntity> AddAsync(TEntity entity);

    /// <summary>
    /// 批量添加实体
    /// </summary>
    /// <param name="entities">要添加的实体列表</param>
    Task AddRangeAsync(IEnumerable<TEntity> entities);

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
    /// <returns>是否存在</returns>
    Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate);

    /// <summary>
    /// 获取符合条件的实体数量
    /// </summary>
    /// <param name="predicate">查询条件（可选）</param>
    /// <returns>数量</returns>
    Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null);
}
