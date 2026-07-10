using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Services;

internal static class SmartConfigurationRecognizedTableFactory
{
    public static SmartConfigurationRecognizedTable FromTemplate(
        TableInfo? tableInfo,
        TableData tableData,
        DocumentTemplate template,
        List<string> headers,
        DocumentStructureHealthCheckResult healthCheck)
    {
        var structure = new SmartConfigurationTableStructure
        {
            TableIndex = tableData.TableIndex,
            TableName = tableInfo?.Name,
            Headers = headers,
            HeaderRowIndex = template.HeaderRowIndex,
            HeaderRowCount = template.HeaderRowCount,
            DataStartRowIndex = template.DataStartRowIndex,
            DataEndRowIndex = template.DataEndRowIndex,
            ProjectColumnIndex = NormalizeTemplateColumn(template.ProjectColumnIndex),
            SpecificationColumnIndex = NormalizeTemplateColumn(template.SpecificationColumnIndex),
            AcceptanceColumnIndex = NormalizeTemplateColumn(template.AcceptanceColumnIndex),
            RemarkColumnIndex = NormalizeTemplateColumn(template.RemarkColumnIndex),
            IsSpecificationOnly = template.IsSpecificationOnly,
            Confidence = 1.0,
            Source = "Template",
            Decision = healthCheck.CanAutoApply ? "AutoApply" : "NeedConfirm"
        };
        return ToRecognizedTable(NormalizeRowRange(tableInfo, tableData, structure));
    }

    public static SmartConfigurationRecognizedTable FromMapping(
        TableInfo? tableInfo,
        TableData tableData,
        ColumnMappingResult mapping,
        DocumentStructureHealthCheckResult healthCheck,
        bool isSpecificationOnly)
    {
        var headers = tableData.Headers.ToList();
        var structure = FromColumnMapping(tableInfo, tableData, mapping, isSpecificationOnly);
        return ToRecognizedTable(NormalizeRowRange(tableInfo, tableData, structure) with
        {
            Decision = healthCheck.CanAutoApply ? "AutoApply" : "NeedConfirm",
            FieldConfidences = BuildFieldConfidences(mapping)
        });
    }

    public static SmartConfigurationTableStructure FromColumnMapping(
        TableInfo? tableInfo,
        TableData tableData,
        ColumnMappingResult mapping,
        bool? isSpecificationOnly = null)
    {
        var columnMapping = mapping.Mapping;
        return new SmartConfigurationTableStructure
        {
            TableIndex = tableData.TableIndex,
            TableName = tableInfo?.Name,
            Headers = tableData.Headers.ToList(),
            HeaderRowIndex = columnMapping.HeaderRowIndex,
            HeaderRowCount = columnMapping.HeaderRowCount,
            DataStartRowIndex = columnMapping.DataStartRowIndex,
            DataEndRowIndex = GetOriginalTableEndRowIndex(tableInfo, tableData),
            ProjectColumnIndex = columnMapping.ProjectColumn,
            SpecificationColumnIndex = columnMapping.SpecificationColumn,
            AcceptanceColumnIndex = columnMapping.AcceptanceColumn,
            RemarkColumnIndex = columnMapping.RemarkColumn,
            IsSpecificationOnly = isSpecificationOnly ?? !columnMapping.ProjectColumn.HasValue,
            Confidence = mapping.Confidence,
            Source = "RuleBased",
            Decision = "NeedConfirm"
        };
    }

    private static IReadOnlyDictionary<string, double> BuildFieldConfidences(ColumnMappingResult mapping)
    {
        return new Dictionary<string, double>
        {
            ["Project"] = GetMappedColumnConfidence(mapping, mapping.Mapping.ProjectColumn),
            ["Specification"] = GetMappedColumnConfidence(mapping, mapping.Mapping.SpecificationColumn),
            ["Acceptance"] = GetMappedColumnConfidence(mapping, mapping.Mapping.AcceptanceColumn),
            ["Remark"] = GetMappedColumnConfidence(mapping, mapping.Mapping.RemarkColumn)
        };
    }

    private static double GetMappedColumnConfidence(ColumnMappingResult mapping, int? columnIndex)
    {
        if (!columnIndex.HasValue)
        {
            return 0;
        }

        return mapping.Details.FirstOrDefault(detail => detail.ColumnIndex == columnIndex.Value)?.Confidence ??
               mapping.Confidence;
    }

    public static SmartConfigurationRecognizedTable FromCandidate(
        TableInfo? tableInfo,
        TableData tableData,
        DocumentStructureCandidate candidate,
        DocumentStructureHealthCheckResult healthCheck)
    {
        var structure = FromCandidate(tableInfo, tableData, candidate);
        return ToRecognizedTable(NormalizeRowRange(tableInfo, tableData, structure) with
        {
            Decision = healthCheck.CanAutoApply ? "AutoApply" : "NeedConfirm"
        });
    }

