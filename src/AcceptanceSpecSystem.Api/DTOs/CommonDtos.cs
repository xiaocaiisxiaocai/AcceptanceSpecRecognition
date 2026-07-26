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

/// <summary>
/// 批量删除响应：成功删除的ID列表 + 逐项失败原因。
/// </summary>
public class BatchDeleteResponseDto
{
    public List<int> SucceededIds { get; set; } = new();

    public List<BatchDeleteFailureDto> Failures { get; set; } = new();
}

/// <summary>
/// 批量删除中单个条目的失败信息。
/// </summary>
public class BatchDeleteFailureDto
{
    public int Id { get; set; }

    public string Reason { get; set; } = string.Empty;
}
