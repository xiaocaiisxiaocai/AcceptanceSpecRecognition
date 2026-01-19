using System.ComponentModel.DataAnnotations;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.DTOs;

/// <summary>
/// 导入列映射规则 DTO（全局）
/// </summary>
public class ColumnMappingRuleDto
{
    public int Id { get; set; }
    public ColumnMappingTargetField TargetField { get; set; }
    public ColumnMappingMatchMode MatchMode { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool Enabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// 创建列映射规则请求
/// </summary>
public class CreateColumnMappingRuleRequest
{
    [Required(ErrorMessage = "目标字段不能为空")]
    public ColumnMappingTargetField TargetField { get; set; }

    public ColumnMappingMatchMode MatchMode { get; set; } = ColumnMappingMatchMode.Contains;

    [Required(ErrorMessage = "匹配词不能为空")]
    [StringLength(200, ErrorMessage = "匹配词不能超过200个字符")]
    public string Pattern { get; set; } = string.Empty;

    public int Priority { get; set; } = 0;

    public bool Enabled { get; set; } = true;
}

/// <summary>
/// 更新列映射规则请求
/// </summary>
public class UpdateColumnMappingRuleRequest
{
    [Required(ErrorMessage = "目标字段不能为空")]
    public ColumnMappingTargetField TargetField { get; set; }

    public ColumnMappingMatchMode MatchMode { get; set; } = ColumnMappingMatchMode.Contains;

    [Required(ErrorMessage = "匹配词不能为空")]
    [StringLength(200, ErrorMessage = "匹配词不能超过200个字符")]
    public string Pattern { get; set; } = string.Empty;

    public int Priority { get; set; } = 0;

    public bool Enabled { get; set; } = true;
}

