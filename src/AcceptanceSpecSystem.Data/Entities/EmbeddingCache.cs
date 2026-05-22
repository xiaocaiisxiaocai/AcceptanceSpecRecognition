namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 向量缓存实体
/// </summary>
public class EmbeddingCache
{
    /// <summary>
    /// 默认用途：验收规格匹配
    /// </summary>
    public const string DefaultUsage = "matching";

    /// <summary>
    /// 缓存ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 关联的验收规格ID
    /// </summary>
    public int SpecId { get; set; }

    /// <summary>
    /// 使用的模型名称
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// 向量用途
    /// </summary>
    public string Usage { get; set; } = DefaultUsage;

    /// <summary>
    /// 源文本指纹
    /// </summary>
    public string TextHash { get; set; } = string.Empty;

    /// <summary>
    /// 向量数据（序列化的float数组）
    /// </summary>
    public byte[] Vector { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 过期时间（可选，用于缓存失效策略）
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// 模型版本（用于模型升级时批量失效旧缓存）
    /// </summary>
    public string? ModelVersion { get; set; }

    /// <summary>
    /// 导航属性：关联的验收规格
    /// </summary>
    public AcceptanceSpec Spec { get; set; } = null!;
}
