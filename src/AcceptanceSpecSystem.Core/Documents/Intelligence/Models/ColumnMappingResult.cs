using AcceptanceSpecSystem.Core.Documents.Models;

namespace AcceptanceSpecSystem.Core.Documents.Intelligence.Models;

/// <summary>
/// 列映射识别结果
/// </summary>
public sealed class ColumnMappingResult
{
    /// <summary>
    /// 列映射配置
    /// </summary>
    public ColumnMapping Mapping { get; init; } = null!;

    /// <summary>
    /// 综合置信度（0.0 - 1.0）
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// 各列的详细识别结果
    /// </summary>
    public List<ColumnIdentificationResult> Details { get; init; } = new();

    /// <summary>
    /// 识别依据/推理过程
    /// </summary>
    public string Reasoning { get; init; } = string.Empty;
}
