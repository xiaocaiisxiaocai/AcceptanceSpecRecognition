namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 公司实体
/// </summary>
public class OrgCompany
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? UpdatedAt { get; set; }

    public ICollection<SystemUser> Users { get; set; } = new List<SystemUser>();

    public ICollection<AuthRole> Roles { get; set; } = new List<AuthRole>();

    public ICollection<OrgUnit> OrgUnits { get; set; } = new List<OrgUnit>();
}

/// <summary>
/// 组织节点实体（支持公司/事业部/部门/课别，允许跳级）
/// </summary>
public class OrgUnit
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int? ParentId { get; set; }

    public OrgUnitType UnitType { get; set; } = OrgUnitType.Department;

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = "/";

    public int Depth { get; set; }

    public int Sort { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? UpdatedAt { get; set; }

    public OrgCompany Company { get; set; } = null!;

    public OrgUnit? Parent { get; set; }

    public ICollection<OrgUnit> Children { get; set; } = new List<OrgUnit>();

    public ICollection<AuthUserOrgUnit> UserOrgUnits { get; set; } = new List<AuthUserOrgUnit>();

    public ICollection<AuthRoleDataScopeNode> DataScopeNodes { get; set; } = new List<AuthRoleDataScopeNode>();
}

/// <summary>
/// 角色实体
/// </summary>
public class AuthRole
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsBuiltIn { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? UpdatedAt { get; set; }

    public OrgCompany Company { get; set; } = null!;

    public ICollection<AuthRolePermission> RolePermissions { get; set; } = new List<AuthRolePermission>();

    public ICollection<AuthUserRole> UserRoles { get; set; } = new List<AuthUserRole>();

    public ICollection<AuthRoleDataScope> DataScopes { get; set; } = new List<AuthRoleDataScope>();
}

/// <summary>
/// 权限实体（页面/按钮/API）
/// </summary>
public class AuthPermission
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public PermissionType PermissionType { get; set; }

    public string Resource { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string? RoutePath { get; set; }

    public string? HttpMethod { get; set; }

    public string? ApiPath { get; set; }

    public bool IsBuiltIn { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? UpdatedAt { get; set; }

    public ICollection<AuthRolePermission> RolePermissions { get; set; } = new List<AuthRolePermission>();
}

/// <summary>
/// 角色-权限关系
/// </summary>
public class AuthRolePermission
{
    public int RoleId { get; set; }

    public int PermissionId { get; set; }

    public AuthRole Role { get; set; } = null!;

    public AuthPermission Permission { get; set; } = null!;
}

/// <summary>
/// 用户-角色关系（支持临时有效期）
/// </summary>
public class AuthUserRole
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int RoleId { get; set; }

    public DateTime? StartAt { get; set; }

    public DateTime? EndAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public SystemUser User { get; set; } = null!;

    public AuthRole Role { get; set; } = null!;
}

/// <summary>
/// 用户-组织关系（支持主组织、临时协作有效期）
/// </summary>
public class AuthUserOrgUnit
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int OrgUnitId { get; set; }

    public bool IsPrimary { get; set; }

    public DateTime? StartAt { get; set; }

    public DateTime? EndAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public SystemUser User { get; set; } = null!;

    public OrgUnit OrgUnit { get; set; } = null!;
}

/// <summary>
/// 角色-数据范围规则
/// </summary>
public class AuthRoleDataScope
{
    public int Id { get; set; }

    public int RoleId { get; set; }

    public string Resource { get; set; } = "*";

    public DataScopeType ScopeType { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public AuthRole Role { get; set; } = null!;

    public ICollection<AuthRoleDataScopeNode> Nodes { get; set; } = new List<AuthRoleDataScopeNode>();
}

/// <summary>
/// 角色-数据范围节点关系
/// </summary>
public class AuthRoleDataScopeNode
{
    public int Id { get; set; }

    public int RoleDataScopeId { get; set; }

    public int OrgUnitId { get; set; }

    public AuthRoleDataScope RoleDataScope { get; set; } = null!;

    public OrgUnit OrgUnit { get; set; } = null!;
}
