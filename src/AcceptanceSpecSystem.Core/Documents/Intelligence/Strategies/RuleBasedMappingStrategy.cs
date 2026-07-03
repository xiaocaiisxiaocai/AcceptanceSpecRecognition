using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Scoring;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Core.Documents.Intelligence.Strategies;

/// <summary>
/// 基于规则的列映射识别策略实现
/// </summary>
public sealed class RuleBasedMappingStrategy : IRuleBasedMappingStrategy
{
    private readonly ITextPreprocessingPipeline _textPipeline;
    private readonly ILogger<RuleBasedMappingStrategy> _logger;

    // 同义词字典（可通过配置注入）
    private static readonly Dictionary<ColumnType, string[]> DefaultSynonyms = new()
    {
        [ColumnType.Project] = new[]
        {
            "项目", "检验项目", "测试项目", "检测项目", "检查项目",
            "項目", "驗收項目", "檢驗項目", "測試項目", "檢測項目", "檢查項目",
            "测试内容", "检测内容", "检验内容", "管控项", "关键特性", "ctq",
            "測試內容", "檢測內容", "檢驗內容", "管控項", "關鍵特性",
            "item", "test item", "inspection item", "test content"
        },
        [ColumnType.Specification] = new[]
        {
            "规格", "标准", "要求", "技术标准", "技术要求", "检验标准",
            "規格", "驗收規格", "標準", "技術標準", "技術要求", "檢驗標準",
            "规范", "参数", "指标", "基准", "判定基准",
            "規範", "參數", "指標", "基準", "判定基準",
            "spec", "specification", "standard", "requirement", "criteria"
        },
        [ColumnType.Acceptance] = new[]
        {
            "验收", "判定", "结果", "测试结果", "检测结果", "判定结果",
            "驗收", "設備商確認", "确认", "確認", "結果", "測試結果", "檢測結果", "判定結果",
            "实测", "实测值", "实测结果", "ok/ng", "合格判定",
            "實測", "實測值", "實測結果",
            "acceptance", "result", "judgment", "actual", "pass/fail"
        },
        [ColumnType.Remark] = new[]
        {
            "备注", "说明", "注释", "补充", "补充说明", "附注",
            "備註", "說明", "註釋", "補充", "補充說明", "附註",
            "remark", "note", "comment", "description", "remarks"
        }
    };

    public RuleBasedMappingStrategy(
        ITextPreprocessingPipeline textPipeline,
        ILogger<RuleBasedMappingStrategy> logger)
    {
        _textPipeline = textPipeline;
        _logger = logger;
    }

    public async Task<ColumnMappingResult> IdentifyAsync(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> sampleRows,
        CancellationToken cancellationToken = default)
    {
        var session = await _textPipeline.CreateSessionAsync(cancellationToken);
        var details = new List<ColumnIdentificationResult>();

        // 识别每一列
        for (int i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            var result = IdentifyColumn(header, i, sampleRows, session);
            details.Add(result);
        }

        // 构建列映射
        var mapping = BuildColumnMapping(details);
        var confidence = CalculateOverallConfidence(details, mapping);
        var reasoning = BuildReasoning(details);

        return new ColumnMappingResult
        {
            Mapping = mapping,
            Confidence = confidence,
            Details = details,
            Reasoning = reasoning
        };
    }

