using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Models;

namespace AcceptanceSpecSystem.Core.Documents.Intelligence;

/// <summary>
/// 文档智能识别服务
/// </summary>
public interface IDocumentIntelligenceService
{
    /// <summary>
    /// 识别目标表格
    /// </summary>
    /// <param name="tables">所有表格信息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表格识别结果</returns>
    Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 识别列映射
    /// </summary>
    /// <param name="tableData">表格数据</param>
    /// <param name="extraSynonyms">外部列名词典，例如客户学习词。</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>列映射识别结果</returns>
    Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 使用保留匹配模式和优先级的结构化规则识别列映射。
    /// </summary>
    Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
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
        return IdentifyColumnMappingAsync(tableData, synonyms, cancellationToken);
    }

    /// <summary>
    /// 检测表头行位置
    /// </summary>
    /// <param name="tableData">表格数据</param>
    /// <param name="scanRowLimit">最多扫描的前置行数。</param>
    /// <returns>表头行索引（0-based）</returns>
    int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null);
}
