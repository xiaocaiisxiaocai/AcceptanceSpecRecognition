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
    /// <param name="extraSynonyms">外部列名词典，例如客户学习词。</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>列映射识别结果</returns>
    Task<ColumnMappingResult> IdentifyAsync(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> sampleRows,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 使用保留匹配模式和优先级的结构化规则识别列映射。
    /// </summary>
    Task<ColumnMappingResult> IdentifyAsync(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> sampleRows,
        IReadOnlyList<ColumnHeaderMappingRule> rules,
        CancellationToken cancellationToken = default)
    {
        var synonyms = rules
            .Where(rule => rule.ColumnType != ColumnType.Unknown)
            .GroupBy(rule => rule.ColumnType)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(rule => rule.Pattern)
                    .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                    .ToList());
        return IdentifyAsync(headers, sampleRows, synonyms, cancellationToken);
    }
}
