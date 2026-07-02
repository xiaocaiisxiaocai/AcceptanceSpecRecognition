using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
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
            cancellationToken);

        return result;
    }

    public async Task<AutoConfigResult> AutoConfigureAsync(
        IReadOnlyList<TableInfo> tables,
        IReadOnlyList<TableData> tablesData,
        CancellationToken cancellationToken = default)
    {
        if (tables.Count == 0 || tablesData.Count == 0)
        {
            throw new ArgumentException("表格列表不能为空");
        }

        if (tables.Count != tablesData.Count)
        {
            throw new ArgumentException("表格信息和表格数据数量不匹配");
        }

        // 1. 识别目标表格
        var tableResult = await IdentifyTargetTableAsync(tables, cancellationToken);

        // 2. 检测表头行位置
        var targetTableData = tablesData[tableResult.TableIndex];
        var headerRowIndex = DetectHeaderRowIndex(targetTableData);

        // 3. 提取表头和数据样本（从检测到的表头行开始）
        var headers = targetTableData.Rows
            .Skip(headerRowIndex)
            .Take(1)
            .SelectMany(row => row.Cells.Select(c => c.Value ?? string.Empty))
            .ToList();

        var sampleRows = targetTableData.Rows
            .Skip(headerRowIndex + 1)
            .Take(3)
            .Select(row => (IReadOnlyList<string>)row.Cells.Select(c => c.Value ?? string.Empty).ToList())
            .ToList();

        // 4. 识别列映射（使用检测到的表头）
        var mappingResult = await _ruleStrategy.IdentifyAsync(
            headers,
            sampleRows,
            cancellationToken);

        // 5. 更新 ColumnMapping 的行位置信息
        mappingResult.Mapping.HeaderRowIndex = headerRowIndex;
        mappingResult.Mapping.HeaderRowCount = 1;
        mappingResult.Mapping.DataStartRowIndex = headerRowIndex + 1;

        // 6. 计算综合置信度
        var overallConfidence = Math.Min(tableResult.Confidence, mappingResult.Confidence);

        // 7. 构建推理说明
        var reasoning = $"表格识别：{tableResult.Reasoning}；" +
                       $"表头检测：第 {headerRowIndex + 1} 行；" +
                       $"列映射识别：{mappingResult.Reasoning}";

        return new AutoConfigResult
        {
            TableIndex = tableResult.TableIndex,
            ColumnMapping = mappingResult.Mapping,
            Confidence = overallConfidence,
            Source = IdentificationSource.RuleBased,
            NeedsManualReview = overallConfidence < 0.85,
            Reasoning = reasoning,
            TableIdentification = tableResult,
            ColumnMappingDetails = mappingResult
        };
    }

    private double ScoreTableRelevance(TableInfo table)
    {
        double score = 0.0;

        // 跳过嵌套表格
        if (table.IsNested)
        {
            return 0.0;
        }

        // 检查表头关键词
        if (table.Headers != null && table.Headers.Count > 0)
        {
            var keywords = new[]
            {
                ("项目", 0.25),
                ("规格", 0.25),
                ("验收", 0.25),
                ("备注", 0.10),
                ("标准", 0.15),
                ("结果", 0.15),
                ("判定", 0.15)
            };

            foreach (var (keyword, weight) in keywords)
            {
                var matchCount = table.Headers.Count(h =>
                    h.Contains(keyword, StringComparison.OrdinalIgnoreCase));

                if (matchCount > 0)
                {
                    score += weight;
                }
            }
        }

        // 检查表格名称（Excel 工作表名）
        if (!string.IsNullOrEmpty(table.Name))
        {
            var nameKeywords = new[] { "验收", "检验", "规格", "acceptance", "inspection", "spec" };
            if (nameKeywords.Any(k => table.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                score += 0.15;
            }
        }

        // 检查表格预览文本
        if (!string.IsNullOrEmpty(table.PreviewText))
        {
            var previewKeywords = new[] { "验收", "检验", "规格" };
            if (previewKeywords.Any(k => table.PreviewText.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                score += 0.10;
            }
        }

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
    /// <returns>表头行索引（0-based）</returns>
    public int DetectHeaderRowIndex(TableData tableData)
    {
        if (tableData.Rows.Count == 0)
        {
            return 0;
        }

        // 关键词列表
        var keywords = new[]
        {
            "项目", "规格", "验收", "备注", "标准", "结果", "判定", "测试",
            "检验", "检测", "实测", "序号", "编号", "次", "名称", "说明",
            "project", "specification", "acceptance", "remark", "result", "test"
        };

        var scores = new List<(int rowIndex, double score, string reason)>();

        // 分析前 10 行（或全部行，取较小值）
        var rowsToAnalyze = Math.Min(10, tableData.Rows.Count);

        for (int i = 0; i < rowsToAnalyze; i++)
        {
            var row = tableData.Rows[i];
            double score = 0.0;
            var reasons = new List<string>();

            // 1. 检查是否包含关键词
            int keywordCount = 0;
            int nonEmptyCount = 0;

            foreach (var cell in row.Cells)
            {
                var value = cell.Value ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    nonEmptyCount++;

                    // 检查是否包含关键词
                    if (keywords.Any(k => value.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    {
                        keywordCount++;
                    }
                }
            }

            // 2. 计算关键词密度
            if (nonEmptyCount > 0)
            {
                double keywordDensity = (double)keywordCount / nonEmptyCount;
                score += keywordDensity * 0.6; // 最高 0.6 分

                if (keywordCount > 0)
                {
                    reasons.Add($"{keywordCount} 个关键词");
                }
            }

            // 3. 检查单元格长度（表头通常较短）
            var avgLength = row.Cells
                .Where(c => !string.IsNullOrWhiteSpace(c.Value))
                .Average(c => c.Value?.Length ?? 0);

            if (avgLength > 2 && avgLength < 15)
            {
                score += 0.2;
                reasons.Add("长度适中");
            }
            else if (avgLength >= 15 && avgLength < 50)
            {
                score += 0.1;
                reasons.Add("长度偏长");
            }

            // 4. 检查非空单元格数量（表头通常填满）
            double fillRate = (double)nonEmptyCount / row.Cells.Count;
            if (fillRate > 0.5)
            {
                score += 0.2;
                reasons.Add($"填充率 {fillRate:P0}");
            }

            scores.Add((i, score, string.Join(", ", reasons)));

            _logger.LogDebug(
                "行 {RowIndex} 表头分数: {Score:F2} ({Reasons})",
                i,
                score,
                string.Join(", ", reasons));
        }

        // 选择得分最高的行
        var best = scores.OrderByDescending(s => s.score).First();

        _logger.LogInformation(
            "检测到表头行: 第 {RowIndex} 行，得分 {Score:F2} ({Reasons})",
            best.rowIndex,
            best.score,
            best.reason);

        return best.rowIndex;
    }
}
