namespace AcceptanceSpecSystem.Core.Documents.Models;

/// <summary>
/// 表格数据
/// </summary>
public class TableData
{
    /// <summary>
    /// 表格索引
    /// </summary>
    public int TableIndex { get; set; }

    /// <summary>
    /// 表头列表
    /// </summary>
    public IList<string> Headers { get; set; } = new List<string>();

    /// <summary>
    /// 数据行列表（不含表头）
    /// </summary>
    public IList<RowData> Rows { get; set; } = new List<RowData>();

    /// <summary>
    /// 总行数（含表头）
    /// </summary>
    public int TotalRowCount => Rows.Count + (Headers.Count > 0 ? 1 : 0);

    /// <summary>
    /// 总列数
    /// </summary>
    public int ColumnCount => Headers.Count > 0 ? Headers.Count : (Rows.Count > 0 ? Rows[0].Cells.Count : 0);

    /// <summary>
    /// 原始合并单元格信息（用于写入时恢复）
    /// </summary>
    public IList<MergedCellInfo> MergedCells { get; set; } = new List<MergedCellInfo>();

    /// <summary>
    /// 获取指定单元格的值
    /// </summary>
    public string? GetValue(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || rowIndex >= Rows.Count)
            return null;
        return Rows[rowIndex].GetValue(columnIndex);
    }

    /// <summary>
    /// 设置指定单元格的值
    /// </summary>
    public void SetValue(int rowIndex, int columnIndex, string value)
    {
        if (rowIndex >= 0 && rowIndex < Rows.Count)
        {
            Rows[rowIndex].SetValue(columnIndex, value);
        }
    }
}
