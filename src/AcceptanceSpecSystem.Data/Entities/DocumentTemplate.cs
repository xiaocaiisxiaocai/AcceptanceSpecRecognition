namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 文档配置模板实体
/// </summary>
public class DocumentTemplate
{
    /// <summary>
    /// 模板ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 所属客户ID
    /// </summary>
    public int CustomerId { get; set; }

    /// <summary>
    /// 模板名称
    /// </summary>
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>
    /// 表头指纹（用于快速匹配，格式：header1|header2|header3）
    /// </summary>
    public string HeadersFingerprint { get; set; } = string.Empty;

    /// <summary>
    /// 原始表头（JSON 数组）
    /// </summary>
    public string HeadersJson { get; set; } = "[]";

    /// <summary>
    /// 项目列索引
    /// </summary>
    public int? ProjectColumnIndex { get; set; }

    /// <summary>
    /// 规格列索引
    /// </summary>
    public int SpecificationColumnIndex { get; set; }

    /// <summary>
    /// 验收列索引
    /// </summary>
    public int? AcceptanceColumnIndex { get; set; }

    /// <summary>
    /// 备注列索引（可选）
    /// </summary>
    public int? RemarkColumnIndex { get; set; }

    /// <summary>
    /// 表头行索引（默认 0）
    /// </summary>
    public int HeaderRowIndex { get; set; } = 0;

    /// <summary>
    /// 表头行数（默认 1）
    /// </summary>
    public int HeaderRowCount { get; set; } = 1;

    /// <summary>
    /// 数据起始行索引（默认 1）
    /// </summary>
    public int DataStartRowIndex { get; set; } = 1;

    /// <summary>
    /// 数据结束行索引（可选）
    /// </summary>
    public int? DataEndRowIndex { get; set; }

    /// <summary>
    /// 是否为仅规格模式（无项目列）
    /// </summary>
    public bool IsSpecificationOnly { get; set; }

    /// <summary>
    /// 最近一次确认时的表格类型
    /// </summary>
    public string TableKind { get; set; } = "Unknown";

    /// <summary>
    /// 最近一次确认时的推荐级别
    /// </summary>
    public string Recommendation { get; set; } = "NeedConfirm";

    /// <summary>
    /// 最近一次结构确认时间
    /// </summary>
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>
    /// 最近一次确认前用户是否手动修正过结构
    /// </summary>
    public bool UserModifiedStructure { get; set; }

    /// <summary>
    /// 使用次数
    /// </summary>
    public int UsageCount { get; set; } = 0;

    /// <summary>
    /// 最后使用时间
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 导航属性：客户
    /// </summary>
    public Customer Customer { get; set; } = null!;

    /// <summary>
    /// 导航属性：同一工作表中的有序数据区域
    /// </summary>
    public ICollection<DocumentTemplateRegion> Regions { get; set; } = new List<DocumentTemplateRegion>();
}
