using System.ComponentModel.DataAnnotations;

namespace AcceptanceSpecSystem.Api.DTOs;

/// <summary>
/// 验收规格数据传输对象
/// </summary>
public class AcceptanceSpecDto
{
    /// <summary>
    /// 验收规格ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 所属客户ID
    /// </summary>
    public int CustomerId { get; set; }

    /// <summary>
    /// 所属制程ID
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// 所属机型ID
    /// </summary>
    public int? MachineModelId { get; set; }

    /// <summary>
    /// 所属制程名称
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// 所属机型名称
    /// </summary>
    public string MachineModelName { get; set; } = string.Empty;

    /// <summary>
    /// 所属客户名称
    /// </summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>
    /// 项目名称
    /// </summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>
    /// 规格内容
    /// </summary>
    public string Specification { get; set; } = string.Empty;

    /// <summary>
    /// 验收标准
    /// </summary>
    public string? Acceptance { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Remark { get; set; }

    /// <summary>
    /// 导入时间
    /// </summary>
    public DateTime ImportedAt { get; set; }

    /// <summary>
    /// 数据归属组织节点ID
    /// </summary>
    public int? OwnerOrgUnitId { get; set; }

    /// <summary>
    /// 创建人用户ID
    /// </summary>
    public int? CreatedByUserId { get; set; }
}

/// <summary>
/// 创建验收规格请求
/// </summary>
public class CreateSpecRequest
{
    /// <summary>
    /// 所属客户ID
    /// </summary>
    [Required(ErrorMessage = "客户ID不能为空")]
    public int CustomerId { get; set; }

    /// <summary>
    /// 所属制程ID
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// 所属机型ID
    /// </summary>
    public int? MachineModelId { get; set; }

    /// <summary>
    /// 项目名称
    /// </summary>
    [Required(ErrorMessage = "项目名称不能为空")]
    [StringLength(500, ErrorMessage = "项目名称不能超过500个字符")]
    public string Project { get; set; } = string.Empty;

    /// <summary>
    /// 规格内容
    /// </summary>
    [Required(ErrorMessage = "规格内容不能为空")]
    public string Specification { get; set; } = string.Empty;

    /// <summary>
    /// 验收标准
    /// </summary>
    public string? Acceptance { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Remark { get; set; }
}

/// <summary>
/// 更新验收规格请求
/// </summary>
public class UpdateSpecRequest
{
    /// <summary>
    /// 项目名称
    /// </summary>
    [Required(ErrorMessage = "项目名称不能为空")]
    [StringLength(500, ErrorMessage = "项目名称不能超过500个字符")]
    public string Project { get; set; } = string.Empty;

    /// <summary>
    /// 规格内容
    /// </summary>
    [Required(ErrorMessage = "规格内容不能为空")]
    public string Specification { get; set; } = string.Empty;

    /// <summary>
    /// 验收标准
    /// </summary>
    public string? Acceptance { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Remark { get; set; }
}

/// <summary>
/// 批量导入验收规格请求
/// </summary>
public class BatchImportSpecsRequest
{
    /// <summary>
    /// 所属客户ID
    /// </summary>
    [Required(ErrorMessage = "客户ID不能为空")]
    public int CustomerId { get; set; }

    /// <summary>
    /// 所属制程ID
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// 所属机型ID
    /// </summary>
    public int? MachineModelId { get; set; }

    /// <summary>
    /// 来源Word文件ID
    /// </summary>
    [Required(ErrorMessage = "Word文件ID不能为空")]
    public int WordFileId { get; set; }

    /// <summary>
    /// 验收规格列表
    /// </summary>
    [Required(ErrorMessage = "规格列表不能为空")]
    public List<SpecImportItem> Items { get; set; } = [];
}

/// <summary>
/// 导入的规格项
/// </summary>
public class SpecImportItem
{
    /// <summary>
    /// 项目名称
    /// </summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>
    /// 规格内容
    /// </summary>
    public string Specification { get; set; } = string.Empty;

    /// <summary>
    /// 验收标准
    /// </summary>
    public string? Acceptance { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Remark { get; set; }
}

/// <summary>
/// 批量导入结果
/// </summary>
public class BatchImportResult
{
    /// <summary>
    /// 成功导入数量
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// 失败数量
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// 总数量
    /// </summary>
    public int TotalCount { get; set; }
}

/// <summary>
/// 验收规格分组汇总（按客户 + 机型 + 制程分组）
/// </summary>
public class SpecGroupDto
{
    /// <summary>
    /// 客户ID
    /// </summary>
    public int CustomerId { get; set; }

    /// <summary>
    /// 客户名称
    /// </summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>
    /// 机型ID（可为空）
    /// </summary>
    public int? MachineModelId { get; set; }

    /// <summary>
    /// 机型名称（可为空）
    /// </summary>
    public string? MachineModelName { get; set; }

    /// <summary>
    /// 制程ID（可为空）
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// 制程名称（可为空）
    /// </summary>
    public string? ProcessName { get; set; }

    /// <summary>
    /// 该分组下的规格数量
    /// </summary>
    public int SpecCount { get; set; }
}

/// <summary>
/// 验收规格筛选请求
/// </summary>
public class SpecFilterRequest
{
    /// <summary>
    /// 客户ID（可选）
    /// </summary>
    public int? CustomerId { get; set; }

