using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;

namespace AcceptanceSpecSystem.Core.Documents.Intelligence.Strategies;

/// <summary>
/// 基于规则的列映射识别策略
/// </summary>
public interface IRuleBasedMappingStrategy
{
    /// <summary>
    /// 识别列映射
    /// </summary>
    /// <param name="headers">表头列表</param>
    /// <param name="sampleRows">样本数据行（用于辅助判断）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>列映射识别结果</returns>
    Task<ColumnMappingResult> IdentifyAsync(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> sampleRows,
        CancellationToken cancellationToken = default);
}
