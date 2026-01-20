using System.ComponentModel.DataAnnotations;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.DTOs;

/// <summary>
/// Word文件信息DTO
/// </summary>
public class WordFileDto
{
    /// <summary>
    /// 文件ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 原始文件名
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件类型（Word/Excel）
    /// </summary>
    public UploadedFileType FileType { get; set; } = UploadedFileType.WordDocx;

    /// <summary>
    /// 文件哈希值
    /// </summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>
    /// 上传时间
    /// </summary>
    public DateTime UploadedAt { get; set; }

    /// <summary>
    /// 导入的规格数量
    /// </summary>
    public int SpecCount { get; set; }
}

/// <summary>
/// 文件上传响应
/// </summary>
public class FileUploadResponse
{
    /// <summary>
    /// 文件ID
    /// </summary>
    public int FileId { get; set; }

    /// <summary>
    /// 文件名
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件类型（Word/Excel）
    /// </summary>
    public UploadedFileType FileType { get; set; } = UploadedFileType.WordDocx;

    /// <summary>
    /// 文件哈希
    /// </summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>
    /// 是否为重复文件
    /// </summary>
    public bool IsDuplicate { get; set; }

    /// <summary>
    /// 表格数量
    /// </summary>
    public int TableCount { get; set; }
}

/// <summary>
/// 表格信息DTO
/// </summary>
public class TableInfoDto
{
    /// <summary>
    /// 表格索引（从0开始）
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// 名称（Excel：工作表名称；Word：通常为空）
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 表格行数
    /// </summary>
    public int RowCount { get; set; }

    /// <summary>
    /// 表格列数
    /// </summary>
    public int ColumnCount { get; set; }

    /// <summary>
    /// 是否为嵌套表格
    /// </summary>
    public bool IsNested { get; set; }

    /// <summary>
    /// 预览文本
    /// </summary>
    public string? PreviewText { get; set; }

    /// <summary>
    /// 表头列表
    /// </summary>
    public List<string> Headers { get; set; } = [];

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

/// <summary>
/// 表格数据DTO
/// </summary>
public class TableDataDto
{
    /// <summary>
    /// 表格索引
    /// </summary>
    public int TableIndex { get; set; }

    /// <summary>
    /// 表头列表
    /// </summary>
    public List<string> Headers { get; set; } = [];

    /// <summary>
    /// 数据行
    /// </summary>
    public List<List<string>> Rows { get; set; } = [];

    /// <summary>
    /// 结构化数据行（用于表达单元格内嵌套表格等复杂内容）。与 Rows 行列对齐。
    /// </summary>
    public List<List<StructuredCellValueDto>> StructuredRows { get; set; } = [];

    /// <summary>
    /// 总行数
    /// </summary>
    public int TotalRows { get; set; }

    /// <summary>
    /// 列数
    /// </summary>
    public int ColumnCount { get; set; }
}

/// <summary>
/// 结构化单元格值DTO
/// </summary>
public class StructuredCellValueDto
{
    /// <summary>
    /// 内容片段（按出现顺序）
    /// </summary>
    public List<StructuredCellPartDto> Parts { get; set; } = [];
}

/// <summary>
/// 单元格内容片段DTO
/// </summary>
public class StructuredCellPartDto
{
    /// <summary>
    /// 片段类型：text / table
    /// </summary>
    public string Type { get; set; } = "text";

    /// <summary>
    /// 文本内容（Type=text）
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// 嵌套表格内容（Type=table）
    /// </summary>
    public StructuredTableValueDto? Table { get; set; }
}

/// <summary>
/// 结构化表格DTO（用于嵌套表格）
/// </summary>
public class StructuredTableValueDto
{
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public List<List<StructuredCellValueDto>> Rows { get; set; } = [];
}

/// <summary>
/// 表格预览请求
/// </summary>
public class TablePreviewRequest
{
    /// <summary>
    /// 表格索引
    /// </summary>
    [Required]
    public int TableIndex { get; set; }

    /// <summary>
    /// 预览行数（默认10行）
    /// </summary>
    public int PreviewRows { get; set; } = 10;

    /// <summary>
    /// 表头行索引（默认0）
    /// </summary>
    public int HeaderRowIndex { get; set; } = 0;

    /// <summary>
    /// 数据起始行索引（默认1）
    /// </summary>
    public int DataStartRowIndex { get; set; } = 1;
}

/// <summary>
/// 列映射配置DTO
/// </summary>
public class ColumnMappingDto
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
    /// 表头行索引
    /// </summary>
    public int HeaderRowIndex { get; set; } = 0;

    /// <summary>
    /// 数据起始行索引
    /// </summary>
    public int DataStartRowIndex { get; set; } = 1;
}

/// <summary>
/// 导入数据请求
/// </summary>
public class ImportDataRequest
{
    /// <summary>
    /// 文件ID
    /// </summary>
    [Required(ErrorMessage = "文件ID不能为空")]
    public int FileId { get; set; }

    /// <summary>
    /// 表格索引
    /// </summary>
    [Required(ErrorMessage = "表格索引不能为空")]
    public int TableIndex { get; set; }

    /// <summary>
    /// 目标客户ID
    /// </summary>
    [Required(ErrorMessage = "客户ID不能为空")]
    public int CustomerId { get; set; }

    /// <summary>
    /// 目标制程ID
    /// </summary>
    [Required(ErrorMessage = "制程ID不能为空")]
    public int ProcessId { get; set; }

    /// <summary>
    /// 列映射配置
    /// </summary>
    [Required(ErrorMessage = "列映射配置不能为空")]
    public ColumnMappingDto Mapping { get; set; } = new();
}

/// <summary>
/// 导入结果
/// </summary>
public class ImportResult
{
    /// <summary>
    /// 成功数量
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// 失败数量
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// 跳过数量（空行等）
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// 总行数
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 错误详情
    /// </summary>
    public List<ImportError> Errors { get; set; } = [];
}

/// <summary>
/// 导入错误详情
/// </summary>
public class ImportError
{
    /// <summary>
    /// 行号
    /// </summary>
    public int RowIndex { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
