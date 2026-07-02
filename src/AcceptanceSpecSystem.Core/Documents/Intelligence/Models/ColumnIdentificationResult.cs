namespace AcceptanceSpecSystem.Core.Documents.Intelligence.Models;

/// <summary>
/// 列识别结果
/// </summary>
public sealed class ColumnIdentificationResult
{
    /// <summary>
    /// 列索引（从 0 开始）
    /// </summary>
    public int ColumnIndex { get; init; }

    /// <summary>
    /// 列表头文本
    /// </summary>
    public string HeaderText { get; init; } = string.Empty;

    /// <summary>
    /// 识别出的列类型
    /// </summary>
    public ColumnType ColumnType { get; init; }

    /// <summary>
    /// 置信度（0.0 - 1.0）
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// 识别依据/推理过程
    /// </summary>
    public string Reasoning { get; init; } = string.Empty;
}
