using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 机型Repository实现
/// </summary>
public class MachineModelRepository : Repository<MachineModel>, IMachineModelRepository
{
    /// <summary>
    /// 创建MachineModelRepository实例
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public MachineModelRepository(AppDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 获取机型并包含其验收规格集合（<see cref="MachineModel.AcceptanceSpecs"/>）。
    /// </summary>
    /// <param name="id">机型ID</param>
    /// <returns>机型（包含验收规格）或 null</returns>
    public async Task<MachineModel?> GetWithSpecCountAsync(int id)
    {
        return await _dbSet
            .Include(m => m.AcceptanceSpecs)
            .FirstOrDefaultAsync(m => m.Id == id);
    }
}
