using System.ComponentModel.DataAnnotations;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Contracts;

/// <summary>
/// 组织节点DTO
/// </summary>
public class OrgUnitDto
{
    public int Id { get; set; }

    public int? ParentId { get; set; }

    public OrgUnitType UnitType { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = "/";

    public int Depth { get; set; }

    public int Sort { get; set; }

    public bool IsActive { get; set; }

    public List<OrgUnitDto> Children { get; set; } = [];
}

/// <summary>
/// 导入与智能填充使用的业务归属上下文。
/// </summary>
public class BusinessOrgContextDto
{
    public bool RequiresSelection { get; set; }

    public int? CurrentOrgUnitId { get; set; }

    public string? CurrentOrgUnitName { get; set; }

    public bool IsCompanyFallback { get; set; }

    public List<BusinessOrgOptionDto> Options { get; set; } = [];
}

public class BusinessOrgOptionDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public OrgUnitType UnitType { get; set; }

    public string Path { get; set; } = "/";

    public int Depth { get; set; }
}

/// <summary>
/// 创建组织节点请求
/// </summary>
public class CreateOrgUnitRequest
{
    public int? ParentId { get; set; }

    [Required(ErrorMessage = "组织类型不能为空")]
    public OrgUnitType UnitType { get; set; }

    [Required(ErrorMessage = "组织编码不能为空")]
    [StringLength(64, ErrorMessage = "组织编码长度不能超过64")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "组织名称不能为空")]
    [StringLength(100, ErrorMessage = "组织名称长度不能超过100")]
    public string Name { get; set; } = string.Empty;

    public int Sort { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 更新组织节点请求
/// </summary>
public class UpdateOrgUnitRequest
{
    [Required(ErrorMessage = "组织编码不能为空")]
    [StringLength(64, ErrorMessage = "组织编码长度不能超过64")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "组织名称不能为空")]
    [StringLength(100, ErrorMessage = "组织名称长度不能超过100")]
    public string Name { get; set; } = string.Empty;

    public int Sort { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 移动组织节点请求。
/// </summary>
public class MoveOrgUnitRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "请选择新的上级组织")]
    public int NewParentId { get; set; }
}
