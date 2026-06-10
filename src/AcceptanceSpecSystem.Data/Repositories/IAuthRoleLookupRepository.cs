namespace AcceptanceSpecSystem.Data.Repositories;

/// <summary>
/// 角色查询仓储，用于鉴权只读场景。
/// </summary>
public interface IAuthRoleLookupRepository
{
    /// <summary>
    /// 获取公司下所有启用角色概要。
    /// </summary>
    Task<IReadOnlyList<AuthRoleLookupItem>> GetCompanyRolesAsync(int companyId);

    /// <summary>
    /// 获取角色ID到角色编码的映射。
    /// </summary>
    Task<IReadOnlyDictionary<int, string>> GetRoleCodeMapAsync(int companyId, IEnumerable<int> roleIds);
}

/// <summary>
/// 角色只读查询结果。
/// </summary>
public sealed class AuthRoleLookupItem
{
    public int Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
}
