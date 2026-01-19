namespace AcceptanceSpecSystem.Core.Documents.Models;

/// <summary>
/// 合并单元格信息（用于记录原始合并状态，写入时恢复）
/// </summary>
public class MergedCellInfo
{
    /// <summary>
    /// 起始行索引
    /// </summary>
    public int StartRow { get; set; }

    /// <summary>
    /// 起始列索引
    /// </summary>
    public int StartColumn { get; set; }

    /// <summary>
    /// 结束行索引
    /// </summary>
    public int EndRow { get; set; }

    /// <summary>
    /// 结束列索引
    /// </summary>
    public int EndColumn { get; set; }

    /// <summary>
    /// 行跨度
    /// </summary>
    public int RowSpan => EndRow - StartRow + 1;

    /// <summary>
    /// 列跨度
    /// </summary>
    public int ColSpan => EndColumn - StartColumn + 1;

    /// <summary>
    /// 是否为垂直合并（跨行）
    /// </summary>
    public bool IsVerticalMerge => RowSpan > 1;

    /// <summary>
    /// 是否为水平合并（跨列）
    /// </summary>
    public bool IsHorizontalMerge => ColSpan > 1;
}
