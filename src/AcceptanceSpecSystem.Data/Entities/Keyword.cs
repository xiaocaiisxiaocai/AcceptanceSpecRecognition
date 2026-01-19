namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 关键字实体
/// </summary>
public class Keyword
{
    /// <summary>
    /// 关键字ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 关键字内容（唯一）
    /// </summary>
    public string Word { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
