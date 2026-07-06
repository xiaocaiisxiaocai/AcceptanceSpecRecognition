using System.Text.RegularExpressions;

namespace AcceptanceSpecSystem.Core.Documents.Intelligence.Scoring;

/// <summary>
/// 规格文本特征评分器，统一判断文本是否像规格值。
/// </summary>
public static partial class SpecificationLikelihoodScorer
{
    private static readonly (string Keyword, double Weight)[] StructureKeywords =
    [
        ("项目", 0.25),
        ("规格", 0.25),
        ("验收", 0.25),
        ("备注", 0.10),
        ("标准", 0.15),
        ("结果", 0.15),
        ("判定", 0.15)
    ];

    private static readonly string[] NameKeywords =
    [
        "验收", "检验", "规格", "acceptance", "inspection", "spec"
    ];

    private static readonly string[] HeaderKeywords =
    [
        "项目", "规格", "验收", "备注", "标准", "结果", "判定", "测试",
        "检验", "检测", "实测", "序号", "编号", "次", "名称", "说明",
        "检查", "管制", "条件", "对象", "供应商", "确认", "回复", "补充",
        "依据", "方式", "分类",
        "project", "specification", "acceptance", "remark", "result", "test"
    ];

    private static readonly string[] SpecificationKeywords =
    [
        "无", "不", "应", "需", "必须", "不得", "以内", "以上", "以下", "公差", "误差",
        "mm", "cm", "kg", "v", "kw", "%", "±", "≤", "≥"
    ];

    public static double Calculate(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return 0;
        }

        var score = 0.0;
        if (text.Length >= 8)
        {
            score += 0.25;
        }

        if (text.Any(char.IsDigit))
        {
            score += 0.25;
        }

        if (SpecificationKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            score += 0.35;
        }

        if (text.Contains('，') || text.Contains(',') || text.Contains('；') || text.Contains(';'))
        {
            score += 0.15;
        }

        return Math.Min(score, 1.0);
    }

    public static double Average(IEnumerable<string?> values)
    {
        var scores = values
            .Select(value => value?.Trim() ?? string.Empty)
            .Where(value => value.Length > 0)
            .Select(Calculate)
            .ToList();

        return scores.Count == 0 ? 0 : scores.Average();
    }

    public static int CountTechnicalPatternValues(IEnumerable<string?> values)
    {
        return values.Count(value =>
        {
            var text = value?.Trim() ?? string.Empty;
            return text.Length > 0 &&
                   (SpecificationKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                    RangePattern().IsMatch(text));
        });
    }

    public static double ScoreStructureHeaders(IEnumerable<string?> headers)
    {
        var normalizedHeaders = headers
            .Select(header => header?.Trim() ?? string.Empty)
            .Where(header => header.Length > 0)
            .ToList();

        if (normalizedHeaders.Count == 0)
        {
            return 0;
        }

        var score = 0.0;
        foreach (var (keyword, weight) in StructureKeywords)
        {
            if (normalizedHeaders.Any(header => ContainsKeyword(header, keyword)))
            {
                score += weight;
            }
        }

        return Math.Min(score, 1.0);
    }

    public static double ScoreDocumentTableName(string? name)
    {
        var text = name?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return 0;
        }

        return NameKeywords.Any(keyword => ContainsKeyword(text, keyword)) ? 0.15 : 0;
    }

    public static double ScoreDocumentPreviewText(string? previewText)
    {
        var text = previewText?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return 0;
        }

        return NameKeywords
            .Where(keyword => keyword is "验收" or "检验" or "规格")
            .Any(keyword => ContainsKeyword(text, keyword))
            ? 0.10
            : 0;
    }

    public static HeaderRowScore ScoreHeaderRow(IEnumerable<string?> cellValues)
    {
        var values = cellValues
            .Select(value => value?.Trim() ?? string.Empty)
            .ToList();
        if (values.Count == 0)
        {
            return new HeaderRowScore(0, 0, 0, 0);
        }

        var nonEmptyValues = values
            .Where(value => value.Length > 0)
            .ToList();
        if (nonEmptyValues.Count == 0)
        {
            return new HeaderRowScore(0, 0, 0, 0);
        }

        var distinctNonEmptyCount = nonEmptyValues
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var keywordCount = nonEmptyValues.Count(value =>
            HeaderKeywords.Any(keyword => ContainsKeyword(value, keyword)));
        var keywordDensity = (double)keywordCount / nonEmptyValues.Count;
        var score = keywordDensity * 0.6;

        var averageLength = nonEmptyValues.Average(value => value.Length);
        if (averageLength > 2 && averageLength < 15)
        {
            score += 0.2;
        }
        else if (averageLength >= 15 && averageLength < 50)
        {
            score += 0.1;
        }

        var effectiveNonEmptyCount = Math.Min(nonEmptyValues.Count, distinctNonEmptyCount);
        var fillRate = (double)effectiveNonEmptyCount / values.Count;
        if (fillRate > 0.5)
        {
            score += 0.2;
        }

        if (effectiveNonEmptyCount < 2)
        {
            score *= 0.5;
        }

        return new HeaderRowScore(
            Math.Min(score, 1.0),
            keywordCount,
            averageLength,
            fillRate);
    }

    private static bool ContainsKeyword(string text, string keyword)
    {
        return text.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"\d+[~\-]\d+")]
    private static partial Regex RangePattern();
}

public readonly record struct HeaderRowScore(
    double Score,
    int KeywordCount,
    double AverageLength,
    double FillRate);
