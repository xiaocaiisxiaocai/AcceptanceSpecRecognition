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
