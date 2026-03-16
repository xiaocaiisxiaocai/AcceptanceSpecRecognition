using AcceptanceSpecSystem.Core.Documents.Interfaces;
using AcceptanceSpecSystem.Core.Documents.Models;
using ClosedXML.Excel;

namespace AcceptanceSpecSystem.Core.Documents.Writers;

/// <summary>
/// Excel 文档写入器实现（.xlsx）
/// 约定：tableIndex 对应工作表索引（从 0 开始）。
/// </summary>
public class ExcelDocumentWriter : IDocumentWriter
{
    private static readonly string[] SupportedExtensions = { ".xlsx" };

    public DocumentType DocumentType => DocumentType.Excel;

    public bool CanWrite(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedExtensions.Contains(extension);
    }

    public Task<int> WriteTableDataAsync(string filePath, int tableIndex, IEnumerable<CellWriteOperation> operations)
    {
        return Task.Run(() =>
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return WriteTableDataInternal(stream, tableIndex, operations);
        });
    }

    public Task<int> WriteTableDataAsync(Stream stream, int tableIndex, IEnumerable<CellWriteOperation> operations)
    {
        return Task.Run(() => WriteTableDataInternal(stream, tableIndex, operations));
    }

    public Task<bool> WriteCellAsync(Stream stream, int tableIndex, int rowIndex, int columnIndex, string value)
    {
        return Task.Run(() =>
        {
            var operation = new CellWriteOperation
            {
                RowIndex = rowIndex,
                ColumnIndex = columnIndex,
                Value = value
            };

            var count = WriteTableDataInternal(stream, tableIndex, new[] { operation });
            return count > 0;
        });
    }

    public Task<int> WriteToNewFileAsync(string sourceFilePath, string targetFilePath, int tableIndex, IEnumerable<CellWriteOperation> operations)
    {
        return Task.Run(() =>
        {
            File.Copy(sourceFilePath, targetFilePath, overwrite: true);
            using var stream = File.Open(targetFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return WriteTableDataInternal(stream, tableIndex, operations);
        });
    }

    public Task<int> WriteMultipleTablesAsync(Stream stream, Dictionary<int, List<CellWriteOperation>> tableOperations)
    {
        return Task.Run(() => WriteMultipleTablesInternal(stream, tableOperations));
    }

    private int WriteTableDataInternal(Stream stream, int tableIndex, IEnumerable<CellWriteOperation> operations)
    {
        var operationsList = operations?.ToList() ?? [];
        if (operationsList.Count == 0)
            return 0;

        using var workbook = new XLWorkbook(stream);
        var sheets = workbook.Worksheets.ToList();
        if (tableIndex < 0 || tableIndex >= sheets.Count)
            throw new ArgumentOutOfRangeException(nameof(tableIndex), $"工作表索引超出范围。文档共有 {sheets.Count} 个工作表。");

        var sheet = sheets[tableIndex];
        var successCount = WriteSheetOperations(sheet, operationsList);

        SaveWorkbookToStream(workbook, stream);
        return successCount;
    }

    private int WriteMultipleTablesInternal(Stream stream, Dictionary<int, List<CellWriteOperation>> tableOperations)
    {
        if (tableOperations == null || tableOperations.Count == 0)
            return 0;

        using var workbook = new XLWorkbook(stream);
        var sheets = workbook.Worksheets.ToList();
        var totalSuccess = 0;

        foreach (var (tableIndex, operations) in tableOperations)
        {
            if (operations == null || operations.Count == 0)
                continue;

            if (tableIndex < 0 || tableIndex >= sheets.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(tableIndex),
                    $"工作表索引 {tableIndex} 超出范围。文档共有 {sheets.Count} 个工作表。");
            }

            totalSuccess += WriteSheetOperations(sheets[tableIndex], operations);
        }

        SaveWorkbookToStream(workbook, stream);
        return totalSuccess;
    }

    private static int WriteSheetOperations(IXLWorksheet sheet, List<CellWriteOperation> operations)
    {
        var usedRange = sheet.RangeUsed();
        var startRow = usedRange?.RangeAddress.FirstAddress.RowNumber ?? 1;
        var startCol = usedRange?.RangeAddress.FirstAddress.ColumnNumber ?? 1;
        var endRow = usedRange?.RangeAddress.LastAddress.RowNumber ?? int.MaxValue;
        var endCol = usedRange?.RangeAddress.LastAddress.ColumnNumber ?? int.MaxValue;
        var mergedLookup = usedRange == null
            ? null
            : BuildMergedLookup(sheet, usedRange);

        var successCount = 0;
        foreach (var operation in operations)
        {
            if (TryWriteCell(sheet, startRow, startCol, endRow, endCol, mergedLookup, operation))
            {
                successCount++;
            }
        }

        return successCount;
    }

    private static bool TryWriteCell(
        IXLWorksheet sheet,
        int startRow,
        int startCol,
        int endRow,
        int endCol,
        Dictionary<(int Row, int Col), (int MasterRow, int MasterCol)>? mergedLookup,
        CellWriteOperation operation)
    {
        if (operation.RowIndex < 0 || operation.ColumnIndex < 0)
            return false;

        var absRow = startRow + operation.RowIndex;
        var absCol = startCol + operation.ColumnIndex;

        if (absRow < startRow || absRow > endRow || absCol < startCol || absCol > endCol)
            return false;

        if (mergedLookup != null && mergedLookup.TryGetValue((absRow, absCol), out var master))
        {
            absRow = master.MasterRow;
            absCol = master.MasterCol;
        }

        sheet.Cell(absRow, absCol).Value = operation.Value ?? string.Empty;
        return true;
    }

    private static Dictionary<(int Row, int Col), (int MasterRow, int MasterCol)> BuildMergedLookup(IXLWorksheet sheet, IXLRange usedRange)
    {
        var dict = new Dictionary<(int Row, int Col), (int MasterRow, int MasterCol)>();

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

    private static void SaveWorkbookToStream(XLWorkbook workbook, Stream stream)
    {
        stream.Position = 0;
        stream.SetLength(0);
        workbook.SaveAs(stream);
        stream.Position = 0;
    }
}
