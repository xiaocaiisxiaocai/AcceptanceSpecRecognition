namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 验收规格实体
/// </summary>
public class AcceptanceSpec
{
    /// <summary>
    /// 验收规格ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 所属客户ID（用于定义“客户 + 制程 = 一整份验规”的组合归属）
    /// </summary>
    public int CustomerId { get; set; }

    /// <summary>
    /// 所属制程ID
    /// </summary>
    public int ProcessId { get; set; }

    /// <summary>
    /// 项目名称
    /// </summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>
    /// 规格内容
    /// </summary>
    public string Specification { get; set; } = string.Empty;

    /// <summary>
    /// 验收标准（可为空）
    /// </summary>
    public string? Acceptance { get; set; }

    /// <summary>
    /// 备注（可为空）
    /// </summary>
    public string? Remark { get; set; }

    /// <summary>
    /// 来源Word文件ID
    /// </summary>
    public int WordFileId { get; set; }

    /// <summary>
    /// 导入时间
    /// </summary>
    public DateTime ImportedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 导航属性：所属客户
    /// </summary>
    public Customer Customer { get; set; } = null!;

    /// <summary>
    /// 导航属性：所属制程
    /// </summary>
    public Process Process { get; set; } = null!;

    /// <summary>
    /// 导航属性：来源Word文件
    /// </summary>
    public WordFile WordFile { get; set; } = null!;

    /// <summary>
    /// 导航属性：该规格的向量缓存
    /// </summary>
    public ICollection<EmbeddingCache> EmbeddingCaches { get; set; } = new List<EmbeddingCache>();
}
