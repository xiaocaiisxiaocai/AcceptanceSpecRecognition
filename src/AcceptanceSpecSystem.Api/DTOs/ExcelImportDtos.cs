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
    [Range(1, int.MaxValue, ErrorMessage = "文件ID必须大于0")]
    public int FileId { get; set; }

    /// <summary>
    /// 工作表索引（0-based，与工作表列表返回保持一致）
    /// </summary>
    [Required(ErrorMessage = "工作表索引不能为空")]
    [Range(0, int.MaxValue, ErrorMessage = "工作表索引不能小于0")]
    public int SheetIndex { get; set; }

    /// <summary>
    /// 目标客户ID
    /// </summary>
    [Required(ErrorMessage = "客户ID不能为空")]
    [Range(1, int.MaxValue, ErrorMessage = "客户ID必须大于0")]
    public int CustomerId { get; set; }

    /// <summary>
    /// 目标制程ID
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "制程ID必须大于0")]
    public int? ProcessId { get; set; }

    /// <summary>
    /// 目标机型ID
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "机型ID必须大于0")]
    public int? MachineModelId { get; set; }

    /// <summary>
    /// 表头起始行（1-based）
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "表头起始行必须大于0")]
    public int HeaderRowStart { get; set; } = 1;

    /// <summary>
    /// 表头行数（允许为 0；0 表示不关心表头，仅用于预览展示）
    /// </summary>
    [Range(0, 100, ErrorMessage = "表头行数必须在 0 到 100 之间")]
    public int HeaderRowCount { get; set; } = 1;

    /// <summary>
    /// 数据起始行（1-based）
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "数据起始行必须大于0")]
    public int DataStartRow { get; set; } = 2;

    /// <summary>
    /// 数据结束行（1-based，可选；未传则默认读取到已用区域末行）
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "数据结束行必须大于0")]
    public int? DataEndRow { get; set; }

    /// <summary>
    /// 项目列（必填，1-based；第 1 列为 A）
    /// </summary>
    [Required(ErrorMessage = "项目列不能为空")]
    [Range(1, int.MaxValue, ErrorMessage = "项目列必须大于0")]
    public int ProjectColumn { get; set; }

    /// <summary>
    /// 规格内容列（必填，1-based；第 1 列为 A）
    /// </summary>
    [Required(ErrorMessage = "规格内容列不能为空")]
    [Range(1, int.MaxValue, ErrorMessage = "规格内容列必须大于0")]
    public int SpecificationColumn { get; set; }

    /// <summary>
    /// 验收标准列（可选，1-based）
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "验收标准列必须大于0")]
    public int? AcceptanceColumn { get; set; }

    /// <summary>
    /// 备注列（可选，1-based）
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "备注列必须大于0")]
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
    /// 差异行中“部分覆盖”的键集合（仅覆盖验收标准与备注）
    /// </summary>
    public List<string> PartiallyConfirmedDifferenceKeys { get; set; } = [];

    /// <summary>
    /// 差异行中“确认跳过”的键集合（用于二次确认提交）
    /// </summary>
    public List<string> SkippedDifferenceKeys { get; set; } = [];

    /// <summary>
    /// 本次导入前由用户手动剔除的数据行索引（基于解析后的数据区，0-based）
    /// </summary>
    public List<int> ExcludedRowIndexes { get; set; } = [];

    /// <summary>
    /// AI 疑似重复识别配置
    /// </summary>
    public ImportDuplicateCheckOptions DuplicateCheckOptions { get; set; } = new();
}
