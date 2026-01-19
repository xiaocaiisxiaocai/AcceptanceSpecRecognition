namespace AcceptanceSpecSystem.Core.Documents.Models;

/// <summary>
/// 单元格数据
/// </summary>
public class CellData
{
    /// <summary>
    /// 单元格值
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// 结构化单元格值（用于表达嵌套表格等复杂内容）。可为空，表示未提供结构化信息。
    /// </summary>
    public StructuredCellValue? StructuredValue { get; set; }

    /// <summary>
    /// 行索引（从0开始）
    /// </summary>
    public int RowIndex { get; set; }

    /// <summary>
    /// 列索引（从0开始）
    /// </summary>
    public int ColumnIndex { get; set; }

    /// <summary>
    /// 是否为合并单元格的一部分
    /// </summary>
    public bool IsMerged { get; set; }

    /// <summary>
    /// 是否为合并单元格的起始单元格
    /// </summary>
    public bool IsMergeStart { get; set; }

    /// <summary>
    /// 合并单元格的行跨度
    /// </summary>
    public int RowSpan { get; set; } = 1;

    /// <summary>
    /// 合并单元格的列跨度
    /// </summary>
    public int ColSpan { get; set; } = 1;
}
