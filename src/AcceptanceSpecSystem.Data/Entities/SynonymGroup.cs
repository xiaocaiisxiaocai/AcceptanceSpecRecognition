namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 同义词组实体
/// </summary>
public class SynonymGroup
{
    /// <summary>
    /// 同义词组ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// 导航属性：组内的所有同义词
    /// </summary>
    public ICollection<SynonymWord> Words { get; set; } = new List<SynonymWord>();
}
