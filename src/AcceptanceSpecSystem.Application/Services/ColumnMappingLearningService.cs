using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 将最终确认过的列配置沉淀为客户级列映射规则。
/// </summary>
public sealed class ColumnMappingLearningService
{
    private const int MaxLearnableHeaderLength = 100;

    private readonly SmartConfigurationLearningService _learningService;
    private readonly IDocumentImportTableReader _documentTableAccessService;

    public ColumnMappingLearningService(
        SmartConfigurationLearningService learningService,
        IDocumentImportTableReader documentTableAccessService)
    {
        _learningService = learningService;
        _documentTableAccessService = documentTableAccessService;
    }

    public async Task LearnFromHeadersAsync(
        int? customerId,
        IReadOnlyList<string> headers,
        int? projectColumnIndex,
        int? specificationColumnIndex,
        int? acceptanceColumnIndex,
        int? remarkColumnIndex,
        string? tableName,
        CancellationToken cancellationToken)
    {
        if (!customerId.HasValue || customerId.Value <= 0 || headers.Count == 0)
        {
            return;
        }

        var learnedColumns = BuildLearnedColumns(
            headers,
            projectColumnIndex,
            specificationColumnIndex,
            acceptanceColumnIndex,
            remarkColumnIndex);
        if (learnedColumns.Count == 0)
        {
            return;
        }

        await _learningService.ApplyLearningAsync(
            customerId.Value,
            tableName,
            tableKind: null,
            recommendation: null,
            learnedColumns,
            cancellationToken);
    }

    public async Task LearnFromDocumentTableAsync(
        int? customerId,
        WordFile wordFile,
        int tableIndex,
        int? projectColumnIndex,
        int? specificationColumnIndex,
        int? acceptanceColumnIndex,
        int? remarkColumnIndex,
        int? headerRowStart,
        int? headerRowCount,
        int? dataStartRow,
        CancellationToken cancellationToken)
    {
        if (!customerId.HasValue || customerId.Value <= 0)
        {
            return;
        }

        var (mapping, tableName) = await BuildExtractionMappingAsync(
            wordFile,
            tableIndex,
            headerRowStart,
            headerRowCount,
            dataStartRow,
            cancellationToken);
        var tableData = await _documentTableAccessService.ExtractTableDataAsync(
            wordFile,
            tableIndex,
            mapping,
            cancellationToken: cancellationToken);

        await LearnFromHeadersAsync(
            customerId,
            tableData.Headers.ToList(),
            projectColumnIndex,
            specificationColumnIndex,
            acceptanceColumnIndex,
            remarkColumnIndex,
            tableName,
            cancellationToken);
    }

    private async Task<(ColumnMapping Mapping, string? TableName)> BuildExtractionMappingAsync(
        WordFile wordFile,
        int tableIndex,
        int? headerRowStart,
        int? headerRowCount,
        int? dataStartRow,
        CancellationToken cancellationToken)
    {
        if (wordFile.FileType != UploadedFileType.ExcelXlsx)
        {
            // Word 匹配流程当前不使用 Excel 的 1-based 行配置，学习表头时保持同一提取口径。
            return (new ColumnMapping
            {
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            }, null);
        }

        var tables = await _documentTableAccessService.GetTablesAsync(wordFile, cancellationToken);
        if (tableIndex < 0 || tableIndex >= tables.Count)
        {
            return (new ColumnMapping(), null);
        }

        var sheetInfo = tables[tableIndex];
        var usedStartRow = Math.Max(1, sheetInfo.UsedRangeStartRow);
        var normalizedHeaderRowStart = Math.Max(
            usedStartRow,
            headerRowStart.GetValueOrDefault(usedStartRow));
        var excelHeaderRowCount = Math.Max(0, headerRowCount.GetValueOrDefault(1));
        var minDataStartRow = normalizedHeaderRowStart + excelHeaderRowCount;
        var normalizedDataStartRow = Math.Max(
            minDataStartRow,
            dataStartRow.GetValueOrDefault(minDataStartRow));

        return (new ColumnMapping
        {
            HeaderRowIndex = Math.Max(0, normalizedHeaderRowStart - usedStartRow),
            HeaderRowCount = Math.Max(1, excelHeaderRowCount == 0 ? 1 : excelHeaderRowCount),
            DataStartRowIndex = Math.Max(0, normalizedDataStartRow - usedStartRow)
        }, sheetInfo.Name);
    }

    private static List<SmartConfigurationLearnedColumn> BuildLearnedColumns(
        IReadOnlyList<string> headers,
        int? projectColumnIndex,
        int? specificationColumnIndex,
        int? acceptanceColumnIndex,
        int? remarkColumnIndex)
    {
        var result = new List<SmartConfigurationLearnedColumn>();
        var seen = new HashSet<(string Header, ColumnMappingTargetField TargetField)>();

        AddColumn(projectColumnIndex, ColumnMappingTargetField.Project);
        AddColumn(specificationColumnIndex, ColumnMappingTargetField.Specification);
        AddColumn(acceptanceColumnIndex, ColumnMappingTargetField.Acceptance);
        AddColumn(remarkColumnIndex, ColumnMappingTargetField.Remark);
        return result;

        void AddColumn(int? columnIndex, ColumnMappingTargetField targetField)
        {
            if (!columnIndex.HasValue ||
                columnIndex.Value < 0 ||
                columnIndex.Value >= headers.Count)
            {
                return;
            }

            var header = headers[columnIndex.Value].Trim();
            if (!IsLearnableHeader(header) || !seen.Add((header, targetField)))
            {
                return;
            }

            result.Add(new SmartConfigurationLearnedColumn
            {
                Header = header,
                TargetField = targetField
            });
        }
    }

    private static bool IsLearnableHeader(string header)
    {
        if (string.IsNullOrWhiteSpace(header) ||
            header.Length > MaxLearnableHeaderLength ||
            header.All(char.IsDigit))
        {
            return false;
        }

        return header.Any(char.IsLetterOrDigit);
    }
}