    public static SmartConfigurationTableStructure FromCandidate(
        TableInfo? tableInfo,
        TableData tableData,
        DocumentStructureCandidate candidate)
    {
        return new SmartConfigurationTableStructure
        {
            TableIndex = tableData.TableIndex,
            TableName = tableInfo?.Name,
            Headers = tableData.Headers.ToList(),
            HeaderRowIndex = candidate.HeaderRowIndex,
            HeaderRowCount = candidate.HeaderRowCount,
            DataStartRowIndex = candidate.DataStartRowIndex,
            DataEndRowIndex = candidate.DataEndRowIndex ?? GetOriginalTableEndRowIndex(tableInfo, tableData),
            ProjectColumnIndex = candidate.ProjectColumnIndex,
            SpecificationColumnIndex = candidate.SpecificationColumnIndex,
            AcceptanceColumnIndex = candidate.AcceptanceColumnIndex,
            RemarkColumnIndex = candidate.RemarkColumnIndex,
            IsSpecificationOnly = candidate.IsSpecificationOnly,
            Confidence = candidate.Confidence,
            Source = "Fused",
            Decision = "NeedConfirm"
        };
    }

    public static DocumentStructureCandidate ToStructureCandidate(
        TableInfo? tableInfo,
        TableData tableData,
        ColumnMappingResult mapping)
    {
        return ToStructureCandidate(FromColumnMapping(tableInfo, tableData, mapping), DocumentStructureCandidateSource.Rule);
    }

    public static DocumentStructureCandidate ToStructureCandidate(
        SmartConfigurationTableStructure structure,
        DocumentStructureCandidateSource source)
    {
        return new DocumentStructureCandidate
        {
            TableIndex = structure.TableIndex,
            TableName = structure.TableName,
            HeaderRowIndex = structure.HeaderRowIndex,
            HeaderRowCount = structure.HeaderRowCount,
            DataStartRowIndex = structure.DataStartRowIndex,
            DataEndRowIndex = structure.DataEndRowIndex,
            ProjectColumnIndex = structure.ProjectColumnIndex,
            SpecificationColumnIndex = structure.SpecificationColumnIndex,
            AcceptanceColumnIndex = structure.AcceptanceColumnIndex,
            RemarkColumnIndex = structure.RemarkColumnIndex,
            IsSpecificationOnly = structure.IsSpecificationOnly,
            Confidence = structure.Confidence,
            Source = source
        };
    }

    public static ColumnMappingResult ToColumnMappingResult(DocumentStructureCandidate candidate)
    {
        return ToColumnMappingResult(new SmartConfigurationTableStructure
        {
            TableIndex = candidate.TableIndex,
            TableName = candidate.TableName,
            Headers = [],
            HeaderRowIndex = candidate.HeaderRowIndex,
            HeaderRowCount = candidate.HeaderRowCount,
            DataStartRowIndex = candidate.DataStartRowIndex,
            DataEndRowIndex = candidate.DataEndRowIndex,
            ProjectColumnIndex = candidate.ProjectColumnIndex,
            SpecificationColumnIndex = candidate.SpecificationColumnIndex,
            AcceptanceColumnIndex = candidate.AcceptanceColumnIndex,
            RemarkColumnIndex = candidate.RemarkColumnIndex,
            IsSpecificationOnly = candidate.IsSpecificationOnly,
            Confidence = candidate.Confidence,
            Source = candidate.Source.ToString(),
            Decision = "NeedConfirm"
        });
    }

    public static ColumnMappingResult ToColumnMappingResult(DocumentTemplate template)
    {
        return new ColumnMappingResult
        {
            Confidence = 1.0,
            Mapping = new ColumnMapping
            {
                ProjectColumn = NormalizeTemplateColumn(template.ProjectColumnIndex),
                SpecificationColumn = NormalizeTemplateColumn(template.SpecificationColumnIndex),
                AcceptanceColumn = NormalizeTemplateColumn(template.AcceptanceColumnIndex),
                RemarkColumn = NormalizeTemplateColumn(template.RemarkColumnIndex),
                HeaderRowIndex = template.HeaderRowIndex,
                HeaderRowCount = template.HeaderRowCount,
                DataStartRowIndex = template.DataStartRowIndex
            }
        };
    }

    public static ColumnMappingResult ToColumnMappingResult(SmartConfigurationTableStructure structure)
    {
        return new ColumnMappingResult
        {
            Confidence = structure.Confidence,
            Mapping = new ColumnMapping
            {
                ProjectColumn = structure.ProjectColumnIndex,
                SpecificationColumn = structure.SpecificationColumnIndex,
                AcceptanceColumn = structure.AcceptanceColumnIndex,
                RemarkColumn = structure.RemarkColumnIndex,
                HeaderRowIndex = structure.HeaderRowIndex,
                HeaderRowCount = structure.HeaderRowCount,
                DataStartRowIndex = structure.DataStartRowIndex
            }
        };
    }

