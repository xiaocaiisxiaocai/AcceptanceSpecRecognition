namespace AcceptanceSpecSystem.Application.Contracts;

/// <summary>
/// 文档结构模板列表项。
/// </summary>
public sealed class DocumentTemplateListItemDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string TableKind { get; set; } = "Unknown";
    public string Recommendation { get; set; } = "NeedConfirm";
    public int RegionCount { get; set; }
    public int UsageCount { get; set; }
    public bool UserModifiedStructure { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 文档结构模板详情。
/// </summary>
public sealed class DocumentTemplateDetailDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string TableKind { get; set; } = "Unknown";
    public string Recommendation { get; set; } = "NeedConfirm";
    public int UsageCount { get; set; }
    public bool UserModifiedStructure { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<DocumentTemplateRegionDto> Regions { get; set; } = [];
}

/// <summary>
/// 模板中的一个连续逻辑区域。行列索引均与解析器保持零起始语义。
/// </summary>
public sealed class DocumentTemplateRegionDto
{
    public int RegionIndex { get; set; }
    public List<string> Headers { get; set; } = [];
    public int HeaderRowIndex { get; set; }
    public int HeaderRowCount { get; set; }
    public int DataStartRowIndex { get; set; }
    public int? DataEndRowIndex { get; set; }
    public int? ProjectColumnIndex { get; set; }
    public int? SpecificationColumnIndex { get; set; }
    public int? AcceptanceColumnIndex { get; set; }
    public int? RemarkColumnIndex { get; set; }
    public bool IsSpecificationOnly { get; set; }
}
