namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 用户多角色裁剪策略
/// </summary>
public static class AuthUserRoleSingleRolePolicy
{
    public static AuthUserRole? SelectRoleToKeep(IEnumerable<AuthUserRole>? roles)
    {
        if (roles == null)
            return null;

        return roles
            .Where(role => role.Role != null)
            .OrderByDescending(role => string.Equals(role.Role.Code, "admin", StringComparison.OrdinalIgnoreCase))
            .ThenBy(role => role.CreatedAt)
            .ThenBy(role => role.Id)
            .FirstOrDefault();
    }
}
