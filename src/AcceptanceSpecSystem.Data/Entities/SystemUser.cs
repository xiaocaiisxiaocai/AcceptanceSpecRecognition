namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 系统用户实体（用于登录鉴权）
/// </summary>
public class SystemUser
{
    /// <summary>
    /// 主键ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 所属公司ID（单公司边界）
    /// </summary>
    public int CompanyId { get; set; }

    /// <summary>
    /// 用户名（唯一）
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// 密码哈希（PBKDF2）
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// 昵称
    /// </summary>
    public string Nickname { get; set; } = string.Empty;

    /// <summary>
    /// 头像地址
    /// </summary>
    public string Avatar { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 权限版本号（角色或组织变更后递增，可用于强制令牌刷新）
    /// </summary>
    public int PermissionVersion { get; set; } = 1;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// 导航属性：所属公司
    /// </summary>
    public OrgCompany Company { get; set; } = null!;

    /// <summary>
    /// 导航属性：用户角色关系
    /// </summary>
    public ICollection<AuthUserRole> UserRoles { get; set; } = new List<AuthUserRole>();

    /// <summary>
    /// 导航属性：用户组织关系
    /// </summary>
    public ICollection<AuthUserOrgUnit> UserOrgUnits { get; set; } = new List<AuthUserOrgUnit>();
}
