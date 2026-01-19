namespace AcceptanceSpecSystem.Core.Documents.Models;

/// <summary>
/// 单元格写入操作
/// </summary>
public class CellWriteOperation
{
    /// <summary>
    /// 行索引（从0开始）
    /// </summary>
    public int RowIndex { get; set; }

    /// <summary>
    /// 列索引（从0开始）
    /// </summary>
    public int ColumnIndex { get; set; }

    /// <summary>
    /// 要写入的值
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// 原始值（用于撤销）
    /// </summary>
    public string? OriginalValue { get; set; }

    /// <summary>
    /// 是否保留原有格式
    /// </summary>
    public bool PreserveFormatting { get; set; } = true;

    /// <summary>
    /// 创建写入操作
    /// </summary>
    public static CellWriteOperation Create(int rowIndex, int columnIndex, string value, string? originalValue = null)
    {
        return new CellWriteOperation
        {
            RowIndex = rowIndex,
            ColumnIndex = columnIndex,
            Value = value,
            OriginalValue = originalValue
        };
    }
}
