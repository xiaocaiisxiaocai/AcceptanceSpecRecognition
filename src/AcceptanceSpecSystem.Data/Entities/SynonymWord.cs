namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 同义词实体
/// </summary>
public class SynonymWord
{
    /// <summary>
    /// 同义词ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 所属同义词组ID
    /// </summary>
    public int GroupId { get; set; }

    /// <summary>
    /// 词语内容
    /// </summary>
    public string Word { get; set; } = string.Empty;

    /// <summary>
    /// 是否为标准词（组内第一个词）
    /// </summary>
    public bool IsStandard { get; set; }

    /// <summary>
    /// 导航属性：所属同义词组
    /// </summary>
    public SynonymGroup Group { get; set; } = null!;
}
