namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 操作历史实体
/// </summary>
public class OperationHistory
{
    /// <summary>
    /// 历史记录ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 操作类型
    /// </summary>
    public OperationType OperationType { get; set; }

    /// <summary>
    /// 目标文件路径（可为空）
    /// </summary>
    public string? TargetFile { get; set; }

    /// <summary>
    /// 操作详情（JSON格式）
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// 是否可以撤销
    /// </summary>
    public bool CanUndo { get; set; } = true;

    /// <summary>
    /// 撤销所需的数据（JSON格式）
    /// </summary>
    public string? UndoData { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
