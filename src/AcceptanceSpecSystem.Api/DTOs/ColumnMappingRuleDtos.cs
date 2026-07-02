using System.ComponentModel.DataAnnotations;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.DTOs;

public class ColumnMappingRuleDto
{
    public int Id { get; set; }

    public ColumnMappingTargetField TargetField { get; set; }

    public ColumnMappingMatchMode MatchMode { get; set; }

    public string Pattern { get; set; } = string.Empty;

    [Range(-10000, 10000, ErrorMessage = "优先级必须在 -10000 到 10000 之间")]
    public int Priority { get; set; }

    public bool Enabled { get; set; }

    public ColumnMappingRuleSource Source { get; set; }

    public int? CustomerId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

public class CreateColumnMappingRuleRequest
{
    [Required(ErrorMessage = "目标字段不能为空")]
    public ColumnMappingTargetField TargetField { get; set; }

    public ColumnMappingMatchMode MatchMode { get; set; } = ColumnMappingMatchMode.Equals;

    [Required(ErrorMessage = "匹配词不能为空")]
    [MaxLength(200, ErrorMessage = "匹配词不能超过200个字符")]
    public string Pattern { get; set; } = string.Empty;

    [Range(-10000, 10000, ErrorMessage = "优先级必须在 -10000 到 10000 之间")]
    public int Priority { get; set; }

    public bool Enabled { get; set; } = true;

    public ColumnMappingRuleSource Source { get; set; } = ColumnMappingRuleSource.Manual;

    public int? CustomerId { get; set; }
}

public class UpdateColumnMappingRuleRequest
{
    [Required(ErrorMessage = "目标字段不能为空")]
    public ColumnMappingTargetField TargetField { get; set; }

    public ColumnMappingMatchMode MatchMode { get; set; } = ColumnMappingMatchMode.Equals;

    [Required(ErrorMessage = "匹配词不能为空")]
    [MaxLength(200, ErrorMessage = "匹配词不能超过200个字符")]
    public string Pattern { get; set; } = string.Empty;

    public int Priority { get; set; }

    public bool Enabled { get; set; } = true;

    public ColumnMappingRuleSource Source { get; set; } = ColumnMappingRuleSource.Manual;

    public int? CustomerId { get; set; }
}
