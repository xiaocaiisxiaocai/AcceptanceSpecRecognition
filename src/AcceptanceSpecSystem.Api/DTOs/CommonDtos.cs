using System.ComponentModel.DataAnnotations;

namespace AcceptanceSpecSystem.Api.DTOs;

/// <summary>
/// 批量删除请求
/// </summary>
public class BatchDeleteRequest
{
    /// <summary>
    /// 要删除的ID列表
    /// </summary>
    [Required]
    public List<int> Ids { get; set; } = new();
}
