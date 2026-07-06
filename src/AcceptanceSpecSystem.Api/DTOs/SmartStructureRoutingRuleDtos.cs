using System.ComponentModel.DataAnnotations;

namespace AcceptanceSpecSystem.Api.DTOs;

public sealed class SmartStructureRoutingRuleDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string TableKind { get; set; } = "Unknown";

    public string Recommendation { get; set; } = "NeedConfirm";

    public string MatchScope { get; set; } = "Any";

    public string MatchMode { get; set; } = "Contains";

    public string Pattern { get; set; } = string.Empty;

    public double Weight { get; set; }

    public int Priority { get; set; }

    public bool Enabled { get; set; }

    public string Source { get; set; } = "Manual";

    public int? CustomerId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

public class CreateSmartStructureRoutingRuleRequest
{
    [Required(ErrorMessage = "规则名称不能为空")]
    [MaxLength(100, ErrorMessage = "规则名称不能超过100个字符")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "表格类型不能为空")]
    [MaxLength(50, ErrorMessage = "表格类型不能超过50个字符")]
    public string TableKind { get; set; } = "Unknown";

    [Required(ErrorMessage = "推荐结果不能为空")]
    [MaxLength(50, ErrorMessage = "推荐结果不能超过50个字符")]
    public string Recommendation { get; set; } = "NeedConfirm";

    public string MatchScope { get; set; } = "Any";

    public string MatchMode { get; set; } = "Contains";

    [Required(ErrorMessage = "匹配词不能为空")]
    [MaxLength(500, ErrorMessage = "匹配词不能超过500个字符")]
    public string Pattern { get; set; } = string.Empty;

    [Range(0, 10, ErrorMessage = "权重必须在 0 到 10 之间")]
    public double Weight { get; set; } = 1;

    [Range(-10000, 10000, ErrorMessage = "优先级必须在 -10000 到 10000 之间")]
    public int Priority { get; set; }

    public bool Enabled { get; set; } = true;

    public string Source { get; set; } = "Manual";

    public int? CustomerId { get; set; }
}

public sealed class UpdateSmartStructureRoutingRuleRequest : CreateSmartStructureRoutingRuleRequest
{
}
