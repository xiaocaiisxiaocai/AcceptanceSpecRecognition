using System.ComponentModel.DataAnnotations;

namespace AcceptanceSpecSystem.Api.DTOs;

/// <summary>
/// Excel 导入请求（按列序号配置，列号/行号均为 1-based）
/// </summary>
public class ExcelImportDataRequest
{
    /// <summary>
    /// 文件ID
    /// </summary>
    [Required(ErrorMessage = "文件ID不能为空")]
    public int FileId { get; set; }

    /// <summary>
    /// 工作表索引（0-based，与工作表列表返回保持一致）
    /// </summary>
    [Required(ErrorMessage = "工作表索引不能为空")]
    public int SheetIndex { get; set; }

    /// <summary>
    /// 目标客户ID
    /// </summary>
    [Required(ErrorMessage = "客户ID不能为空")]
    public int CustomerId { get; set; }

    /// <summary>
    /// 目标制程ID
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// 目标机型ID
    /// </summary>
    public int? MachineModelId { get; set; }

    /// <summary>
    /// 表头起始行（1-based）
    /// </summary>
    public int HeaderRowStart { get; set; } = 1;

    /// <summary>
    /// 表头行数（允许为 0；0 表示不关心表头，仅用于预览展示）
    /// </summary>
    public int HeaderRowCount { get; set; } = 1;

    /// <summary>
    /// 数据起始行（1-based）
    /// </summary>
    public int DataStartRow { get; set; } = 2;

    /// <summary>
    /// 项目列（必填，1-based；第 1 列为 A）
    /// </summary>
    [Required(ErrorMessage = "项目列不能为空")]
    public int ProjectColumn { get; set; }

    /// <summary>
    /// 规格内容列（必填，1-based；第 1 列为 A）
    /// </summary>
    [Required(ErrorMessage = "规格内容列不能为空")]
    public int SpecificationColumn { get; set; }

    /// <summary>
    /// 验收标准列（可选，1-based）
    /// </summary>
    public int? AcceptanceColumn { get; set; }

    /// <summary>
    /// 备注列（可选，1-based）
    /// </summary>
    public int? RemarkColumn { get; set; }

    /// <summary>
    /// 是否在本次导入后清理源文件。
    /// 多工作表分批导入时，建议仅最后一次请求传 true。
    /// </summary>
    public bool CleanupSourceFile { get; set; } = true;

    /// <summary>
    /// 是否返回“未导入（跳过）”明细（默认不返回，减少响应体）
    /// </summary>
    public bool PreviewSkippedRows { get; set; } = false;

    /// <summary>
    /// 差异行中“确认导入”的键集合（用于二次确认提交）
    /// </summary>
    public List<string> ConfirmedDifferenceKeys { get; set; } = [];

    /// <summary>
    /// 差异行中“确认跳过”的键集合（用于二次确认提交）
    /// </summary>
    public List<string> SkippedDifferenceKeys { get; set; } = [];
}