    public static SmartConfigurationRecognizedTable ToRecognizedTable(SmartConfigurationTableStructure structure)
    {
        return new SmartConfigurationRecognizedTable
        {
            TableIndex = structure.TableIndex,
            TableName = structure.TableName,
            Headers = structure.Headers,
            HeaderRowIndex = structure.HeaderRowIndex,
            HeaderRowCount = structure.HeaderRowCount,
            DataStartRowIndex = structure.DataStartRowIndex,
            DataEndRowIndex = structure.DataEndRowIndex,
            ProjectColumnIndex = structure.ProjectColumnIndex,
            SpecificationColumnIndex = structure.SpecificationColumnIndex,
            AcceptanceColumnIndex = structure.AcceptanceColumnIndex,
            RemarkColumnIndex = structure.RemarkColumnIndex,
            IsSpecificationOnly = structure.IsSpecificationOnly,
            Confidence = structure.Confidence,
            Source = structure.Source,
            Decision = structure.Decision,
            Fields = BuildFields(
                structure.Headers,
                structure.ProjectColumnIndex,
                structure.SpecificationColumnIndex,
                structure.AcceptanceColumnIndex,
                structure.RemarkColumnIndex,
                structure.Confidence,
                structure.Source,
                structure.FieldConfidences)
        };
    }

    private static SmartConfigurationTableStructure NormalizeRowRange(
        TableInfo? tableInfo,
        TableData tableData,
        SmartConfigurationTableStructure structure)
    {
        var fallbackEndRowIndex = GetOriginalTableEndRowIndex(tableInfo, tableData);
        var dataEndRowIndex = structure.DataEndRowIndex ?? fallbackEndRowIndex;
        if (dataEndRowIndex.HasValue && fallbackEndRowIndex.HasValue)
        {
            dataEndRowIndex = Math.Min(dataEndRowIndex.Value, fallbackEndRowIndex.Value);
        }

        if (dataEndRowIndex.HasValue && dataEndRowIndex.Value < structure.DataStartRowIndex)
        {
            // 多行表头占满已用区域时没有数据行，结束行应为空，不能返回反向范围。
            dataEndRowIndex = null;
        }

        return structure with
        {
            DataEndRowIndex = dataEndRowIndex
        };
    }

    private static int? GetOriginalTableEndRowIndex(TableInfo? tableInfo, TableData tableData)
    {
        var totalRowCount = tableInfo?.RowCount > 0
            ? tableInfo.RowCount
            : tableData.TotalRowCount;
        return totalRowCount > 0 ? totalRowCount - 1 : null;
    }

    private static int? NormalizeTemplateColumn(int? columnIndex)
    {
        return columnIndex.HasValue && columnIndex.Value >= 0
            ? columnIndex.Value
            : null;
    }

    private static List<SmartConfigurationRecognizedField> BuildFields(
        IReadOnlyList<string> headers,
        int? projectColumn,
        int? specificationColumn,
        int? acceptanceColumn,
        int? remarkColumn,
        double confidence,
        string source,
        IReadOnlyDictionary<string, double>? fieldConfidences = null)
    {
        return
        [
            BuildField("Project", projectColumn, headers, confidence, source, fieldConfidences),
            BuildField("Specification", specificationColumn, headers, confidence, source, fieldConfidences),
            BuildField("Acceptance", acceptanceColumn, headers, confidence, source, fieldConfidences),
            BuildField("Remark", remarkColumn, headers, confidence, source, fieldConfidences)
        ];
    }

    private static SmartConfigurationRecognizedField BuildField(
        string field,
        int? columnIndex,
        IReadOnlyList<string> headers,
        double confidence,
        string source,
        IReadOnlyDictionary<string, double>? fieldConfidences)
    {
        return new SmartConfigurationRecognizedField
        {
            Field = field,
            ColumnIndex = columnIndex,
            Header = columnIndex.HasValue &&
                     columnIndex.Value >= 0 &&
                     columnIndex.Value < headers.Count
                ? headers[columnIndex.Value]
                : null,
            Confidence = columnIndex.HasValue
                ? fieldConfidences?.GetValueOrDefault(field, confidence) ?? confidence
                : 0,
            Source = source
        };
    }
}

internal sealed record SmartConfigurationTableStructure
{
    public required int TableIndex { get; init; }

    public string? TableName { get; init; }

    public required List<string> Headers { get; init; }

    public required int HeaderRowIndex { get; init; }

    public required int HeaderRowCount { get; init; }

    public required int DataStartRowIndex { get; init; }

    public int? DataEndRowIndex { get; init; }

    public int? ProjectColumnIndex { get; init; }

    public int? SpecificationColumnIndex { get; init; }

    public int? AcceptanceColumnIndex { get; init; }

    public int? RemarkColumnIndex { get; init; }

    public required bool IsSpecificationOnly { get; init; }

    public required double Confidence { get; init; }

    public required string Source { get; init; }

    public required string Decision { get; init; }

    public IReadOnlyDictionary<string, double>? FieldConfidences { get; init; }
}
