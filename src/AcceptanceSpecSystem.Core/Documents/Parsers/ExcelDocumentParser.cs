using AcceptanceSpecSystem.Core.Documents.Interfaces;
using AcceptanceSpecSystem.Core.Documents.Models;
using ClosedXML.Excel;

namespace AcceptanceSpecSystem.Core.Documents.Parsers;

/// <summary>
/// Excel 文档解析器（.xlsx）
/// - 以工作表作为“表格”（Table）概念
/// - 支持合并单元格展开（表头+数据区）
/// - 以已用区域（Used Range）作为读取边界
/// </summary>
public class ExcelDocumentParser : IDocumentParser
{
    public DocumentType DocumentType => DocumentType.Excel;

    public bool CanParse(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext == ".xlsx";
    }

    public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        return await Task.FromResult(GetSheetInfos(workbook));
    }

    public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        return await Task.FromResult(GetSheetInfos(workbook));
    }

    public async Task<TableData> ExtractTableDataAsync(Stream stream, int tableIndex, ColumnMapping? mapping = null)
    {
        using var workbook = new XLWorkbook(stream);
        return await Task.FromResult(ExtractSheetTableData(workbook, tableIndex, mapping));
    }

    public async Task<TableData> ExtractTableDataAsync(string filePath, int tableIndex, ColumnMapping? mapping = null)
    {
        using var workbook = new XLWorkbook(filePath);
        return await Task.FromResult(ExtractSheetTableData(workbook, tableIndex, mapping));
    }

    public async Task<IReadOnlyList<TableData>> ExtractAllTablesDataAsync(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var infos = GetSheetInfos(workbook);
        var list = new List<TableData>(infos.Count);
        foreach (var info in infos)
        {
            list.Add(ExtractSheetTableData(workbook, info.Index, mapping: null));
        }

        return await Task.FromResult(list);
    }

    private static List<TableInfo> GetSheetInfos(XLWorkbook workbook)
    {
        var list = new List<TableInfo>();

        var index = 0;
        foreach (var sheet in workbook.Worksheets)
        {
            var used = sheet.RangeUsed();
            var hasMerged = sheet.MergedRanges?.Count > 0;

            var startRow = used?.RangeAddress.FirstAddress.RowNumber ?? 1;
            var startCol = used?.RangeAddress.FirstAddress.ColumnNumber ?? 1;
            var rowCount = used?.RowCount() ?? 0;
            var colCount = used?.ColumnCount() ?? 0;

            // 默认以已用区域的第一行作为 headers（仅用于列表展示；真实导入按用户配置）
            IReadOnlyList<string>? headers = null;
            if (used != null && rowCount > 0 && colCount > 0)
            {
                var headerRow = startRow;
                var cols = new List<string>(colCount);
                for (var c = 0; c < colCount; c++)
                {
                    var absCol = startCol + c;
                    cols.Add(GetCellString(sheet, headerRow, absCol, mergedLookup: null));
                }
                headers = cols;
            }

            string? previewText = null;
            if (used != null && rowCount > 0 && colCount > 0)
            {
                var sample = GetCellString(sheet, startRow, startCol, mergedLookup: null);
                previewText = string.IsNullOrWhiteSpace(sample) ? null : sample;
            }

            list.Add(new TableInfo
            {
                Index = index,
                Name = sheet.Name,
                RowCount = rowCount,
                ColumnCount = colCount,
                IsNested = false,
                PreviewText = previewText,
                Headers = headers,
                HasMergedCells = hasMerged,
                UsedRangeStartRow = startRow,
                UsedRangeStartColumn = startCol
            });

            index++;
        }

        return list;
    }

    private static TableData ExtractSheetTableData(XLWorkbook workbook, int tableIndex, ColumnMapping? mapping)
    {
        var sheets = workbook.Worksheets.ToList();
        if (tableIndex < 0 || tableIndex >= sheets.Count)
            throw new ArgumentOutOfRangeException(nameof(tableIndex));

        var sheet = sheets[tableIndex];
        var used = sheet.RangeUsed();

        // 空工作表：返回空表
        if (used == null)
        {
            return new TableData
            {
                TableIndex = tableIndex,
                Headers = Array.Empty<string>(),
                Rows = new List<RowData>()
            };
        }

        var startRow = used.RangeAddress.FirstAddress.RowNumber;
        var startCol = used.RangeAddress.FirstAddress.ColumnNumber;
        var rowCount = used.RowCount();
        var colCount = used.ColumnCount();

        var headerRowIndex = mapping?.HeaderRowIndex ?? 0;
        var dataStartRowIndex = mapping?.DataStartRowIndex ?? 1;

        if (headerRowIndex < 0) headerRowIndex = 0;
        if (dataStartRowIndex < 0) dataStartRowIndex = 0;

        // 合并单元格展开：建立“子单元格 -> 左上角单元格”映射
        var mergedLookup = BuildMergedLookup(sheet, used);

        // headers：支持多行表头（HeaderRowCount），按列将每一行非空片段用 " / " 拼接
        var headerAbsRow = startRow + headerRowIndex;
        var headerRowCount = mapping?.HeaderRowCount ?? 1;
        if (headerRowCount < 1) headerRowCount = 1;

        var headers = new string[colCount];
        for (var c = 0; c < colCount; c++)
        {
            var absCol = startCol + c;
            var parts = new List<string>(headerRowCount);
            for (var k = 0; k < headerRowCount; k++)
            {
                var r = headerAbsRow + k;
                // 超出已用区域则停止
                if (r > startRow + rowCount - 1) break;
                var v = GetCellString(sheet, r, absCol, mergedLookup).Trim();
                if (!string.IsNullOrWhiteSpace(v))
                    parts.Add(v);
            }

            headers[c] = parts.Count == 0 ? string.Empty : string.Join(" / ", parts);
        }

        var rows = new List<RowData>();
        var dataAbsStartRow = startRow + dataStartRowIndex;
        var dataAbsEndRow = startRow + rowCount - 1;

        var rowIndex = 0;
        for (var r = dataAbsStartRow; r <= dataAbsEndRow; r++)
        {
            var row = new RowData { Index = rowIndex };
            for (var c = 0; c < colCount; c++)
            {
                var absCol = startCol + c;
                var val = GetCellString(sheet, r, absCol, mergedLookup);
                row.Cells.Add(new CellData
                {
                    ColumnIndex = c,
                    Value = val
                });
            }
            rows.Add(row);
            rowIndex++;
        }

        return new TableData
        {
            TableIndex = tableIndex,
            Headers = headers,
            Rows = rows
        };
    }

    private static Dictionary<(int Row, int Col), (int MasterRow, int MasterCol)> BuildMergedLookup(IXLWorksheet sheet, IXLRange usedRange)
    {
        var dict = new Dictionary<(int Row, int Col), (int MasterRow, int MasterCol)>();

        // 仅考虑与已用区域相交的合并区域，避免大表性能问题
        foreach (var merged in sheet.MergedRanges)
        {
            if (!merged.Intersects(usedRange))
                continue;

            var master = merged.RangeAddress.FirstAddress;
            var masterRow = master.RowNumber;
            var masterCol = master.ColumnNumber;

            var r1 = merged.RangeAddress.FirstAddress.RowNumber;
            var c1 = merged.RangeAddress.FirstAddress.ColumnNumber;
            var r2 = merged.RangeAddress.LastAddress.RowNumber;
            var c2 = merged.RangeAddress.LastAddress.ColumnNumber;

            for (var r = r1; r <= r2; r++)
            {
                for (var c = c1; c <= c2; c++)
                {
                    dict[(r, c)] = (masterRow, masterCol);
                }
            }
        }

        return dict;
    }

    private static string GetCellString(IXLWorksheet sheet, int absRow, int absCol, Dictionary<(int Row, int Col), (int MasterRow, int MasterCol)>? mergedLookup)
    {
        if (mergedLookup != null && mergedLookup.TryGetValue((absRow, absCol), out var master))
        {
            absRow = master.MasterRow;
            absCol = master.MasterCol;
        }

        var cell = sheet.Cell(absRow, absCol);
        if (cell == null)
            return string.Empty;

        // 统一按文本读取：避免数值/日期精度争议；公式不做特别处理
        var text = cell.GetFormattedString();
        return text ?? string.Empty;
    }
}
