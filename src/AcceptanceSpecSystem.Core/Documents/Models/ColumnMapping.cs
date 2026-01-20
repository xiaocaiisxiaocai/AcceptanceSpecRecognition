namespace AcceptanceSpecSystem.Core.Documents.Models;

/// <summary>
/// 列映射配置
/// </summary>
public class ColumnMapping
{
    /// <summary>
    /// 项目列索引
    /// </summary>
    public int? ProjectColumn { get; set; }

    /// <summary>
    /// 规格列索引
    /// </summary>
    public int? SpecificationColumn { get; set; }

    /// <summary>
    /// 验收列索引
    /// </summary>
    public int? AcceptanceColumn { get; set; }

    /// <summary>
    /// 备注列索引
    /// </summary>
    public int? RemarkColumn { get; set; }

    /// <summary>
    /// 表头行索引（从0开始，默认为0表示第一行是表头）
    /// </summary>
    public int HeaderRowIndex { get; set; } = 0;

    /// <summary>
    /// 表头行数（默认 1）。Excel 常见“表头跨行”场景可配置为 2~N，用于组合生成列标题。
    /// </summary>
    public int HeaderRowCount { get; set; } = 1;

    /// <summary>
    /// 数据起始行索引（从0开始，默认为1表示从第二行开始是数据）
    /// </summary>
    public int DataStartRowIndex { get; set; } = 1;

    /// <summary>
    /// 验证映射是否有效
    /// </summary>
    public bool IsValid()
    {
        // 至少需要项目列和规格列
        return ProjectColumn.HasValue && SpecificationColumn.HasValue;
    }

    /// <summary>
    /// 获取所有已映射的列索引
    /// </summary>
    public IEnumerable<int> GetMappedColumns()
    {
        if (ProjectColumn.HasValue) yield return ProjectColumn.Value;
        if (SpecificationColumn.HasValue) yield return SpecificationColumn.Value;
        if (AcceptanceColumn.HasValue) yield return AcceptanceColumn.Value;
        if (RemarkColumn.HasValue) yield return RemarkColumn.Value;
    }
}
