namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 执行记录实体（智能填充 / 批量回复）
/// </summary>
public class ExecutionHistoryRecord
{
    /// <summary>
    /// 主键
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 业务任务ID
    /// </summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// 任务类型：smart-fill / batch-reply
    /// </summary>
    public string TaskType { get; set; } = string.Empty;

    /// <summary>
    /// 来源文件ID（批量回复来源为临时文件时可为空）
    /// </summary>
    public int? SourceFileId { get; set; }

    /// <summary>
    /// 来源文件名称
    /// </summary>
    public string SourceFileName { get; set; } = string.Empty;

    /// <summary>
    /// 来源文件类型
    /// </summary>
    public UploadedFileType? SourceFileType { get; set; }

    /// <summary>
    /// 文件数量
    /// </summary>
    public int FileCount { get; set; }

    /// <summary>
    /// 总行数
    /// </summary>
    public int TotalRowCount { get; set; }

    /// <summary>
    /// 已匹配行数
    /// </summary>
    public int MatchedRowCount { get; set; }

    /// <summary>
    /// 已采用行数
    /// </summary>
    public int AdoptedRowCount { get; set; }

    /// <summary>
    /// 未匹配行数
    /// </summary>
    public int UnmatchedRowCount { get; set; }

    /// <summary>
    /// 已跳过行数
    /// </summary>
    public int SkippedRowCount { get; set; }

    /// <summary>
    /// 未采用行数
    /// </summary>
    public int NotAdoptedRowCount { get; set; }

    /// <summary>
    /// 人工选择行数
    /// </summary>
    public int ManualSelectedRowCount { get; set; }

    /// <summary>
    /// 详情 JSON
    /// </summary>
    public string DetailJson { get; set; } = string.Empty;

    /// <summary>
    /// 创建记录的用户ID
    /// </summary>
    public int? CreatedByUserId { get; set; }

    /// <summary>
    /// 创建记录时的公司ID
    /// </summary>
    public int? CompanyId { get; set; }

    /// <summary>
    /// 业务执行发生时的归属组织快照。
    /// </summary>
    public int? OwnerOrgUnitId { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
