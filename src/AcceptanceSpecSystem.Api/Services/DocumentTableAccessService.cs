using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Interfaces;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 文档表格访问协作组件。
/// </summary>
public sealed class DocumentTableAccessService
{
    private static readonly Regex ListPrefixRegex =
        new(@"^(?<indent>\s*)(?<num>\d+)\s*(?<sep>[、:：])(?<space>\s*)(?<rest>.*)$", RegexOptions.Compiled);

    private readonly DocumentServiceFactory _documentServiceFactory;
    private readonly DocumentFileAccessService _documentFileAccessService;

    public DocumentTableAccessService(
        DocumentServiceFactory documentServiceFactory,
        DocumentFileAccessService documentFileAccessService)
    {
        _documentServiceFactory = documentServiceFactory;
        _documentFileAccessService = documentFileAccessService;
    }

    public async Task<int> CountTablesAsync(UploadedFileType fileType, byte[] fileContent)
    {
        var parser = _documentServiceFactory.GetParser(GetDocumentType(fileType));
        if (parser == null)
        {
            return 0;
        }

        using var stream = new MemoryStream(fileContent);
        var tables = await parser.GetTablesAsync(stream);
        return tables.Count;
    }

    public async Task<List<TableInfoDto>> GetTableInfoDtosAsync(WordFile wordFile)
    {
        var tables = await GetTablesAsync(wordFile);
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

    public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(WordFile wordFile)
    {
        var parser = GetRequiredParser(wordFile.FileType);
        using var stream = _documentFileAccessService.OpenReadStream(wordFile);
        return await parser.GetTablesAsync(stream);
    }

    public async Task<TableData> ExtractTableDataAsync(WordFile wordFile, int tableIndex, ColumnMapping mapping)
    {
        var parser = GetRequiredParser(wordFile.FileType);
        using var stream = _documentFileAccessService.OpenReadStream(wordFile);
        try
        {
            return await parser.ExtractTableDataAsync(stream, tableIndex, mapping);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ApplicationServiceException(400, "表格索引超出范围");
        }
    }

    public async Task<TableDataDto> GetTablePreviewAsync(
        WordFile wordFile,
        int tableIndex,
        int previewRows,
        int headerRowIndex,
        int headerRowCount,
        int dataStartRowIndex)
    {
        var tableData = await ExtractTableDataAsync(
            wordFile,
            tableIndex,
            new ColumnMapping
            {
                HeaderRowIndex = headerRowIndex,
                HeaderRowCount = headerRowCount,
                DataStartRowIndex = dataStartRowIndex
            });

        var rowSource = previewRows <= 0 ? tableData.Rows : tableData.Rows.Take(previewRows);
        return new TableDataDto
        {
            TableIndex = tableData.TableIndex,
            Headers = tableData.Headers.ToList(),
            Rows = rowSource.Select(row => row.Cells.Select(FormatPreviewCellText).ToList()).ToList(),
            StructuredRows = rowSource.Select(row => row.Cells.Select(cell => MapStructuredCellValue(cell.StructuredValue)).ToList()).ToList(),
            TotalRows = tableData.Rows.Count,
            ColumnCount = tableData.ColumnCount
        };
    }

    public async Task<List<MatchSourceItem>> ExtractMatchSourceItemsAsync(
        WordFile wordFile,
        int tableIndex,
        int projectColumnIndex,
        int specificationColumnIndex,
        int? headerRowStart = null,
        int? headerRowCount = null,
        int? dataStartRow = null,
        bool filterEmptySourceRows = true)
    {
        var parser = _documentServiceFactory.GetParser(GetDocumentType(wordFile.FileType));
        if (parser == null)
        {
            return [];
        }

        using var stream = _documentFileAccessService.OpenReadStream(wordFile);
        TableData tableData;
        var excelDataStartRowIndexForWriteBack = 1;
        try
        {
            var mapping = new ColumnMapping
            {
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            };

            if (wordFile.FileType == UploadedFileType.ExcelXlsx)
            {
                IReadOnlyList<TableInfo> tables;
                using (var metaStream = _documentFileAccessService.OpenReadStream(wordFile))
                {
                    tables = await parser.GetTablesAsync(metaStream);
                }

                if (tableIndex < 0 || tableIndex >= tables.Count)
                {
                    return [];
                }

                var sheetInfo = tables[tableIndex];
                var usedStartRow = Math.Max(1, sheetInfo.UsedRangeStartRow);

                var normalizedHeaderRowStart = headerRowStart.GetValueOrDefault(usedStartRow);
                if (normalizedHeaderRowStart < usedStartRow)
                {
                    normalizedHeaderRowStart = usedStartRow;
                }

                var normalizedHeaderRowCount = headerRowCount.GetValueOrDefault(1);
                if (normalizedHeaderRowCount < 0)
                {
                    normalizedHeaderRowCount = 0;
                }

                var minDataStartRow = normalizedHeaderRowStart + normalizedHeaderRowCount;
                var normalizedDataStartRow = dataStartRow.GetValueOrDefault(minDataStartRow);
                if (normalizedDataStartRow < minDataStartRow)
                {
                    normalizedDataStartRow = minDataStartRow;
                }

                mapping = new ColumnMapping
                {
                    HeaderRowIndex = Math.Max(0, normalizedHeaderRowStart - usedStartRow),
                    HeaderRowCount = Math.Max(1, normalizedHeaderRowCount == 0 ? 1 : normalizedHeaderRowCount),
                    DataStartRowIndex = Math.Max(0, normalizedDataStartRow - usedStartRow)
                };
                excelDataStartRowIndexForWriteBack = mapping.DataStartRowIndex;
            }

            tableData = await parser.ExtractTableDataAsync(stream, tableIndex, mapping);
        }
        catch
        {
            return [];
        }

        if (tableData.ColumnCount < 2 ||
            projectColumnIndex < 0 || projectColumnIndex >= tableData.ColumnCount ||
            specificationColumnIndex < 0 || specificationColumnIndex >= tableData.ColumnCount)
        {
            return [];
        }

        var items = new List<MatchSourceItem>();
        foreach (var row in tableData.Rows)
        {
            var project = row.GetValue(projectColumnIndex) ?? string.Empty;
            var specification = row.GetValue(specificationColumnIndex) ?? string.Empty;

            if (filterEmptySourceRows &&
                string.IsNullOrWhiteSpace(project) &&
                string.IsNullOrWhiteSpace(specification))
            {
                continue;
            }

            var writeBackRowIndex = row.Index;
            if (wordFile.FileType == UploadedFileType.ExcelXlsx)
            {
                writeBackRowIndex += excelDataStartRowIndexForWriteBack;
            }

            items.Add(new MatchSourceItem
            {
                RowIndex = writeBackRowIndex,
                Project = project.Trim(),
                Specification = specification.Trim()
            });
        }

        return items;
    }

    internal async Task<List<ReplySourceItem>> ExtractReplySourceItemsAsync(
        WordFile wordFile,
        BatchTableConfig config)
    {
        var parser = _documentServiceFactory.GetParser(GetDocumentType(wordFile.FileType));
        if (parser == null)
        {
            return [];
        }

        using var stream = _documentFileAccessService.OpenReadStream(wordFile);
        TableData tableData;
        var excelDataStartRowIndexForWriteBack = 1;
        try
        {
            var mapping = new ColumnMapping
            {
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            };

            if (wordFile.FileType == UploadedFileType.ExcelXlsx)
            {
                IReadOnlyList<TableInfo> tables;
                using (var metaStream = _documentFileAccessService.OpenReadStream(wordFile))
                {
                    tables = await parser.GetTablesAsync(metaStream);
                }

                if (config.TableIndex < 0 || config.TableIndex >= tables.Count)
                {
                    return [];
                }

                var sheetInfo = tables[config.TableIndex];
                var usedStartRow = Math.Max(1, sheetInfo.UsedRangeStartRow);
                var normalizedHeaderRowStart = Math.Max(
                    usedStartRow,
                    config.HeaderRowStart.GetValueOrDefault(usedStartRow));
                var normalizedHeaderRowCount = Math.Max(0, config.HeaderRowCount.GetValueOrDefault(1));
                var minDataStartRow = normalizedHeaderRowStart + normalizedHeaderRowCount;
                var normalizedDataStartRow = Math.Max(
                    minDataStartRow,
                    config.DataStartRow.GetValueOrDefault(minDataStartRow));

                mapping = new ColumnMapping
                {
                    HeaderRowIndex = Math.Max(0, normalizedHeaderRowStart - usedStartRow),
                    HeaderRowCount = Math.Max(1, normalizedHeaderRowCount == 0 ? 1 : normalizedHeaderRowCount),
                    DataStartRowIndex = Math.Max(0, normalizedDataStartRow - usedStartRow)
                };
                excelDataStartRowIndexForWriteBack = mapping.DataStartRowIndex;
            }

            tableData = await parser.ExtractTableDataAsync(stream, config.TableIndex, mapping);
        }
        catch
        {
            return [];
        }

        var requiredColumns = new[]
        {
            config.ProjectColumnIndex,
            config.SpecificationColumnIndex,
            config.AcceptanceColumnIndex,
            config.RemarkColumnIndex ?? -1
        };
        if (requiredColumns.Any(index => index >= tableData.ColumnCount))
        {
            return [];
        }

        var filterEmptySourceRows = config.FilterEmptySourceRows ?? true;
        var items = new List<ReplySourceItem>();
        foreach (var row in tableData.Rows)
        {
            var project = row.GetValue(config.ProjectColumnIndex) ?? string.Empty;
            var specification = row.GetValue(config.SpecificationColumnIndex) ?? string.Empty;
            if (filterEmptySourceRows &&
                string.IsNullOrWhiteSpace(project) &&
                string.IsNullOrWhiteSpace(specification))
            {
                continue;
            }

            var writeBackRowIndex = row.Index;
            if (wordFile.FileType == UploadedFileType.ExcelXlsx)
            {
                writeBackRowIndex += excelDataStartRowIndexForWriteBack;
            }

            items.Add(new ReplySourceItem
            {
                RowIndex = writeBackRowIndex,
                Project = project.Trim(),
                Specification = specification.Trim(),
                Acceptance = (row.GetValue(config.AcceptanceColumnIndex) ?? string.Empty).Trim(),
                Remark = config.RemarkColumnIndex.HasValue
                    ? (row.GetValue(config.RemarkColumnIndex.Value) ?? string.Empty).Trim()
                    : null
            });
        }

        return items;
    }

    private IDocumentParser GetRequiredParser(UploadedFileType fileType)
    {
        var parser = _documentServiceFactory.GetParser(GetDocumentType(fileType));
        if (parser == null)
        {
            throw new ApplicationServiceException(500, "文档解析器不可用");
        }

        return parser;
    }

    private static DocumentType GetDocumentType(UploadedFileType fileType)
    {
        return fileType == UploadedFileType.ExcelXlsx
            ? DocumentType.Excel
            : DocumentType.Word;
    }

    private static StructuredCellValueDto MapStructuredCellValue(StructuredCellValue? value)
    {
        var dto = new StructuredCellValueDto();
        if (value?.Parts == null || value.Parts.Count == 0)
        {
            return dto;
        }

        dto.Parts = value.Parts.Select(MapStructuredPart).ToList();
        return dto;
    }

    private static StructuredCellPartDto MapStructuredPart(StructuredCellPart part)
    {
        return new StructuredCellPartDto
        {
            Type = part.Type,
            Text = part.Text,
            Table = part.Table == null ? null : MapStructuredTable(part.Table)
        };
    }

    private static StructuredTableValueDto MapStructuredTable(StructuredTableValue table)
    {
        return new StructuredTableValueDto
        {
            RowCount = table.RowCount,
            ColumnCount = table.ColumnCount,
            Rows = table.Rows.Select(row => row.Select(MapStructuredCellValue).ToList()).ToList()
        };
    }

    private static string FormatPreviewCellText(CellData cell)
    {
        var structuredText = ExtractStructuredText(cell.StructuredValue);
        var rawText = string.IsNullOrWhiteSpace(structuredText)
            ? cell.Value ?? string.Empty
            : structuredText;
        return AlignListPrefixes(rawText);
    }

    private static string ExtractStructuredText(StructuredCellValue? value)
    {
        if (value?.Parts == null || value.Parts.Count == 0)
        {
            return string.Empty;
        }

        var texts = value.Parts
            .Where(part => part.Type == "text" && !string.IsNullOrWhiteSpace(part.Text))
            .Select(part => part.Text!.TrimEnd());

        return string.Join("\n", texts);
    }

    private static string AlignListPrefixes(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = normalized.Split('\n');
        if (lines.Length < 2)
        {
            return normalized;
        }

        var items = new List<(bool HasPrefix, string Original, string Indent, string Num, string Sep, string Space, string Tail)>();
        foreach (var line in lines)
        {
            var match = ListPrefixRegex.Match(line);
            if (match.Success)
            {
                items.Add((
                    true,
                    line,
                    match.Groups["indent"].Value,
                    match.Groups["num"].Value,
                    match.Groups["sep"].Value,
                    match.Groups["space"].Value,
                    match.Groups["rest"].Value));
            }
            else
            {
                items.Add((false, line, "", "", "", "", ""));
            }
        }

        var listItems = items.Where(item => item.HasPrefix).ToList();
        if (listItems.Count < 2)
        {
            return normalized;
        }

        var maxDigits = listItems.Max(item => item.Num.Length);
        return string.Join("\n", items.Select(item =>
        {
            if (!item.HasPrefix)
            {
                return item.Original;
            }

            var paddedNum = item.Num.PadLeft(maxDigits);
            return $"{item.Indent}{paddedNum}{item.Sep}{item.Space}{item.Tail}";
        }));
    }
}

internal sealed class ReplySourceItem
{
    public int RowIndex { get; set; }

    public string Project { get; set; } = string.Empty;

    public string Specification { get; set; } = string.Empty;

    public string Acceptance { get; set; } = string.Empty;

    public string? Remark { get; set; }
}
