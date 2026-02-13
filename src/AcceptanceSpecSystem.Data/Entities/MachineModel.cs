namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 机型实体
/// </summary>
public class MachineModel
{
    /// <summary>
    /// 机型ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 机型名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 导航属性：该机型下的所有验收规格
    /// </summary>
    public ICollection<AcceptanceSpec> AcceptanceSpecs { get; set; } = new List<AcceptanceSpec>();
}
