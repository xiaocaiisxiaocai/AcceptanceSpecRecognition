namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 制程实体
/// </summary>
public class Process
{
    /// <summary>
    /// 制程ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 制程名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 导航属性：该制程下的所有验收规格
    /// </summary>
    public ICollection<AcceptanceSpec> AcceptanceSpecs { get; set; } = new List<AcceptanceSpec>();
}
