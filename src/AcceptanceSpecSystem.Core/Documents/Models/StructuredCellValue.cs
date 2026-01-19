namespace AcceptanceSpecSystem.Core.Documents.Models;

/// <summary>
/// 结构化单元格值：用于表达“文本 + 嵌套表格”等复杂内容
/// </summary>
public class StructuredCellValue
{
    /// <summary>
    /// 该单元格的内容片段（按出现顺序）
    /// </summary>
    public List<StructuredCellPart> Parts { get; set; } = [];
}

/// <summary>
/// 单元格内容片段（文本或嵌套表格）
/// </summary>
public class StructuredCellPart
{
    /// <summary>
    /// 片段类型：text / table
    /// </summary>
    public string Type { get; set; } = "text";

    /// <summary>
    /// 文本内容（Type=text 时使用）
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// 嵌套表格内容（Type=table 时使用）
    /// </summary>
    public StructuredTableValue? Table { get; set; }
}

/// <summary>
/// 结构化表格值（用于嵌套表格）
/// </summary>
public class StructuredTableValue
{
    /// <summary>
    /// 行数
    /// </summary>
    public int RowCount { get; set; }

    /// <summary>
    /// 列数（最大列数）
    /// </summary>
    public int ColumnCount { get; set; }

    /// <summary>
    /// 单元格二维数组：Rows[row][col]
    /// </summary>
    public List<List<StructuredCellValue>> Rows { get; set; } = [];
}

