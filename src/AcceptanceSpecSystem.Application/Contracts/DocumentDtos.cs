using System.ComponentModel.DataAnnotations;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Contracts;

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

    /// <summary>
    /// 表格数量是否已完成读取
    /// </summary>
    public bool TableCountReady { get; set; } = true;

    /// <summary>
    /// 本次导入或智能填充任务的业务归属组织。
    /// </summary>
    public int OwnerOrgUnitId { get; set; }

    public string OwnerOrgUnitName { get; set; } = string.Empty;
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

    /// <summary>
    /// 当前窗口相对于数据起始行的偏移量（0-based）。
    /// </summary>
    public int RowOffset { get; set; }

    /// <summary>
    /// 当前窗口相对于表格起始列的偏移量（0-based）。
    /// </summary>
    public int ColumnOffset { get; set; }

    /// <summary>
    /// 表格总列数。ColumnCount 仅表示当前响应窗口的列数。
    /// </summary>
    public int TotalColumns { get; set; }
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
    /// <summary>客户端生成的幂等键；同一用户下相同键与请求内容的重试返回既有结果。</summary>
    [StringLength(80)]
    public string? ExecutionRequestId { get; set; }

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
    public int? ProcessId { get; set; }

    /// <summary>
    /// 目标机型ID
    /// </summary>
    public int? MachineModelId { get; set; }

    /// <summary>
    /// 列映射配置
    /// </summary>
    [Required(ErrorMessage = "列映射配置不能为空")]
    public ColumnMappingDto Mapping { get; set; } = new();

    /// <summary>多区域导入的稳定区域标识。</summary>
    [StringLength(100)]
    public string? RegionId { get; set; }

    /// <summary>Word 多行表头行数。</summary>
    [Range(1, 100)]
    public int HeaderRowCount { get; set; } = 1;

    /// <summary>Word 数据结束行，表格内 0-based 闭区间。</summary>
    public int? DataEndRowIndex { get; set; }

    /// <summary>
    /// 是否在本次导入后清理源文件。
    /// 多表格/多工作表分批导入时，建议仅最后一次请求传 true。
    /// </summary>
    public bool CleanupSourceFile { get; set; } = true;

    /// <summary>
    /// 是否返回“未导入（跳过）”明细（默认不返回，减少响应体）
    /// </summary>
    public bool PreviewSkippedRows { get; set; } = false;

    /// <summary>
    /// 是否确认本次为仅规格导入；缺项目列时允许用规格内容回填项目。
    /// </summary>
    public bool IsSpecificationOnly { get; set; } = false;

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

/// <summary>
/// 导入阶段 AI 疑似重复识别配置
/// </summary>
public class ImportDuplicateCheckOptions
{
    /// <summary>
    /// 是否启用 AI 疑似重复识别
    /// </summary>
    public bool EnableSemanticDuplicateCheck { get; set; } = false;

    /// <summary>
    /// Embedding 服务 ID（可选）
    /// </summary>
    public int? EmbeddingServiceId { get; set; }

    /// <summary>
    /// 语义召回候选数
    /// </summary>
    public int SemanticTopK { get; set; } = 3;

    /// <summary>
    /// Embedding 最小候选阈值
    /// </summary>
    public double SemanticMinScore { get; set; } = 0.75;

    /// <summary>
    /// 是否启用 LLM 复核
    /// </summary>
    public bool EnableLlmDuplicateReview { get; set; } = false;

    /// <summary>
    /// LLM 服务 ID（可选）
    /// </summary>
    public int? LlmServiceId { get; set; }

    /// <summary>
    /// LLM 通过阈值（0-1）
    /// </summary>
    public double LlmPassScore { get; set; } = 0.9;

    /// <summary>
    /// 高置信展示阈值（0-1）
    /// </summary>
    public double HighConfidenceThreshold { get; set; } = 0.95;
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

    /// <summary>
    /// 未导入（跳过）明细（按请求决定是否返回）
    /// </summary>
    public List<ImportSkippedRow> SkippedRows { get; set; } = [];

    /// <summary>
    /// 是否需要用户确认差异后再导入
    /// </summary>
    public bool RequiresConfirmation { get; set; }

    /// <summary>
    /// 待确认差异数量
    /// </summary>
    public int PendingCount { get; set; }

    /// <summary>
    /// 待确认差异明细
    /// </summary>
    public List<ImportPendingDifference> PendingDifferences { get; set; } = [];

    /// <summary>
    /// 本次导入是否存在“项目由规格补齐”的行。
    /// </summary>
    public bool ProjectBackfilledFromSpecification { get; set; }
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

/// <summary>
/// 未导入（跳过）明细
/// </summary>
public class ImportSkippedRow
{
    /// <summary>
    /// 行号
    /// </summary>
    public int RowIndex { get; set; }

    /// <summary>
    /// 跳过原因
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 整行数据（按列顺序）
    /// </summary>
    public List<string> RowValues { get; set; } = [];
}

/// <summary>
/// 待确认差异明细
/// </summary>
public class ImportPendingDifference
{
    /// <summary>
    /// 差异键（前端回传该键用于确认导入/跳过）
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 命中类型：exact / conflict / semantic
    /// </summary>
    public string MatchType { get; set; } = "conflict";

    /// <summary>
    /// 行号
    /// </summary>
    public int RowIndex { get; set; }

    /// <summary>
    /// 整行数据（按列顺序）
    /// </summary>
    public List<string> RowValues { get; set; } = [];

    /// <summary>
    /// 导入数据：项目
    /// </summary>
    public string IncomingProject { get; set; } = string.Empty;

    /// <summary>
    /// 导入数据：规格
    /// </summary>
    public string IncomingSpecification { get; set; } = string.Empty;

    /// <summary>
    /// 导入数据：验收标准
    /// </summary>
    public string? IncomingAcceptance { get; set; }

    /// <summary>
    /// 导入数据：备注
    /// </summary>
    public string? IncomingRemark { get; set; }

    /// <summary>
    /// 库中已有记录ID
    /// </summary>
    public int ExistingSpecId { get; set; }

    /// <summary>
    /// 库中已有：项目
    /// </summary>
    public string ExistingProject { get; set; } = string.Empty;

    /// <summary>
    /// 库中已有：规格
    /// </summary>
    public string ExistingSpecification { get; set; } = string.Empty;

    /// <summary>
    /// 库中已有：验收标准
    /// </summary>
    public string? ExistingAcceptance { get; set; }

    /// <summary>
    /// 库中已有：备注
    /// </summary>
    public string? ExistingRemark { get; set; }

    /// <summary>
    /// Embedding 相似度（0-1）
    /// </summary>
    public double? EmbeddingScore { get; set; }

    /// <summary>
    /// LLM 复核得分（0-1）
    /// </summary>
    public double? LlmScore { get; set; }

    /// <summary>
    /// 最终判定得分（0-1）
    /// </summary>
    public double? FinalScore { get; set; }

    /// <summary>
    /// 是否达到高置信阈值
    /// </summary>
    public bool IsHighConfidence { get; set; }

    /// <summary>
    /// 复核理由
    /// </summary>
    public string? ReviewReason { get; set; }

    /// <summary>
    /// 复核说明
    /// </summary>
    public string? ReviewCommentary { get; set; }
}
