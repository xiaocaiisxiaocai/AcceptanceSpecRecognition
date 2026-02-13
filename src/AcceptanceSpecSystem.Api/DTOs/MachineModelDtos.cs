using System.ComponentModel.DataAnnotations;

namespace AcceptanceSpecSystem.Api.DTOs;

/// <summary>
/// 机型数据传输对象
/// </summary>
public class MachineModelDto
{
    /// <summary>
    /// 机型ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 机型名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 验收规格数量
    /// </summary>
    public int SpecCount { get; set; }
}

/// <summary>
/// 创建机型请求
/// </summary>
public class CreateMachineModelRequest
{
    /// <summary>
    /// 机型名称
    /// </summary>
    [Required(ErrorMessage = "机型名称不能为空")]
    [StringLength(100, ErrorMessage = "机型名称不能超过100个字符")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 更新机型请求
/// </summary>
public class UpdateMachineModelRequest
{
    /// <summary>
    /// 机型名称
    /// </summary>
    [Required(ErrorMessage = "机型名称不能为空")]
    [StringLength(100, ErrorMessage = "机型名称不能超过100个字符")]
    public string Name { get; set; } = string.Empty;
}
