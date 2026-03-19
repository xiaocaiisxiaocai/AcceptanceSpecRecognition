using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 系统用户仓储
/// </summary>
public interface ISystemUserRepository : IRepository<SystemUser>
{
    /// <summary>
    /// 根据用户名查询用户
    /// </summary>
    Task<SystemUser?> GetByUsernameAsync(string username);

    /// <summary>
    /// 根据用户名查询用户并加载鉴权所需关系（角色、权限、组织）
    /// </summary>
    Task<SystemUser?> GetByUsernameWithAccessAsync(string username);

    /// <summary>
    /// 分页查询系统用户
    /// </summary>
    Task<(IReadOnlyList<SystemUser> Items, int Total)> GetPagedAsync(
        int page,
        int pageSize,
        int? companyId = null,
        string? keyword = null,
        bool? isActive = null);

    /// <summary>
    /// 统计启用中的 admin 用户数量
    /// </summary>
    Task<int> CountActiveAdminUsersAsync(int companyId);
}
