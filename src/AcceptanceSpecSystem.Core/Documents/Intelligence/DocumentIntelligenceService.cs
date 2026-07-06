using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Scoring;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Strategies;
using AcceptanceSpecSystem.Core.Documents.Models;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Core.Documents.Intelligence;

/// <summary>
/// 文档智能识别服务实现
/// </summary>
public sealed class DocumentIntelligenceService : IDocumentIntelligenceService
{
    private readonly IRuleBasedMappingStrategy _ruleStrategy;
    private readonly ILogger<DocumentIntelligenceService> _logger;

    public DocumentIntelligenceService(
        IRuleBasedMappingStrategy ruleStrategy,
        ILogger<DocumentIntelligenceService> logger)
    {
        _ruleStrategy = ruleStrategy;
        _logger = logger;
    }

    public Task<TableIdentificationResult> IdentifyTargetTableAsync(
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken = default)
    {
        if (tables.Count == 0)
        {
            throw new ArgumentException("表格列表不能为空", nameof(tables));
        }

        // 快速路径：单表格
        if (tables.Count == 1)
        {
            return Task.FromResult(new TableIdentificationResult
            {
                TableIndex = 0,
                Confidence = 1.0,
                Reasoning = "文档仅包含一个表格"
            });
        }

        // 规则识别：对每个表格打分
        var scores = tables.Select((table, index) => new
        {
            Index = index,
            Score = ScoreTableRelevance(table),
            Table = table
        }).ToList();

        var best = scores.OrderByDescending(s => s.Score).First();

        if (best.Score > 0.8)
        {
            return Task.FromResult(new TableIdentificationResult
            {
                TableIndex = best.Index,
                Confidence = best.Score,
                Reasoning = $"表格特征匹配验收规格表（得分 {best.Score:P0}）"
            });
        }

        // 回退：选择最大表格（行数最多）
        var largestTable = tables
            .Select((table, index) => new { Index = index, table.RowCount })
            .OrderByDescending(t => t.RowCount)
            .First();

        _logger.LogWarning(
            "未能高置信度识别目标表格，回退到选择最大表格（索引 {TableIndex}，{RowCount} 行）",
            largestTable.Index,
            largestTable.RowCount);

        return Task.FromResult(new TableIdentificationResult
        {
            TableIndex = largestTable.Index,
            Confidence = 0.3,
            Reasoning = $"无法自动识别，已选择最大表格（第 {largestTable.Index + 1} 个，{largestTable.RowCount} 行）"
        });
    }

    public async Task<ColumnMappingResult> IdentifyColumnMappingAsync(
        TableData tableData,
        IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
        CancellationToken cancellationToken = default)
    {
        if (tableData.Headers.Count == 0)
        {
            throw new ArgumentException("表格没有表头", nameof(tableData));
        }

        // 提取样本数据（前 3 行）
        var sampleRows = tableData.Rows
            .Take(3)
            .Select(row => (IReadOnlyList<string>)row.Cells.Select(c => c.Value ?? string.Empty).ToList())
            .ToList();

        // 使用规则策略识别
        var result = await _ruleStrategy.IdentifyAsync(
            (IReadOnlyList<string>)tableData.Headers.ToList(),
            sampleRows,
            extraSynonyms,
            cancellationToken);

        return result;
    }

    private double ScoreTableRelevance(TableInfo table)
    {
        double score = 0.0;

        // 跳过嵌套表格
        if (table.IsNested)
        {
            return 0.0;
        }

        if (table.Headers != null && table.Headers.Count > 0)
        {
            score += SpecificationLikelihoodScorer.ScoreStructureHeaders(table.Headers);
        }

        score += SpecificationLikelihoodScorer.ScoreDocumentTableName(table.Name);

        score += SpecificationLikelihoodScorer.ScoreDocumentPreviewText(table.PreviewText);

        // 加分项：数据行数合理（3-1000 行）
        if (table.RowCount >= 3 && table.RowCount <= 1000)
        {
            score += 0.10;
        }

        // 加分项：列数合理（3-10 列）
        if (table.ColumnCount >= 3 && table.ColumnCount <= 10)
        {
            score += 0.05;
        }

        return Math.Min(score, 1.0);
    }

