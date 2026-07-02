using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Models;

namespace AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;

public enum DocumentStructureDecision
{
    NeedConfirm = 0,
    AutoApply = 1,
    Reject = 2
}

public enum DocumentStructureHealthIssueCode
{
    MissingSpecificationColumn = 1,
    DuplicateMappedColumn = 2,
    EmptySpecificationDataArea = 3,
    ProjectSpecificationLikelyReversed = 4,
    ColumnIndexOutOfRange = 5,
    InvalidRowRange = 6,
    LowConfidence = 7
}

public sealed record DocumentStructureHealthIssue(
    DocumentStructureHealthIssueCode Code,
    string Message);

public sealed class DocumentStructureHealthCheckResult
{
    public DocumentStructureDecision Decision { get; init; }

    public bool CanAutoApply => Decision == DocumentStructureDecision.AutoApply;

    public IReadOnlyList<DocumentStructureHealthIssue> Issues { get; init; } = [];
}

public static class DocumentStructureHealthCheck
{
    private const double AutoApplyConfidenceThreshold = 0.85;
    private const double MinimumSpecificationNonEmptyRate = 0.5;

    public static DocumentStructureHealthCheckResult Evaluate(
        TableData tableData,
        ColumnMappingResult mappingResult,
        double? confidence = null)
    {
        var mapping = mappingResult.Mapping;
        var effectiveConfidence = confidence ?? mappingResult.Confidence;
        var issues = new List<DocumentStructureHealthIssue>();

        if (effectiveConfidence < AutoApplyConfidenceThreshold)
        {
            issues.Add(new DocumentStructureHealthIssue(
                DocumentStructureHealthIssueCode.LowConfidence,
                "识别置信度不足，需人工确认"));
        }

        if (!mapping.SpecificationColumn.HasValue)
        {
            issues.Add(new DocumentStructureHealthIssue(
                DocumentStructureHealthIssueCode.MissingSpecificationColumn,
                "缺少规格列，不能自动采用"));
        }

        AddColumnRangeIssues(tableData, mapping, issues);
        AddDuplicateColumnIssues(mapping, issues);
        AddRowRangeIssues(tableData, mapping, issues);

        if (mapping.SpecificationColumn.HasValue &&
            IsColumnInRange(tableData, mapping.SpecificationColumn.Value) &&
            CalculateNonEmptyRate(tableData, mapping.SpecificationColumn.Value) < MinimumSpecificationNonEmptyRate)
        {
            issues.Add(new DocumentStructureHealthIssue(
                DocumentStructureHealthIssueCode.EmptySpecificationDataArea,
                "规格列数据区为空值过多，需人工确认"));
        }

        if (mapping.ProjectColumn.HasValue &&
            mapping.SpecificationColumn.HasValue &&
            IsColumnInRange(tableData, mapping.ProjectColumn.Value) &&
            IsColumnInRange(tableData, mapping.SpecificationColumn.Value) &&
            LooksLikeProjectSpecificationReversed(tableData, mapping.ProjectColumn.Value, mapping.SpecificationColumn.Value))
        {
            issues.Add(new DocumentStructureHealthIssue(
                DocumentStructureHealthIssueCode.ProjectSpecificationLikelyReversed,
                "项目列与规格列疑似判反，需人工确认"));
        }

        return new DocumentStructureHealthCheckResult
        {
            Decision = issues.Count == 0
                ? DocumentStructureDecision.AutoApply
                : DocumentStructureDecision.NeedConfirm,
            Issues = issues
        };
    }

    private static void AddColumnRangeIssues(
        TableData tableData,
        ColumnMapping mapping,
        List<DocumentStructureHealthIssue> issues)
    {
        foreach (var column in mapping.GetMappedColumns())
        {
            if (!IsColumnInRange(tableData, column))
            {
                issues.Add(new DocumentStructureHealthIssue(
                    DocumentStructureHealthIssueCode.ColumnIndexOutOfRange,
                    $"映射列索引越界：{column}"));
            }
        }
    }

    private static void AddDuplicateColumnIssues(
        ColumnMapping mapping,
        List<DocumentStructureHealthIssue> issues)
    {
        var duplicates = mapping.GetMappedColumns()
            .GroupBy(column => column)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        foreach (var duplicate in duplicates)
        {
            issues.Add(new DocumentStructureHealthIssue(
                DocumentStructureHealthIssueCode.DuplicateMappedColumn,
                $"多个字段映射到同一列：{duplicate}"));
        }
    }

    private static void AddRowRangeIssues(
        TableData tableData,
        ColumnMapping mapping,
        List<DocumentStructureHealthIssue> issues)
    {
        if (mapping.HeaderRowIndex < 0 ||
            mapping.HeaderRowCount <= 0 ||
            mapping.DataStartRowIndex < 0 ||
            mapping.DataStartRowIndex < mapping.HeaderRowIndex + mapping.HeaderRowCount ||
            mapping.DataStartRowIndex >= Math.Max(tableData.TotalRowCount, 1))
        {
            issues.Add(new DocumentStructureHealthIssue(
                DocumentStructureHealthIssueCode.InvalidRowRange,
                "表头行或数据行范围不合法"));
        }
    }

    private static bool IsColumnInRange(TableData tableData, int columnIndex)
    {
        return columnIndex >= 0 && columnIndex < tableData.ColumnCount;
    }

    private static double CalculateNonEmptyRate(TableData tableData, int columnIndex)
    {
        if (tableData.Rows.Count == 0)
        {
            return 0;
        }

        var nonEmptyCount = tableData.Rows.Count(row => !string.IsNullOrWhiteSpace(row.GetValue(columnIndex)));
        return (double)nonEmptyCount / tableData.Rows.Count;
    }

    private static bool LooksLikeProjectSpecificationReversed(
        TableData tableData,
        int projectColumn,
        int specificationColumn)
    {
        var projectScore = AverageSpecificationLikeScore(tableData, projectColumn);
        var specificationScore = AverageSpecificationLikeScore(tableData, specificationColumn);
        return projectScore >= specificationScore + 0.35 && projectScore >= 0.55;
    }

    private static double AverageSpecificationLikeScore(TableData tableData, int columnIndex)
    {
        var values = tableData.Rows
            .Select(row => row.GetValue(columnIndex)?.Trim() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        if (values.Count == 0)
        {
            return 0;
        }

        return values.Average(CalculateSpecificationLikeScore);
    }

    private static double CalculateSpecificationLikeScore(string value)
    {
        var score = 0.0;
        if (value.Length >= 8)
        {
            score += 0.25;
        }

        if (value.Any(char.IsDigit))
        {
            score += 0.25;
        }

        var specificationKeywords = new[]
        {
            "无", "不", "应", "需", "必须", "不得", "以内", "以上", "以下", "公差", "误差", "mm", "cm", "v", "kw", "±", "≤", "≥"
        };
        if (specificationKeywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            score += 0.35;
        }

        if (value.Contains('，') || value.Contains(',') || value.Contains('；') || value.Contains(';'))
        {
            score += 0.15;
        }

        return Math.Min(score, 1.0);
    }
}
