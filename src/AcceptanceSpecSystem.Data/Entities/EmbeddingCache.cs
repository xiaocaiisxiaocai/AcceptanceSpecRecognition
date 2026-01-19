namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 向量缓存实体
/// </summary>
public class EmbeddingCache
{
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
    /// 向量数据（序列化的float数组）
    /// </summary>
    public byte[] Vector { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 导航属性：关联的验收规格
    /// </summary>
    public AcceptanceSpec Spec { get; set; } = null!;
}
