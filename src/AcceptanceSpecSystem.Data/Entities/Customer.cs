namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 客户实体
/// </summary>
public class Customer
{
    /// <summary>
    /// 客户ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 客户名称（唯一）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 导航属性：该客户下的所有验收规格（按 CustomerId 归属）
    /// </summary>
    public ICollection<AcceptanceSpec> AcceptanceSpecs { get; set; } = new List<AcceptanceSpec>();
}
