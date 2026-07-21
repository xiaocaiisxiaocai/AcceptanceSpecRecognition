using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Services;

public interface IDocumentTableQueryAppService
{
    const int MaxPreviewWindowRows = 500;
    const int MaxPreviewWindowColumns = 100;

    Task<List<TableInfoDto>> GetTablesAsync(
        SpecAccessContext scope,
        int fileId,
        CancellationToken cancellationToken = default);

    Task<TableDataDto> GetPreviewAsync(
        SpecAccessContext scope,
        int fileId,
        int tableIndex,
        int previewRows,
        int headerRowIndex,
        int headerRowCount,
        int dataStartRowIndex,
        int? dataEndRowIndex,
        int? rowOffset,
        int? columnOffset,
        int? previewColumns,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 文档表格列表与预览用例。文件范围、预览坐标和 DTO 映射均由 Application 统一拥有。
/// </summary>
public sealed class DocumentTableQueryAppService : IDocumentTableQueryAppService
{
    private static readonly Regex ListPrefixRegex = new(
        @"^(?<indent>\s*)(?<num>\d+)\s*(?<sep>[、:：])(?<space>\s*)(?<rest>.*)$",
        RegexOptions.Compiled);

    private readonly IDocumentFileAccessService _fileAccess;
    private readonly IDocumentImportTableReader _tableReader;

    public DocumentTableQueryAppService(
        IDocumentFileAccessService fileAccess,
        IDocumentImportTableReader tableReader)
    {
        _fileAccess = fileAccess;
        _tableReader = tableReader;
    }

    public async Task<List<TableInfoDto>> GetTablesAsync(
        SpecAccessContext scope,
        int fileId,
        CancellationToken cancellationToken = default)
    {
        var wordFile = await GetAccessibleFileAsync(scope, fileId, cancellationToken);
        var tables = await _tableReader.GetTablesAsync(wordFile, cancellationToken);
        return MapTableInfos(tables);
    }

    public static List<TableInfoDto> MapTableInfos(IReadOnlyList<TableInfo> tables)
    {
        return tables.Select(table => new TableInfoDto
        {
            Index = table.Index,
            Name = table.Name,
            RowCount = table.RowCount,
            ColumnCount = table.ColumnCount,
            IsNested = table.IsNested,
            PreviewText = table.PreviewText,
            Headers = table.Headers?.ToList() ?? [],
            HasMergedCells = table.HasMergedCells,
            UsedRangeStartRow = table.UsedRangeStartRow,
            UsedRangeStartColumn = table.UsedRangeStartColumn
        }).ToList();
    }

    public async Task<TableDataDto> GetPreviewAsync(
        SpecAccessContext scope,
        int fileId,
        int tableIndex,
        int previewRows,
        int headerRowIndex,
        int headerRowCount,
        int dataStartRowIndex,
        int? dataEndRowIndex,
        int? rowOffset,
        int? columnOffset,
        int? previewColumns,
        CancellationToken cancellationToken = default)
    {
        var wordFile = await GetAccessibleFileAsync(scope, fileId, cancellationToken);
        if (dataEndRowIndex.HasValue && dataEndRowIndex.Value < dataStartRowIndex)
            throw new ApplicationServiceException(400, "数据结束行不能早于数据起始行");

        var windowed = rowOffset.HasValue || columnOffset.HasValue || previewColumns.HasValue;
        var effectiveRowOffset = rowOffset ?? 0;
        var effectiveColumnOffset = columnOffset ?? 0;
        ValidateWindow(
            windowed,
            effectiveRowOffset,
            previewRows,
            effectiveColumnOffset,
            previewColumns);

        if (!windowed)
        {
            return await GetLegacyPreviewAsync(
                wordFile,
                tableIndex,
                previewRows,
                headerRowIndex,
                headerRowCount,
                dataStartRowIndex,
                dataEndRowIndex,
                cancellationToken);
        }

        var tables = await _tableReader.GetTablesAsync(wordFile, cancellationToken);
        var tableInfo = tables.FirstOrDefault(table => table.Index == tableIndex)
            ?? throw new ApplicationServiceException(404, "表格不存在");
        var totalRows = Math.Max(0, tableInfo.RowCount - dataStartRowIndex);
        if (dataEndRowIndex.HasValue)
        {
            totalRows = Math.Min(totalRows, dataEndRowIndex.Value - dataStartRowIndex + 1);
        }
        var totalColumns = Math.Max(0, tableInfo.ColumnCount);
        var boundedRowOffset = Math.Min(effectiveRowOffset, totalRows);
        var requestedColumns = previewColumns!.Value;
        var effectiveDataStartRowIndex = dataStartRowIndex + boundedRowOffset;
        var remainingRows = Math.Max(0, totalRows - boundedRowOffset);
        var rowCount = Math.Min(previewRows, remainingRows);

        var tableData = await _tableReader.ExtractTableDataAsync(
            wordFile,
            tableIndex,
            new ColumnMapping
            {
                HeaderRowIndex = headerRowIndex,
                HeaderRowCount = headerRowCount,
                DataStartRowIndex = effectiveDataStartRowIndex
            },
            rowCount,
            cancellationToken);

        return MapWindowPreview(
            tableData,
            effectiveRowOffset,
            effectiveColumnOffset,
            requestedColumns,
            totalRows,
            totalColumns);
    }

    private async Task<TableDataDto> GetLegacyPreviewAsync(
        WordFile wordFile,
        int tableIndex,
        int previewRows,
        int headerRowIndex,
        int headerRowCount,
        int dataStartRowIndex,
        int? dataEndRowIndex,
        CancellationToken cancellationToken)
    {
        var previewRangeRowCount = dataEndRowIndex.HasValue
            ? dataEndRowIndex.Value - dataStartRowIndex + 1
            : (int?)null;
        var maxDataRowCount = previewRows > 0 ? previewRows : (int?)null;
        if (previewRangeRowCount.HasValue)
        {
            maxDataRowCount = maxDataRowCount.HasValue
                ? Math.Min(maxDataRowCount.Value, previewRangeRowCount.Value)
                : previewRangeRowCount.Value;
        }

        var tableData = await _tableReader.ExtractTableDataAsync(
            wordFile,
            tableIndex,
            new ColumnMapping
            {
                HeaderRowIndex = headerRowIndex,
                HeaderRowCount = headerRowCount,
                DataStartRowIndex = dataStartRowIndex
            },
            maxDataRowCount,
            cancellationToken);

        return MapPreview(tableData, previewRows, previewRangeRowCount);
    }

    private static void ValidateWindow(
        bool windowed,
        int rowOffset,
        int previewRows,
        int columnOffset,
        int? previewColumns)
    {
        if (!windowed)
            return;
        if (rowOffset < 0)
            throw new ApplicationServiceException(400, "行偏移不能小于 0");
        if (columnOffset < 0)
            throw new ApplicationServiceException(400, "列偏移不能小于 0");
        if (previewRows <= 0 || previewRows > IDocumentTableQueryAppService.MaxPreviewWindowRows)
            throw new ApplicationServiceException(400, $"预览行数必须在 1 到 {IDocumentTableQueryAppService.MaxPreviewWindowRows} 之间");
        if (!previewColumns.HasValue || previewColumns.Value <= 0 ||
            previewColumns.Value > IDocumentTableQueryAppService.MaxPreviewWindowColumns)
            throw new ApplicationServiceException(400, $"预览列数必须在 1 到 {IDocumentTableQueryAppService.MaxPreviewWindowColumns} 之间");
    }

    private static TableDataDto MapWindowPreview(
        TableData tableData,
        int rowOffset,
        int columnOffset,
        int previewColumns,
        int totalRows,
        int totalColumns)
    {
        var availableColumns = Math.Max(0, totalColumns - columnOffset);
        var actualColumnCount = Math.Min(previewColumns, availableColumns);
        var headers = tableData.Headers
            .Skip(columnOffset)
            .Take(actualColumnCount)
            .ToList();
        var rows = tableData.Rows.ToList();

        return new TableDataDto
        {
            TableIndex = tableData.TableIndex,
            Headers = headers,
            Rows = rows.Select(row => row.Cells
                .Skip(columnOffset)
                .Take(actualColumnCount)
                .Select(FormatPreviewCellText)
                .ToList()).ToList(),
            StructuredRows = rows.Select(row => row.Cells
                .Skip(columnOffset)
                .Take(actualColumnCount)
                .Select(cell => MapStructuredCellValue(cell.StructuredValue))
                .ToList()).ToList(),
            TotalRows = totalRows,
            ColumnCount = actualColumnCount,
            RowOffset = rowOffset,
            ColumnOffset = columnOffset,
            TotalColumns = totalColumns
        };
    }

    public static TableDataDto MapPreview(TableData tableData, int previewRows, int? previewRangeRowCount)
    {
        var rows = (previewRows <= 0 ? tableData.Rows : tableData.Rows.Take(previewRows)).ToList();
        var totalRows = tableData.TotalDataRowCount ?? tableData.Rows.Count;
        if (previewRangeRowCount.HasValue)
            totalRows = Math.Max(0, Math.Min(totalRows, previewRangeRowCount.Value));

        return new TableDataDto
        {
            TableIndex = tableData.TableIndex,
            Headers = tableData.Headers.ToList(),
            Rows = rows.Select(row => row.Cells.Select(FormatPreviewCellText).ToList()).ToList(),
            StructuredRows = rows.Select(row => row.Cells.Select(cell => MapStructuredCellValue(cell.StructuredValue)).ToList()).ToList(),
            TotalRows = totalRows,
            ColumnCount = tableData.ColumnCount,
            RowOffset = 0,
            ColumnOffset = 0,
            TotalColumns = tableData.ColumnCount
        };
    }

    private async Task<WordFile> GetAccessibleFileAsync(
        SpecAccessContext scope,
        int fileId,
        CancellationToken cancellationToken)
    {
        return await _fileAccess.GetAccessibleWordFileAsync(
                   fileId, scope, includeScopedSpecs: true, cancellationToken: cancellationToken)
               ?? throw new ApplicationServiceException(404, "文件不存在");
    }

    public static StructuredCellValueDto MapStructuredCellValue(StructuredCellValue? value)
    {
        var dto = new StructuredCellValueDto();
        if (value?.Parts == null || value.Parts.Count == 0)
            return dto;
        dto.Parts = value.Parts.Select(MapStructuredPart).ToList();
        return dto;
    }

    private static StructuredCellPartDto MapStructuredPart(StructuredCellPart part) => new()
    {
        Type = part.Type,
        Text = part.Text,
        Table = part.Table == null ? null : MapStructuredTable(part.Table)
    };

    private static StructuredTableValueDto MapStructuredTable(StructuredTableValue table) => new()
    {
        RowCount = table.RowCount,
        ColumnCount = table.ColumnCount,
        Rows = table.Rows.Select(row => row.Select(MapStructuredCellValue).ToList()).ToList()
    };

    public static string FormatPreviewCellText(CellData cell)
    {
        var structuredText = ExtractStructuredText(cell.StructuredValue);
        return AlignListPrefixes(string.IsNullOrWhiteSpace(structuredText) ? cell.Value ?? string.Empty : structuredText);
    }

    private static string ExtractStructuredText(StructuredCellValue? value)
    {
        if (value?.Parts == null || value.Parts.Count == 0)
            return string.Empty;
        return string.Join("\n", value.Parts
            .Where(part => part.Type == "text" && !string.IsNullOrWhiteSpace(part.Text))
            .Select(part => part.Text!.TrimEnd()));
    }

    private static string AlignListPrefixes(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = normalized.Split('\n');
        if (lines.Length < 2)
            return normalized;

        var items = new List<(bool HasPrefix, string Original, string Indent, string Num, string Sep, string Space, string Tail)>();
        foreach (var line in lines)
        {
            var match = ListPrefixRegex.Match(line);
            items.Add(match.Success
                ? (HasPrefix: true, Original: line, Indent: match.Groups["indent"].Value,
                    Num: match.Groups["num"].Value, Sep: match.Groups["sep"].Value,
                    Space: match.Groups["space"].Value, Tail: match.Groups["rest"].Value)
                : (HasPrefix: false, Original: line, Indent: string.Empty, Num: string.Empty,
                    Sep: string.Empty, Space: string.Empty, Tail: string.Empty));
        }
        var prefixed = items.Where(item => item.HasPrefix).ToList();
        if (prefixed.Count < 2)
            return normalized;
        var maxDigits = prefixed.Max(item => item.Num.Length);
        return string.Join("\n", items.Select(item => item.HasPrefix
            ? $"{item.Indent}{item.Num.PadLeft(maxDigits)}{item.Sep}{item.Space}{item.Tail}"
            : item.Original));
    }
}
