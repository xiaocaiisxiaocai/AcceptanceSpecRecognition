namespace AcceptanceSpecSystem.Core.Documents.Models;

/// <summary>
/// 行数据
/// </summary>
public class RowData
{
    /// <summary>
    /// 行索引（从0开始）
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// 单元格列表
    /// </summary>
    public IList<CellData> Cells { get; set; } = new List<CellData>();

    /// <summary>
    /// 是否为表头行
    /// </summary>
    public bool IsHeader { get; set; }

    /// <summary>
    /// 获取指定列的值
    /// </summary>
    public string? GetValue(int columnIndex)
    {
        var cell = Cells.FirstOrDefault(c => c.ColumnIndex == columnIndex);
        return cell?.Value;
    }

    /// <summary>
    /// 设置指定列的值
    /// </summary>
    public void SetValue(int columnIndex, string value)
    {
        var cell = Cells.FirstOrDefault(c => c.ColumnIndex == columnIndex);
        if (cell != null)
        {
            cell.Value = value;
        }
        else
        {
            Cells.Add(new CellData
            {
                RowIndex = Index,
                ColumnIndex = columnIndex,
                Value = value
            });
        }
    }
}
