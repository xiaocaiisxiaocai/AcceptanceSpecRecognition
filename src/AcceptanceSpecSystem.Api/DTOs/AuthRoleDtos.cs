using System.ComponentModel.DataAnnotations;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.DTOs;

/// <summary>
/// 角色数据DTO
/// </summary>
public class AuthRoleDto
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsBuiltIn { get; set; }

    public bool IsActive { get; set; }

    public List<string> PermissionCodes { get; set; } = [];

    public List<AuthRoleDataScopeDto> DataScopes { get; set; } = [];
}

/// <summary>
/// 角色数据范围DTO
/// </summary>
public class AuthRoleDataScopeDto
{
    public string Resource { get; set; } = "spec";

    public DataScopeType ScopeType { get; set; }

    public List<int> OrgUnitIds { get; set; } = [];
}

/// <summary>
/// 创建角色请求
/// </summary>
public class CreateAuthRoleRequest
{
    [Required(ErrorMessage = "角色编码不能为空")]
    [StringLength(64, ErrorMessage = "角色编码长度不能超过64")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "角色名称不能为空")]
    [StringLength(100, ErrorMessage = "角色名称长度不能超过100")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "角色描述长度不能超过500")]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public List<string> PermissionCodes { get; set; } = [];

    public List<AuthRoleDataScopeDto> DataScopes { get; set; } = [];
}

/// <summary>
/// 更新角色请求
/// </summary>
public class UpdateAuthRoleRequest
{
    [Required(ErrorMessage = "角色名称不能为空")]
    [StringLength(100, ErrorMessage = "角色名称长度不能超过100")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "角色描述长度不能超过500")]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public List<string> PermissionCodes { get; set; } = [];

    public List<AuthRoleDataScopeDto> DataScopes { get; set; } = [];
}
