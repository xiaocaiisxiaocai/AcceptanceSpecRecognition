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

    public Task<int> WriteTableDataAsync(
        string filePath,
        int tableIndex,
        IEnumerable<CellWriteOperation> operations,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return WriteTableDataInternal(stream, tableIndex, operations, cancellationToken);
        }, cancellationToken);
    }

    public Task<int> WriteTableDataAsync(
        Stream stream,
        int tableIndex,
        IEnumerable<CellWriteOperation> operations,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => WriteTableDataInternal(stream, tableIndex, operations, cancellationToken),
            cancellationToken);
    }

    public Task<bool> WriteCellAsync(
        Stream stream,
        int tableIndex,
        int rowIndex,
        int columnIndex,
        string value,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var operation = new CellWriteOperation
            {
                RowIndex = rowIndex,
                ColumnIndex = columnIndex,
                Value = value
            };

            var count = WriteTableDataInternal(stream, tableIndex, new[] { operation }, cancellationToken);
            return count > 0;
        }, cancellationToken);
    }

    public Task<int> WriteToNewFileAsync(
        string sourceFilePath,
        string targetFilePath,
        int tableIndex,
        IEnumerable<CellWriteOperation> operations,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Copy(sourceFilePath, targetFilePath, overwrite: true);
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = File.Open(targetFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return WriteTableDataInternal(stream, tableIndex, operations, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                File.Delete(targetFilePath);
                throw;
            }
        }, cancellationToken);
    }

    public Task<int> WriteMultipleTablesAsync(
        Stream stream,
        Dictionary<int, List<CellWriteOperation>> tableOperations,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => WriteMultipleTablesInternal(stream, tableOperations, cancellationToken),
            cancellationToken);
    }

    private int WriteTableDataInternal(
        Stream stream,
        int tableIndex,
        IEnumerable<CellWriteOperation> operations,
        CancellationToken cancellationToken)
    {
        var operationsList = operations?.ToList() ?? [];
        cancellationToken.ThrowIfCancellationRequested();
        if (operationsList.Count == 0)
            return 0;

        using var workbook = new XLWorkbook(stream);
        cancellationToken.ThrowIfCancellationRequested();
        var sheets = workbook.Worksheets.ToList();
        if (tableIndex < 0 || tableIndex >= sheets.Count)
            throw new ArgumentOutOfRangeException(nameof(tableIndex), $"工作表索引超出范围。文档共有 {sheets.Count} 个工作表。");

        var sheet = sheets[tableIndex];
        var successCount = WriteSheetOperations(sheet, operationsList, cancellationToken);

        SaveWorkbookToStream(workbook, stream, cancellationToken);
        return successCount;
    }

    private int WriteMultipleTablesInternal(
        Stream stream,
        Dictionary<int, List<CellWriteOperation>> tableOperations,
        CancellationToken cancellationToken)
    {
        if (tableOperations == null || tableOperations.Count == 0)
            return 0;

        using var workbook = new XLWorkbook(stream);
        cancellationToken.ThrowIfCancellationRequested();
        var sheets = workbook.Worksheets.ToList();
        var totalSuccess = 0;

        foreach (var (tableIndex, operations) in tableOperations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operations == null || operations.Count == 0)
                continue;

            if (tableIndex < 0 || tableIndex >= sheets.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(tableIndex),
                    $"工作表索引 {tableIndex} 超出范围。文档共有 {sheets.Count} 个工作表。");
            }

            var sheet = sheets[tableIndex];
            totalSuccess += WriteSheetOperations(sheet, operations, cancellationToken);
        }

        SaveWorkbookToStream(workbook, stream, cancellationToken);
        return totalSuccess;
    }

    private static int WriteSheetOperations(
        IXLWorksheet sheet,
        List<CellWriteOperation> operations,
        CancellationToken cancellationToken)
    {
        var usedRange = sheet.RangeUsed();
        var startRow = usedRange?.RangeAddress.FirstAddress.RowNumber ?? 1;
        var startCol = usedRange?.RangeAddress.FirstAddress.ColumnNumber ?? 1;
        var endRow = usedRange?.RangeAddress.LastAddress.RowNumber ?? int.MaxValue;
        var endCol = usedRange?.RangeAddress.LastAddress.ColumnNumber ?? int.MaxValue;
        var mergedLookup = usedRange == null
            ? null
            : BuildMergedLookup(sheet, usedRange, cancellationToken);

        var successCount = 0;
        var writtenTargets = new HashSet<(int Row, int Col)>();
        foreach (var operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryResolveTarget(
                    startRow,
                    startCol,
                    endRow,
                    endCol,
                    mergedLookup,
                    operation,
                    out var target))
            {
                if (!writtenTargets.Add(target))
                {
                    throw new InvalidOperationException(
                        $"多个写入操作指向同一单元格 {sheet.Name}!R{target.Row}C{target.Col}，请检查合并单元格和列映射");
                }

                sheet.Cell(target.Row, target.Col).Value = operation.Value ?? string.Empty;
                successCount++;
            }
        }

        return successCount;
    }

    private static bool TryResolveTarget(
        int startRow,
        int startCol,
        int endRow,
        int endCol,
        Dictionary<(int Row, int Col), (int MasterRow, int MasterCol)>? mergedLookup,
        CellWriteOperation operation,
        out (int Row, int Col) target)
    {
        target = default;
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

        target = (absRow, absCol);
        return true;
    }

    private static Dictionary<(int Row, int Col), (int MasterRow, int MasterCol)> BuildMergedLookup(
        IXLWorksheet sheet,
        IXLRange usedRange,
        CancellationToken cancellationToken)
    {
        var dict = new Dictionary<(int Row, int Col), (int MasterRow, int MasterCol)>();

        foreach (var merged in sheet.MergedRanges)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                cancellationToken.ThrowIfCancellationRequested();
                for (var c = c1; c <= c2; c++)
                {
                    dict[(r, c)] = (masterRow, masterCol);
                }
            }
        }

        return dict;
    }

    private static void SaveWorkbookToStream(
        XLWorkbook workbook,
        Stream stream,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // ClosedXML 在从同一个可写流加载工作簿后，保存期间仍会读取原包中的
        // Structured Table 关系。先截断原流会让这些关系失效，因此必须先完整
        // 序列化到独立缓冲区，再替换调用方流。
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        cancellationToken.ThrowIfCancellationRequested();
        output.Position = 0;
        stream.Position = 0;
        stream.SetLength(0);
        output.CopyTo(stream);
        stream.Position = 0;
    }
}
