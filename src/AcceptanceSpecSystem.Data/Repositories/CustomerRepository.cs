using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 客户Repository实现
/// </summary>
public class CustomerRepository : Repository<Customer>, ICustomerRepository
{
    /// <summary>
    /// 创建CustomerRepository实例
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public CustomerRepository(AppDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 根据客户名称获取客户。
    /// </summary>
    /// <param name="name">客户名称</param>
    /// <returns>客户或 null</returns>
    public async Task<Customer?> GetByNameAsync(string name)
    {
        return await _dbSet.FirstOrDefaultAsync(c => c.Name == name);
    }

    /// <summary>
    /// 获取客户并包含其验收规格集合（<see cref="Customer.AcceptanceSpecs"/>）。
    /// </summary>
    /// <param name="id">客户ID</param>
    /// <returns>客户（包含验收规格）或 null</returns>
    public async Task<Customer?> GetWithAcceptanceSpecsAsync(int id)
    {
        return await _dbSet
            .Include(c => c.AcceptanceSpecs)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    /// <summary>
    /// 获取所有客户并包含其验收规格集合（<see cref="Customer.AcceptanceSpecs"/>）。
    /// </summary>
    /// <returns>客户列表（包含验收规格）</returns>
    public async Task<IReadOnlyList<Customer>> GetAllWithAcceptanceSpecsAsync()
    {
        return await _dbSet
            .Include(c => c.AcceptanceSpecs)
            .ToListAsync();
    }
}
