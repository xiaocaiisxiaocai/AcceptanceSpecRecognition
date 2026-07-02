using AcceptanceSpecSystem.Core.Documents.Models;

namespace AcceptanceSpecSystem.Core.Documents.Intelligence.Models;

/// <summary>
/// 自动配置结果
/// </summary>
public sealed class AutoConfigResult
{
    /// <summary>
    /// 目标表格索引
    /// </summary>
    public int TableIndex { get; init; }

    /// <summary>
    /// 列映射配置
    /// </summary>
    public ColumnMapping ColumnMapping { get; init; } = null!;

    /// <summary>
    /// 综合置信度（0.0 - 1.0）
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// 识别来源
    /// </summary>
    public IdentificationSource Source { get; init; }

    /// <summary>
    /// 是否需要人工审核
    /// </summary>
    public bool NeedsManualReview { get; init; }

    /// <summary>
    /// 识别依据/推理过程
    /// </summary>
    public string Reasoning { get; init; } = string.Empty;

    /// <summary>
    /// 表格识别详情
    /// </summary>
    public TableIdentificationResult? TableIdentification { get; init; }

    /// <summary>
    /// 列映射识别详情
    /// </summary>
    public ColumnMappingResult? ColumnMappingDetails { get; init; }
}
