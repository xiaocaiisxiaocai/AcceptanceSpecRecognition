using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Scoring;
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
    LowConfidence = 7,
    MissingProjectColumn = 8,
    MissingAcceptanceColumn = 9,
    MissingRemarkColumn = 10,
    AmbiguousColumnMapping = 11
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
        double? confidence = null,
        bool allowMissingProjectColumn = false,
        double? autoApplyConfidenceThreshold = null,
        double? minimumSpecificationNonEmptyRate = null)
    {
        var mapping = mappingResult.Mapping;
        var effectiveConfidence = confidence ?? mappingResult.Confidence;
        var effectiveAutoApplyThreshold = autoApplyConfidenceThreshold ?? AutoApplyConfidenceThreshold;
        var effectiveMinimumSpecificationNonEmptyRate =
            minimumSpecificationNonEmptyRate ?? MinimumSpecificationNonEmptyRate;
        var issues = new List<DocumentStructureHealthIssue>();

        if (effectiveConfidence < effectiveAutoApplyThreshold)
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

        if (!mapping.ProjectColumn.HasValue && !allowMissingProjectColumn)
        {
            issues.Add(new DocumentStructureHealthIssue(
                DocumentStructureHealthIssueCode.MissingProjectColumn,
                "缺少项目列，不能自动采用"));
        }

        if (!mapping.AcceptanceColumn.HasValue)
        {
            issues.Add(new DocumentStructureHealthIssue(
                DocumentStructureHealthIssueCode.MissingAcceptanceColumn,
                "缺少验收列，不能自动采用"));
        }

        AddColumnRangeIssues(tableData, mapping, issues);
        AddDuplicateColumnIssues(mapping, issues);
        AddAmbiguousColumnIssues(mappingResult, issues);
        AddRowRangeIssues(tableData, mapping, issues);

        if (mapping.SpecificationColumn.HasValue &&
            IsColumnInRange(tableData, mapping.SpecificationColumn.Value) &&
            CalculateNonEmptyRate(tableData, mapping.SpecificationColumn.Value) < effectiveMinimumSpecificationNonEmptyRate)
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

    private static void AddAmbiguousColumnIssues(
        ColumnMappingResult mappingResult,
        List<DocumentStructureHealthIssue> issues)
    {
        var ambiguousTypes = mappingResult.Details
            .Where(detail => detail.ColumnType != ColumnType.Unknown && detail.Confidence >= 0.8)
            .GroupBy(detail => detail.ColumnType)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        foreach (var columnType in ambiguousTypes)
        {
            issues.Add(new DocumentStructureHealthIssue(
                DocumentStructureHealthIssueCode.AmbiguousColumnMapping,
                $"{GetColumnTypeName(columnType)}存在多个高置信候选列，需人工确认"));
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

    private static string GetColumnTypeName(ColumnType type) => type switch
    {
        ColumnType.Project => "项目列",
        ColumnType.Specification => "规格列",
        ColumnType.Acceptance => "验收列",
        ColumnType.Remark => "备注列",
        _ => "字段"
    };

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
            .ToList();
        return SpecificationLikelihoodScorer.Average(values);
    }
}
