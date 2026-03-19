using System.ComponentModel.DataAnnotations;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.DTOs;

/// <summary>
/// 系统用户DTO
/// </summary>
public class SystemUserDto
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Nickname { get; set; } = string.Empty;

    public string Avatar { get; set; } = string.Empty;

    public List<string> Roles { get; set; } = [];

    public List<string> Permissions { get; set; } = [];

    public bool IsActive { get; set; }

    public int PermissionVersion { get; set; }

    public List<SystemUserOrgUnitDto> OrgUnits { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// 用户组织关联DTO
/// </summary>
public class SystemUserOrgUnitDto
{
    public int OrgUnitId { get; set; }

    public string OrgUnitName { get; set; } = string.Empty;

    public OrgUnitType OrgUnitType { get; set; }

    public bool IsPrimary { get; set; }

    public DateTime? StartAt { get; set; }

    public DateTime? EndAt { get; set; }
}

/// <summary>
/// 新增系统用户请求
/// </summary>
public class CreateSystemUserRequest
{
    [Required(ErrorMessage = "用户名不能为空")]
    [StringLength(64, ErrorMessage = "用户名长度不能超过64个字符")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "密码不能为空")]
    [StringLength(200, MinimumLength = 8, ErrorMessage = "密码长度必须在8到200个字符之间")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "昵称不能为空")]
    [StringLength(100, ErrorMessage = "昵称长度不能超过100个字符")]
    public string Nickname { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "头像地址长度不能超过500个字符")]
    public string? Avatar { get; set; }

    [Required(ErrorMessage = "角色不能为空")]
    public List<string> Roles { get; set; } = [];

    public int? PrimaryOrgUnitId { get; set; }

    public List<int> OrgUnitIds { get; set; } = [];

    public DateTime? RoleStartAt { get; set; }

    public DateTime? RoleEndAt { get; set; }

    public DateTime? OrgStartAt { get; set; }

    public DateTime? OrgEndAt { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 更新系统用户请求
/// </summary>
public class UpdateSystemUserRequest
{
    [Required(ErrorMessage = "昵称不能为空")]
    [StringLength(100, ErrorMessage = "昵称长度不能超过100个字符")]
    public string Nickname { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "头像地址长度不能超过500个字符")]
    public string? Avatar { get; set; }

    [Required(ErrorMessage = "角色不能为空")]
    public List<string> Roles { get; set; } = [];

    public int? PrimaryOrgUnitId { get; set; }

    public List<int> OrgUnitIds { get; set; } = [];

    public DateTime? RoleStartAt { get; set; }

    public DateTime? RoleEndAt { get; set; }

    public DateTime? OrgStartAt { get; set; }

    public DateTime? OrgEndAt { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 重置密码请求
/// </summary>
public class ResetSystemUserPasswordRequest
{
    [Required(ErrorMessage = "新密码不能为空")]
    [StringLength(200, MinimumLength = 8, ErrorMessage = "新密码长度必须在8到200个字符之间")]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>
/// 更新用户状态请求
/// </summary>
public class UpdateSystemUserStatusRequest
{
    public bool IsActive { get; set; }
}