    private ColumnIdentificationResult IdentifyColumn(
        string header,
        int columnIndex,
        IReadOnlyList<IReadOnlyList<string>> sampleRows,
        TextProcessingSession session)
    {
        // 文本标准化
        var normalizedHeader = session.Process(header).ToLowerInvariant();

        // 尝试匹配每种列类型
        var candidates = new List<(ColumnType type, double confidence, string reason)>();

        if (LooksLikeAcceptanceStandardColumn(normalizedHeader))
        {
            candidates.Add((ColumnType.Acceptance, 0.98, "表头表示验收标准"));
        }

        foreach (var (type, keywords) in DefaultSynonyms)
        {
            if (type == ColumnType.Acceptance && LooksLikeAcceptanceMethodColumn(normalizedHeader))
            {
                continue;
            }
            if (type == ColumnType.Specification && LooksLikeAcceptanceStandardColumn(normalizedHeader))
            {
                continue;
            }

            var (matched, confidence, matchedKeyword) = MatchKeywords(normalizedHeader, keywords);
            if (matched)
            {
                candidates.Add((type, confidence, $"表头包含关键词 '{matchedKeyword}'"));
            }
        }

        // 如果没有匹配，尝试通过数据样本推断
        if (candidates.Count == 0)
        {
            var dataTypeGuess = GuessColumnTypeByData(columnIndex, sampleRows);
            if (dataTypeGuess.type != ColumnType.Unknown)
            {
                candidates.Add(dataTypeGuess);
            }
        }

        // 选择置信度最高的
        if (candidates.Count > 0)
        {
            var best = candidates.OrderByDescending(c => c.confidence).First();
            return new ColumnIdentificationResult
            {
                ColumnIndex = columnIndex,
                HeaderText = header,
                ColumnType = best.type,
                Confidence = best.confidence,
                Reasoning = best.reason
            };
        }

        return new ColumnIdentificationResult
        {
            ColumnIndex = columnIndex,
            HeaderText = header,
            ColumnType = ColumnType.Unknown,
            Confidence = 0.0,
            Reasoning = "未匹配任何规则"
        };
    }

    private static bool LooksLikeAcceptanceMethodColumn(string normalizedHeader)
    {
        return (normalizedHeader.Contains("验收") || normalizedHeader.Contains("驗收")) &&
               (normalizedHeader.Contains("方法") || normalizedHeader.Contains("方式")) &&
               !normalizedHeader.Contains("确认") &&
               !normalizedHeader.Contains("確認") &&
               !normalizedHeader.Contains("结果") &&
               !normalizedHeader.Contains("結果") &&
               !normalizedHeader.Contains("判定");
    }

    private static bool LooksLikeAcceptanceStandardColumn(string normalizedHeader)
    {
        return (normalizedHeader.Contains("验收") || normalizedHeader.Contains("驗收")) &&
               (normalizedHeader.Contains("标准") || normalizedHeader.Contains("標準")) &&
               !LooksLikeAcceptanceMethodColumn(normalizedHeader);
    }

    private (bool matched, double confidence, string matchedKeyword) MatchKeywords(
        string text,
        string[] keywords)
    {
        // 完全匹配
        foreach (var keyword in keywords)
        {
            if (text.Contains(keyword.ToLowerInvariant()))
            {
                return (true, 0.95, keyword);
            }
        }

        // 编辑距离匹配（处理拼写变体）
        foreach (var keyword in keywords)
        {
            var similarity = CalculateLevenshteinSimilarity(text, keyword.ToLowerInvariant());
            if (similarity > 0.8)
            {
                return (true, similarity * 0.9, keyword); // 略微降低置信度
            }
        }

        return (false, 0.0, string.Empty);
    }

