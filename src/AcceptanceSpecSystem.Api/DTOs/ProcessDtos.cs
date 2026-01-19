using System.ComponentModel.DataAnnotations;

namespace AcceptanceSpecSystem.Api.DTOs;

/// <summary>
/// 制程数据传输对象
/// </summary>
public class ProcessDto
{
    /// <summary>
    /// 制程ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 制程名称
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
/// 创建制程请求
/// </summary>
public class CreateProcessRequest
{
    /// <summary>
    /// 制程名称
    /// </summary>
    [Required(ErrorMessage = "制程名称不能为空")]
    [StringLength(100, ErrorMessage = "制程名称不能超过100个字符")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 更新制程请求
/// </summary>
public class UpdateProcessRequest
{
    /// <summary>
    /// 制程名称
    /// </summary>
    [Required(ErrorMessage = "制程名称不能为空")]
    [StringLength(100, ErrorMessage = "制程名称不能超过100个字符")]
    public string Name { get; set; } = string.Empty;
}
