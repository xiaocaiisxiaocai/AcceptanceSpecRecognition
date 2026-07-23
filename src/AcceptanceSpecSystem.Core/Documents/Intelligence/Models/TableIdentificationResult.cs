namespace AcceptanceSpecSystem.Core.Documents.Intelligence.Models;

/// <summary>
/// 表格识别结果
/// </summary>
public sealed class TableIdentificationResult
{
    /// <summary>
    /// 表格索引（从 0 开始）
    /// </summary>
    public int TableIndex { get; init; }

    /// <summary>
    /// 置信度（0.0 - 1.0）
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// 识别依据/推理过程
    /// </summary>
    public string Reasoning { get; init; } = string.Empty;
}
