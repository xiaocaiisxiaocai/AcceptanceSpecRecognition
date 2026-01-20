namespace AcceptanceSpecSystem.Core.Documents.Models;

/// <summary>
/// 表格基本信息
/// </summary>
public class TableInfo
{
    /// <summary>
    /// 表格在文档中的索引（从0开始）
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// 表格名称（Word：通常为空；Excel：工作表名称）
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 表格行数
    /// </summary>
    public int RowCount { get; set; }

    /// <summary>
    /// 表格列数（最大列数）
    /// </summary>
    public int ColumnCount { get; set; }

    /// <summary>
    /// 是否为嵌套表格
    /// </summary>
    public bool IsNested { get; set; }

    /// <summary>
    /// 父表格索引（如果是嵌套表格）
    /// </summary>
    public int? ParentTableIndex { get; set; }

    /// <summary>
    /// 表格预览文本（首行内容，用于识别）
    /// </summary>
    public string? PreviewText { get; set; }

    /// <summary>
    /// 表头行（如果可识别）
    /// </summary>
    public IReadOnlyList<string>? Headers { get; set; }

    /// <summary>
    /// 是否包含合并单元格
    /// </summary>
    public bool HasMergedCells { get; set; }

    /// <summary>
    /// 已用区域起始行（Excel 使用；Word 通常为 0）
    /// </summary>
    public int UsedRangeStartRow { get; set; }

    /// <summary>
    /// 已用区域起始列（Excel 使用；Word 通常为 0）
    /// </summary>
    public int UsedRangeStartColumn { get; set; }
}