    private (ColumnType type, double confidence, string reason) GuessColumnTypeByData(
        int columnIndex,
        IReadOnlyList<IReadOnlyList<string>> sampleRows)
    {
        if (sampleRows.Count == 0)
            return (ColumnType.Unknown, 0.0, string.Empty);

        // 收集该列的样本数据
        var samples = sampleRows
            .Where(row => columnIndex < row.Count)
            .Select(row => row[columnIndex])
            .Where(cell => !string.IsNullOrWhiteSpace(cell))
            .Take(5)
            .ToList();

        if (samples.Count == 0)
            return (ColumnType.Unknown, 0.0, string.Empty);

        // 检查是否为验收列（包含 OK/NG）
        var okNgCount = samples.Count(s =>
            s.Contains("OK", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("NG", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("✓") || s.Contains("✗") ||
            s.Contains("合格") || s.Contains("不合格"));

        if (okNgCount >= samples.Count * 0.5)
        {
            return (ColumnType.Acceptance, 0.75, "数据包含 OK/NG 判定值");
        }

        // 检查是否为规格列（包含技术参数格式）
        var specPatternCount = SpecificationLikelihoodScorer.CountTechnicalPatternValues(samples);

        if (specPatternCount >= samples.Count * 0.5)
        {
            return (ColumnType.Specification, 0.70, "数据包含技术参数格式（单位、范围等）");
        }

        return (ColumnType.Unknown, 0.0, string.Empty);
    }

    private ColumnMapping BuildColumnMapping(List<ColumnIdentificationResult> details)
    {
        var mapping = new ColumnMapping
        {
            // 设置默认的表头和数据行位置
            HeaderRowIndex = 0,      // 默认第 0 行是表头
            HeaderRowCount = 1,      // 默认表头占 1 行
            DataStartRowIndex = 1    // 默认数据从第 1 行开始
        };

        foreach (var detail in details.Where(d => d.ColumnType != ColumnType.Unknown))
        {
            switch (detail.ColumnType)
            {
                case ColumnType.Project:
                    if (!mapping.ProjectColumn.HasValue ||
                        detail.Confidence > details.First(d => d.ColumnIndex == mapping.ProjectColumn).Confidence)
                    {
                        mapping.ProjectColumn = detail.ColumnIndex;
                    }
                    break;

                case ColumnType.Specification:
                    if (!mapping.SpecificationColumn.HasValue ||
                        detail.Confidence > details.First(d => d.ColumnIndex == mapping.SpecificationColumn).Confidence)
                    {
                        mapping.SpecificationColumn = detail.ColumnIndex;
                    }
                    break;

                case ColumnType.Acceptance:
                    if (!mapping.AcceptanceColumn.HasValue ||
                        detail.Confidence > details.First(d => d.ColumnIndex == mapping.AcceptanceColumn).Confidence)
                    {
                        mapping.AcceptanceColumn = detail.ColumnIndex;
                    }
                    break;

                case ColumnType.Remark:
                    if (!mapping.RemarkColumn.HasValue ||
                        detail.Confidence > details.First(d => d.ColumnIndex == mapping.RemarkColumn).Confidence)
                    {
                        mapping.RemarkColumn = detail.ColumnIndex;
                    }
                    break;
            }
        }

        return mapping;
    }

    private double CalculateOverallConfidence(
        List<ColumnIdentificationResult> details,
        ColumnMapping mapping)
    {
        // 至少需要项目列和规格列
        if (!mapping.ProjectColumn.HasValue || !mapping.SpecificationColumn.HasValue)
        {
            return 0.0;
        }

        // 计算已识别列的平均置信度
        var identifiedColumns = details.Where(d => d.ColumnType != ColumnType.Unknown).ToList();
        if (identifiedColumns.Count == 0)
        {
            return 0.0;
        }

        var avgConfidence = identifiedColumns.Average(d => d.Confidence);

        // 如果缺少验收列，降低整体置信度
        if (!mapping.AcceptanceColumn.HasValue)
        {
            avgConfidence *= 0.8;
        }

        return Math.Round(avgConfidence, 2);
    }

    private string BuildReasoning(List<ColumnIdentificationResult> details)
    {
        var identified = details.Where(d => d.ColumnType != ColumnType.Unknown).ToList();
        if (identified.Count == 0)
        {
            return "未识别到任何列";
        }

        var parts = identified.Select(d =>
            $"第 {d.ColumnIndex + 1} 列 \"{d.HeaderText}\" → {GetColumnTypeName(d.ColumnType)} ({d.Confidence:P0})");

        return string.Join("；", parts);
    }

    private string GetColumnTypeName(ColumnType type) => type switch
    {
        ColumnType.Project => "项目列",
        ColumnType.Specification => "规格列",
        ColumnType.Acceptance => "验收列",
        ColumnType.Remark => "备注列",
        _ => "未知"
    };

    private double CalculateLevenshteinSimilarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
            return 0.0;

        var distance = CalculateLevenshteinDistance(s1, s2);
        var maxLength = Math.Max(s1.Length, s2.Length);
        return 1.0 - (double)distance / maxLength;
    }

    private int CalculateLevenshteinDistance(string s1, string s2)
    {
        var m = s1.Length;
        var n = s2.Length;
        var d = new int[m + 1, n + 1];

        for (var i = 0; i <= m; i++)
            d[i, 0] = i;

        for (var j = 0; j <= n; j++)
            d[0, j] = j;

        for (var j = 1; j <= n; j++)
        {
            for (var i = 1; i <= m; i++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[m, n];
    }
}