    /// <summary>
    /// 制程ID（可选）
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// 机型ID（可选）
    /// </summary>
    public int? MachineModelId { get; set; }

    /// <summary>
    /// 搜索关键字（可选）
    /// </summary>
    public string? Keyword { get; set; }

    /// <summary>
    /// 页码
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// 每页数量
    /// </summary>
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// 规格重复排查结果
/// </summary>
public class SpecDuplicateDetectionResultDto
{
    /// <summary>
    /// 本次扫描的规格数
    /// </summary>
    public int ScannedCount { get; set; }

    /// <summary>
    /// 完全重复分组数
    /// </summary>
    public int ExactGroupCount { get; set; }

    /// <summary>
    /// 近重复分组数
    /// </summary>
    public int SimilarGroupCount { get; set; }

    /// <summary>
    /// 完全重复分组列表
    /// </summary>
    public List<SpecDuplicateGroupDto> ExactGroups { get; set; } = [];

    /// <summary>
    /// 近重复分组列表
    /// </summary>
    public List<SpecDuplicateGroupDto> SimilarGroups { get; set; } = [];
}

/// <summary>
/// 规格重复分组
/// </summary>
public class SpecDuplicateGroupDto
{
    /// <summary>
    /// 分组类型：exact / similar
    /// </summary>
    public string GroupType { get; set; } = string.Empty;

    /// <summary>
    /// 代表项目
    /// </summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>
    /// 代表规格摘要
    /// </summary>
    public string SpecificationPreview { get; set; } = string.Empty;

    /// <summary>
    /// 相似原因说明
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 相似度分数
    /// </summary>
    public double SimilarityScore { get; set; }

    /// <summary>
    /// 当前分组条数
    /// </summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// 分组内的规格项
    /// </summary>
    public List<SpecDuplicateItemDto> Items { get; set; } = [];
}

/// <summary>
/// 重复排查中的规格项
/// </summary>
public class SpecDuplicateItemDto
{
    /// <summary>
    /// 规格ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 项目
    /// </summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>
    /// 规格内容
    /// </summary>
    public string Specification { get; set; } = string.Empty;

    /// <summary>
    /// 验收标准
    /// </summary>
    public string? Acceptance { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Remark { get; set; }

    /// <summary>
    /// 导入时间
    /// </summary>
    public DateTime ImportedAt { get; set; }
}

/// <summary>
/// 验收规格语义搜索请求
/// </summary>
public class SpecSemanticSearchRequest
{
    /// <summary>
    /// 查询文本列表，多行输入时每行对应一条查询
    /// </summary>
    [Required(ErrorMessage = "搜索内容不能为空")]
    public List<string> Queries { get; set; } = [];

    /// <summary>
    /// 客户ID（可选）
    /// </summary>
    public int? CustomerId { get; set; }

    /// <summary>
    /// 制程ID（可选）
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// 机型ID（可选）
    /// </summary>
    public int? MachineModelId { get; set; }

    /// <summary>
    /// 是否筛选制程为空
    /// </summary>
    public bool? ProcessIdIsNull { get; set; }

    /// <summary>
    /// 是否筛选机型为空
    /// </summary>
    public bool? MachineModelIdIsNull { get; set; }

    /// <summary>
    /// 返回的结果条数
    /// </summary>
    [Range(1, 20, ErrorMessage = "TopK 必须在 1 到 20 之间")]
    public int TopK { get; set; } = 5;

    /// <summary>
    /// 最小相似度阈值
    /// </summary>
    [Range(0, 1, ErrorMessage = "最小分数必须在 0 到 1 之间")]
    public double MinScore { get; set; } = 0.5;

    /// <summary>
    /// 指定 Embedding 服务ID（可选）
    /// </summary>
    public int? EmbeddingServiceId { get; set; }
}

/// <summary>
/// 验收规格语义搜索响应
/// </summary>
public class SpecSemanticSearchResponse
{
    /// <summary>
    /// 查询条数
    /// </summary>
    public int QueryCount { get; set; }

    /// <summary>
    /// 参与检索的候选规格数
    /// </summary>
    public int CandidateCount { get; set; }

    /// <summary>
    /// 实际使用的 Embedding 模型名称
    /// </summary>
    public string? EmbeddingModel { get; set; }

    /// <summary>
    /// 查询结果分组
    /// </summary>
    public List<SpecSemanticSearchGroupDto> Groups { get; set; } = [];
}

/// <summary>
/// 单条查询的语义搜索结果分组
/// </summary>
public class SpecSemanticSearchGroupDto
{
    /// <summary>
    /// 查询序号（从0开始）
    /// </summary>
    public int QueryIndex { get; set; }

    /// <summary>
    /// 查询文本
    /// </summary>
    public string QueryText { get; set; } = string.Empty;

    /// <summary>
    /// 命中总数（未截断前）
    /// </summary>
    public int TotalHits { get; set; }

    /// <summary>
    /// 当前返回的结果项
    /// </summary>
    public List<SpecSemanticSearchItemDto> Items { get; set; } = [];
}

/// <summary>
/// 语义搜索命中的规格项
/// </summary>
public class SpecSemanticSearchItemDto : AcceptanceSpecDto
{
    /// <summary>
    /// 语义相似度分数（0-1）
    /// </summary>
    public double Score { get; set; }
}