    /// <summary>
    /// 检测表头行位置
    /// </summary>
    /// <param name="tableData">表格数据（包含前 N 行原始数据）</param>
    /// <param name="scanRowLimit">最多扫描的前置行数。</param>
    /// <returns>表头行索引（0-based）</returns>
    public int DetectHeaderRowIndex(TableData tableData, int? scanRowLimit = null)
    {
        if (tableData.Rows.Count == 0)
        {
            return 0;
        }

        var scores = new List<(int rowIndex, double score, double adjustedScore, int keywordCount, string reason)>();

        // 默认分析前 10 行；调用方可按业务配置放宽窗口。
        var effectiveScanRowLimit = Math.Clamp(scanRowLimit ?? 10, 1, Math.Max(1, tableData.Rows.Count));
        var rowsToAnalyze = Math.Min(effectiveScanRowLimit, tableData.Rows.Count);

        var hasSeenSpecificationDataRow = false;
        for (int i = 0; i < rowsToAnalyze; i++)
        {
            var row = tableData.Rows[i];
            var reasons = new List<string>();
            var rowScore = SpecificationLikelihoodScorer.ScoreHeaderRow(
                row.Cells.Select(cell => cell.Value));
            if (rowScore.KeywordCount > 0)
            {
                reasons.Add($"{rowScore.KeywordCount} 个关键词");
            }

            if (rowScore.AverageLength > 2 && rowScore.AverageLength < 15)
            {
                reasons.Add("长度适中");
            }
            else if (rowScore.AverageLength >= 15 && rowScore.AverageLength < 50)
            {
                reasons.Add("长度偏长");
            }

            if (rowScore.FillRate > 0.5)
            {
                reasons.Add($"填充率 {rowScore.FillRate:P0}");
            }

            var adjustedScore = rowScore.Score;
            if (i > 0 && LooksLikeSpecificationDataRow(row))
            {
                adjustedScore *= 0.65;
                reasons.Add("疑似数据行降权");
            }

            if (hasSeenSpecificationDataRow && rowScore.KeywordCount > 0)
            {
                adjustedScore *= 0.5;
                reasons.Add("位于数据区后降权");
            }

            scores.Add((i, rowScore.Score, adjustedScore, rowScore.KeywordCount, string.Join(", ", reasons)));
            hasSeenSpecificationDataRow |= LooksLikeSpecificationDataRow(row);

            _logger.LogDebug(
                "行 {RowIndex} 表头分数: {Score:F2} ({Reasons})",
                i,
                rowScore.Score,
                string.Join(", ", reasons));
        }

        // 候选排序先看降权后的分数；分差不大时优先更早的表头候选，避免数据行抢锚点。
        var orderedScores = scores
            .OrderByDescending(s => s.adjustedScore)
            .ThenBy(s => s.rowIndex)
            .ToList();
        var best = orderedScores.First();
        var earlyComparable = orderedScores
            .Where(score => score.rowIndex < best.rowIndex &&
                            score.keywordCount > 0 &&
                            best.adjustedScore - score.adjustedScore <= 0.25)
            .OrderBy(score => score.rowIndex)
            .FirstOrDefault();
        if (earlyComparable != default)
        {
            best = earlyComparable;
        }

        _logger.LogInformation(
            "检测到表头行: 第 {RowIndex} 行，得分 {Score:F2} ({Reasons})",
            best.rowIndex,
            best.adjustedScore,
            best.reason);

        return best.rowIndex;
    }

    private static bool LooksLikeSpecificationDataRow(RowData row)
    {
        var values = row.Cells
            .Select(cell => cell.Value?.Trim() ?? string.Empty)
            .Where(value => value.Length > 0)
            .ToList();
        if (values.Count == 0)
        {
            return false;
        }

        var technicalPatternCount = SpecificationLikelihoodScorer.CountTechnicalPatternValues(values);
        if (technicalPatternCount == 0)
        {
            return false;
        }

        return values.Any(value =>
            value.Length >= 8 ||
            value.Contains("不得", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("必须", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("应", StringComparison.OrdinalIgnoreCase));
    }
}
