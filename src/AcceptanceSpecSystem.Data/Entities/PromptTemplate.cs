namespace AcceptanceSpecSystem.Data.Entities;

public enum PromptTemplateScene
{
    Unknown = 0,
    MatchingReview = 1,
    ImportDuplicateReview = 2,
    MatchingGenerate = 3
}

/// <summary>
/// Prompt模板实体
/// </summary>
public class PromptTemplate
{
    /// <summary>
    /// 模板ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 模板名称（唯一）
    /// </summary>
    public string Name { get; set; } = "default";

    /// <summary>
    /// Prompt模板内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 模板场景
    /// </summary>
    public PromptTemplateScene Scene { get; set; }

    /// <summary>
    /// 展示名称
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 是否为系统模板
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// 是否为默认模板
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
