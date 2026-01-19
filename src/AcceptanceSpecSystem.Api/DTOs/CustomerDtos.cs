using System.ComponentModel.DataAnnotations;

namespace AcceptanceSpecSystem.Api.DTOs;

/// <summary>
/// 客户数据传输对象
/// </summary>
public class CustomerDto
{
    /// <summary>
    /// 客户ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 客户名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 制程数量
    /// </summary>
    public int ProcessCount { get; set; }
}

/// <summary>
/// 创建客户请求
/// </summary>
public class CreateCustomerRequest
{
    /// <summary>
    /// 客户名称
    /// </summary>
    [Required(ErrorMessage = "客户名称不能为空")]
    [StringLength(100, ErrorMessage = "客户名称不能超过100个字符")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 更新客户请求
/// </summary>
public class UpdateCustomerRequest
{
    /// <summary>
    /// 客户名称
    /// </summary>
    [Required(ErrorMessage = "客户名称不能为空")]
    [StringLength(100, ErrorMessage = "客户名称不能超过100个字符")]
    public string Name { get; set; } = string.Empty;
}
