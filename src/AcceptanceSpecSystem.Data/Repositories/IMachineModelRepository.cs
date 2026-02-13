using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 机型Repository接口
/// </summary>
public interface IMachineModelRepository : IRepository<MachineModel>
{
    /// <summary>
    /// 获取机型及其验收规格数量
    /// </summary>
    /// <param name="id">机型ID</param>
    /// <returns>机型或null</returns>
    Task<MachineModel?> GetWithSpecCountAsync(int id);
}
