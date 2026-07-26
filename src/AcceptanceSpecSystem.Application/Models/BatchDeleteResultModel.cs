namespace AcceptanceSpecSystem.Application.Models;

/// <summary>
/// 批量删除结果：成功删除的ID列表 + 逐项失败原因（如仍存在关联规格、ID不存在等）。
/// </summary>
public sealed class BatchDeleteResultModel
{
    public List<int> SucceededIds { get; set; } = [];

    public List<BatchDeleteFailureModel> Failures { get; set; } = [];
}

/// <summary>
/// 批量删除中单个条目的失败信息。
/// </summary>
public sealed class BatchDeleteFailureModel
{
    public int Id { get; set; }

    public string Reason { get; set; } = string.Empty;
}
