using System.Linq.Expressions;
using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 通用Repository基类实现
/// </summary>
/// <typeparam name="TEntity">实体类型</typeparam>
public class Repository<TEntity> : IRepository<TEntity> where TEntity : class
{
    /// <summary>
    /// 数据库上下文
    /// </summary>
    protected readonly AppDbContext _context;

    /// <summary>
    /// 实体DbSet
    /// </summary>
    protected readonly DbSet<TEntity> _dbSet;

    /// <summary>
    /// 创建Repository实例
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public Repository(AppDbContext context)
    {
        _context = context;
        _dbSet = context.Set<TEntity>();
    }

    /// <summary>
    /// 获取可组合查询（默认不跟踪，适用于筛选/分页场景）
    /// </summary>
    public virtual IQueryable<TEntity> Query(bool asNoTracking = true)
    {
        return asNoTracking ? _dbSet.AsNoTracking() : _dbSet;
    }

    /// <summary>
    /// 根据主键ID获取实体。
    /// </summary>
    /// <param name="id">实体主键ID</param>
    /// <param name="cancellationToken">请求取消令牌</param>
    /// <returns>实体或 null</returns>
    public virtual async Task<TEntity?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FindAsync([id], cancellationToken);
    }

    /// <summary>
    /// 获取全部实体列表。
    /// </summary>
    /// <param name="cancellationToken">请求取消令牌</param>
    /// <returns>实体列表</returns>
    public virtual async Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await Query().ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 按条件查询实体列表。
    /// </summary>
    /// <param name="predicate">查询条件</param>
    /// <param name="cancellationToken">请求取消令牌</param>
    /// <returns>实体列表</returns>
    public virtual async Task<IReadOnlyList<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await Query().Where(predicate).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 按条件获取第一条实体，若不存在返回 null。
    /// </summary>
    /// <param name="predicate">查询条件</param>
    /// <param name="cancellationToken">请求取消令牌</param>
    /// <returns>实体或 null</returns>
    public virtual async Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FirstOrDefaultAsync(predicate, cancellationToken);
    }

    /// <summary>
    /// 新增实体。
    /// </summary>
    /// <param name="entity">实体</param>
    /// <param name="cancellationToken">请求取消令牌</param>
    /// <returns>实体（同一实例）</returns>
    public virtual async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddAsync(entity, cancellationToken);
        return entity;
    }

    /// <summary>
    /// 批量新增实体。
    /// </summary>
    /// <param name="entities">实体集合</param>
    /// <param name="cancellationToken">请求取消令牌</param>
    public virtual async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddRangeAsync(entities, cancellationToken);
    }

    /// <summary>
    /// 更新实体。
    /// </summary>
    /// <param name="entity">实体</param>
    public virtual void Update(TEntity entity)
    {
        _dbSet.Update(entity);
    }

    /// <summary>
    /// 删除实体。
    /// </summary>
    /// <param name="entity">实体</param>
    public virtual void Remove(TEntity entity)
    {
        _dbSet.Remove(entity);
    }

    /// <summary>
    /// 批量删除实体。
    /// </summary>
    /// <param name="entities">实体集合</param>
    public virtual void RemoveRange(IEnumerable<TEntity> entities)
    {
        _dbSet.RemoveRange(entities);
    }

    /// <summary>
    /// 判断是否存在满足条件的实体。
    /// </summary>
    /// <param name="predicate">查询条件</param>
    /// <param name="cancellationToken">请求取消令牌</param>
    /// <returns>是否存在</returns>
    public virtual async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await _dbSet.AnyAsync(predicate, cancellationToken);
    }

    /// <summary>
    /// 统计实体数量。
    /// </summary>
    /// <param name="predicate">查询条件（可选）</param>
    /// <param name="cancellationToken">请求取消令牌</param>
    /// <returns>数量</returns>
    public virtual async Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        return predicate == null
            ? await _dbSet.CountAsync(cancellationToken)
            : await _dbSet.CountAsync(predicate, cancellationToken);
    }
}
