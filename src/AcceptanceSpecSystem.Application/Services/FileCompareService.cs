using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Interfaces;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using CoreDocumentType = AcceptanceSpecSystem.Core.Documents.Models.DocumentType;

namespace AcceptanceSpecSystem.Application.Services;

public interface IFileCompareService
{
    Task<FileCompareResult> CompareAsync(WordFile fileA, WordFile fileB, CancellationToken cancellationToken = default);
}

public interface IFileCompareDocumentParser
{
    Task<IReadOnlyList<TableInfo>> GetTablesAsync(Stream content, CancellationToken cancellationToken);

    Task<TableData> ExtractTableDataAsync(
        Stream content,
        int tableIndex,
        ColumnMapping mapping,
        int maxDataRowCount,
        CancellationToken cancellationToken);
}

public sealed class FileCompareDocumentParser(DocumentServiceFactory factory) : IFileCompareDocumentParser
{
    public Task<IReadOnlyList<TableInfo>> GetTablesAsync(
        Stream content,
        CancellationToken cancellationToken) =>
        Resolve().GetTablesAsync(content, cancellationToken);

    public Task<TableData> ExtractTableDataAsync(
        Stream content,
        int tableIndex,
        ColumnMapping mapping,
        int maxDataRowCount,
        CancellationToken cancellationToken) =>
        Resolve().ExtractTableDataAsync(
            content, tableIndex, mapping, maxDataRowCount, cancellationToken: cancellationToken);

    private IDocumentParser Resolve() =>
        factory.GetParser(CoreDocumentType.Excel)
        ?? throw new InvalidOperationException("文档解析器不可用");
}

public class FileCompareResult
{
    public UploadedFileType FileType { get; set; }
    public List<FileCompareDiffItem> Items { get; set; } = new();
    public List<FileCompareHunk> Hunks { get; set; } = new();
}

public class FileCompareDiffItem
{
    public FileCompareDiffType DiffType { get; set; }
    public FileCompareLocation Location { get; set; } = new();
    public string? OriginalText { get; set; }
    public string? CurrentText { get; set; }
    public string? DisplayLocation { get; set; }
}

public class FileCompareHunk
{
    public int StartItemIndex { get; set; }
    public int EndItemIndex { get; set; }
    public string? RangeText { get; set; }
    public List<FileCompareHunkLine> Lines { get; set; } = new();
}

public class FileCompareHunkLine
{
    public string LineType { get; set; } = string.Empty;
    public int ItemIndex { get; set; }
    public string? ChangeGroupId { get; set; }
    public string? DisplayLocation { get; set; }
    public string? OriginalText { get; set; }
    public string? CurrentText { get; set; }
}

public class FileCompareLocation
{
    public string DocumentType { get; set; } = string.Empty;
    public int? TableIndex { get; set; }
    public string? SheetName { get; set; }
    public int? RowIndex { get; set; }
    public int? ColumnIndex { get; set; }
    public string? Address { get; set; }
}

public enum FileCompareDiffType
{
    Unchanged = 0,
    Added = 1,
    Removed = 2,
    Modified = 3
}

public class FileCompareService : IFileCompareService
{
    private const long MaxLcsMatrixCells = 250_000;
    private const int ChunkLookAhead = 80;
    private const int MaxCompareRowsPerSheet = 20_000;
    private const int MaxCompareColumnsPerSheet = 100;
    private readonly IFileStorageService _fileStorage;
    private readonly IResourceBudgetGovernor _resourceBudgetGovernor;
    private readonly IFileCompareDocumentParser _excelParser;
    private readonly IFileCompareTemporaryStorage _temporaryStorage;

    public FileCompareService(
        DocumentServiceFactory documentServiceFactory,
        IFileStorageService fileStorage,
        IResourceBudgetGovernor resourceBudgetGovernor,
        IFileCompareTemporaryStorage temporaryStorage,
        IFileCompareDocumentParser? excelParser = null)
    {
        _fileStorage = fileStorage;
        _resourceBudgetGovernor = resourceBudgetGovernor;
        _temporaryStorage = temporaryStorage;
        _excelParser = excelParser ?? new FileCompareDocumentParser(documentServiceFactory);
    }

    public async Task<FileCompareResult> CompareAsync(WordFile fileA, WordFile fileB, CancellationToken cancellationToken = default)
    {
        if (fileA.FileType != fileB.FileType)
            throw new InvalidOperationException("仅支持同类型文件对比");

        await using var documentA = await ComparisonDocument.CreateAsync(
            fileA, _fileStorage, _temporaryStorage, _resourceBudgetGovernor, cancellationToken);
        await using var documentB = await ComparisonDocument.CreateAsync(
            fileB, _fileStorage, _temporaryStorage, _resourceBudgetGovernor, cancellationToken);
        return fileA.FileType switch
        {
            UploadedFileType.WordDocx => await CompareWordAsync(documentA, documentB, cancellationToken),
            UploadedFileType.ExcelXlsx => await CompareExcelAsync(documentA, documentB, cancellationToken),
            _ => throw new InvalidOperationException("不支持的文件类型")
        };
    }

    private Task<FileCompareResult> CompareWordAsync(
        ComparisonDocument documentA,
        ComparisonDocument documentB,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long nodes = 0;
        using var streamA = documentA.OpenRead();
        var paragraphsA = ExtractWordParagraphs(streamA, ref nodes, cancellationToken);
        using var streamB = documentB.OpenRead();
        var paragraphsB = ExtractWordParagraphs(streamB, ref nodes, cancellationToken);
        var ops = BuildParagraphDiff(paragraphsA, paragraphsB, cancellationToken);
        var items = BuildWordDiffItems(ops, cancellationToken);

        return Task.FromResult(new FileCompareResult
        {
            FileType = UploadedFileType.WordDocx,
            Items = items,
            Hunks = BuildDiffHunks(items, cancellationToken)
        });
    }

    private async Task<FileCompareResult> CompareExcelAsync(
        ComparisonDocument documentA,
        ComparisonDocument documentB,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TableInfo> sheetsA;
        IReadOnlyList<TableInfo> sheetsB;
        using (var streamA = documentA.OpenRead())
            sheetsA = await _excelParser.GetTablesAsync(streamA, cancellationToken);
        using (var streamB = documentB.OpenRead())
            sheetsB = await _excelParser.GetTablesAsync(streamB, cancellationToken);
        ValidateCompareDimensions(sheetsA);
        ValidateCompareDimensions(sheetsB);
        long predictedNodes = 0;
        ValidateExcelMetadataNodes(sheetsA, ref predictedNodes);
        ValidateExcelMetadataNodes(sheetsB, ref predictedNodes);
        var max = Math.Max(sheetsA.Count, sheetsB.Count);

        var mapping = new ColumnMapping
        {
            HeaderRowIndex = 0,
            HeaderRowCount = 1,
            DataStartRowIndex = 0
        };

        var items = new List<FileCompareDiffItem>();
        long actualNodes = 0;
        long diffItems = 0;

        for (var sheetIndex = 0; sheetIndex < max; sheetIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TableInfo? infoA = sheetIndex < sheetsA.Count ? sheetsA[sheetIndex] : null;
            TableInfo? infoB = sheetIndex < sheetsB.Count ? sheetsB[sheetIndex] : null;

            TableData? tableA = null;
            TableData? tableB = null;

            if (infoA != null)
            {
                using var streamA = documentA.OpenRead();
                tableA = await _excelParser.ExtractTableDataAsync(
                    streamA,
                    sheetIndex,
                    mapping,
                    MaxCompareRowsPerSheet,
                    cancellationToken: cancellationToken);
            }
            if (infoB != null)
            {
                using var streamB = documentB.OpenRead();
                tableB = await _excelParser.ExtractTableDataAsync(
                    streamB,
                    sheetIndex,
                    mapping,
                    MaxCompareRowsPerSheet,
                    cancellationToken: cancellationToken);
            }

            var mapA = BuildExcelCellMap(tableA, infoA, ref actualNodes, cancellationToken);
            var mapB = BuildExcelCellMap(tableB, infoB, ref actualNodes, cancellationToken);

            foreach (var key in GetUnionKeys(mapA, mapB, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                mapA.TryGetValue(key, out var aVal);
                mapB.TryGetValue(key, out var bVal);

                var diffType = aVal == bVal
                    ? FileCompareDiffType.Unchanged
                    : aVal == null
                        ? FileCompareDiffType.Added
                        : bVal == null
                            ? FileCompareDiffType.Removed
                            : FileCompareDiffType.Modified;

                var sheetName = infoB?.Name ?? infoA?.Name ?? $"Sheet{sheetIndex + 1}";
                var address = $"{ToExcelColumnName(key.ColumnIndex)}{key.RowIndex}";

                if (diffType != FileCompareDiffType.Unchanged)
                    _resourceBudgetGovernor.ValidateFileCompareDiffItems(++diffItems);
                items.Add(new FileCompareDiffItem
                {
                    DiffType = diffType,
                    OriginalText = aVal,
                    CurrentText = bVal,
                    Location = new FileCompareLocation
                    {
                        DocumentType = "Excel",
                        TableIndex = sheetIndex,
                        SheetName = sheetName,
                        RowIndex = key.RowIndex,
                        ColumnIndex = key.ColumnIndex,
                        Address = address
                    },
                    DisplayLocation = $"{sheetName}!{address}"
                });
            }
        }

        return new FileCompareResult
        {
            FileType = UploadedFileType.ExcelXlsx,
            Items = items,
            Hunks = BuildDiffHunks(items, cancellationToken)
        };
    }

    private void ValidateExcelMetadataNodes(IReadOnlyList<TableInfo> sheets, ref long total)
    {
        foreach (var sheet in sheets)
        {
            var headerCells = sheet.RowCount > 0
                ? Math.Min(sheet.ColumnCount, sheet.Headers?.Count ?? sheet.ColumnCount)
                : 0;
            total = checked(total + headerCells + checked((long)sheet.RowCount * sheet.ColumnCount));
            _resourceBudgetGovernor.ValidateFileCompareCells(total);
        }
    }

    private static void ValidateCompareDimensions(IReadOnlyList<TableInfo> sheets)
    {
        foreach (var sheet in sheets)
        {
            if (sheet.RowCount > MaxCompareRowsPerSheet)
            {
                throw new FileCompareBudgetExceededException("file_compare_sheet_rows");
            }
            if (sheet.ColumnCount > MaxCompareColumnsPerSheet)
            {
                throw new FileCompareBudgetExceededException("file_compare_sheet_columns");
            }
        }
    }

    private static Dictionary<WordCellKey, string> BuildCellMap(TableData? tableData)
    {
        var map = new Dictionary<WordCellKey, string>();
        if (tableData == null)
            return map;

        foreach (var row in tableData.Rows)
        {
            foreach (var cell in row.Cells)
            {
                var value = (cell.Value ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                var key = new WordCellKey(row.Index, cell.ColumnIndex);
                map[key] = value;
            }
        }

        return map;
    }

    private List<string> ExtractWordParagraphs(
        Stream content,
        ref long nodeCount,
        CancellationToken cancellationToken)
    {
        var list = new List<string>();
        using var doc = WordprocessingDocument.Open(content, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null)
            return list;

        var counters = new Dictionary<(int NumId, int Level), int>();

        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            _resourceBudgetGovernor.ValidateFileCompareCells(++nodeCount);
            var text = GetParagraphPlainText(paragraph, cancellationToken).Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (TryGetParagraphNumbering(paragraph, out var numId, out var level))
            {
                var key = (numId, level);
                var next = counters.TryGetValue(key, out var current) ? current + 1 : 1;
                counters[key] = next;

                text = StripLeadingListPrefix(NormalizeLeadingListPrefix(text));
                var prefix = $"{next}、";
                if (level > 0)
                    text = new string(' ', level * 2) + prefix + text;
                else
                    text = prefix + text;
            }

            list.Add(text);
        }

        return list;
    }

    private enum DiffOpType
    {
        Equal,
        Add,
        Remove
    }

    private readonly record struct DiffOp(DiffOpType Type, string Text, int IndexA, int IndexB);

    private List<DiffOp> BuildParagraphDiff(
        IReadOnlyList<string> a,
        IReadOnlyList<string> b,
        CancellationToken cancellationToken)
    {
        var n = a.Count;
        var m = b.Count;
        long changedOps = 0;

        // 大文档走分块近似算法，避免 O(n*m) 动态规划造成高延迟与高内存占用
        if ((long)n * m > MaxLcsMatrixCells)
        {
            return BuildParagraphDiffByChunk(
                a, b, ChunkLookAhead, ref changedOps, cancellationToken);
        }

        var dp = new int[n + 1, m + 1];

        for (var i = 1; i <= n; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var j = 1; j <= m; j++)
            {
                if (a[i - 1] == b[j - 1])
                {
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                }
                else
                {
                    dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }
            }
        }

        var ops = new List<DiffOp>();
        var x = n;
        var y = m;

        while (x > 0 && y > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (a[x - 1] == b[y - 1])
            {
                AppendDiffOp(ops, new DiffOp(DiffOpType.Equal, a[x - 1], x - 1, y - 1), ref changedOps, cancellationToken);
                x--;
                y--;
            }
            else if (dp[x - 1, y] >= dp[x, y - 1])
            {
                AppendDiffOp(ops, new DiffOp(DiffOpType.Remove, a[x - 1], x - 1, -1), ref changedOps, cancellationToken);
                x--;
            }
            else
            {
                AppendDiffOp(ops, new DiffOp(DiffOpType.Add, b[y - 1], -1, y - 1), ref changedOps, cancellationToken);
                y--;
            }
        }

        while (x > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendDiffOp(ops, new DiffOp(DiffOpType.Remove, a[x - 1], x - 1, -1), ref changedOps, cancellationToken);
            x--;
        }

        while (y > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendDiffOp(ops, new DiffOp(DiffOpType.Add, b[y - 1], -1, y - 1), ref changedOps, cancellationToken);
            y--;
        }

        for (var left = 0; left < ops.Count / 2; left++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var right = ops.Count - left - 1;
            (ops[left], ops[right]) = (ops[right], ops[left]);
        }
        return ops;
    }

    private List<DiffOp> BuildParagraphDiffByChunk(
        IReadOnlyList<string> a,
        IReadOnlyList<string> b,
        int lookAhead,
        ref long changedOps,
        CancellationToken cancellationToken)
    {
        var ops = new List<DiffOp>();
        var i = 0;
        var j = 0;

        while (i < a.Count && j < b.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (a[i] == b[j])
            {
                AppendDiffOp(ops, new DiffOp(DiffOpType.Equal, a[i], i, j), ref changedOps, cancellationToken);
                i++;
                j++;
                continue;
            }

            var matchInB = FindMatchIndex(b, j + 1, lookAhead, a[i], cancellationToken);
            var matchInA = FindMatchIndex(a, i + 1, lookAhead, b[j], cancellationToken);

            if (matchInB >= 0 && (matchInA < 0 || matchInB - j <= matchInA - i))
            {
                while (j < matchInB)
                {
                    AppendDiffOp(ops, new DiffOp(DiffOpType.Add, b[j], -1, j), ref changedOps, cancellationToken);
                    j++;
                }
                continue;
            }

            if (matchInA >= 0)
            {
                while (i < matchInA)
                {
                    AppendDiffOp(ops, new DiffOp(DiffOpType.Remove, a[i], i, -1), ref changedOps, cancellationToken);
                    i++;
                }
                continue;
            }

            // 无近邻锚点：按同位置差异处理，后续会组合为 Modified
            AppendDiffOp(ops, new DiffOp(DiffOpType.Remove, a[i], i, -1), ref changedOps, cancellationToken);
            AppendDiffOp(ops, new DiffOp(DiffOpType.Add, b[j], -1, j), ref changedOps, cancellationToken);
            i++;
            j++;
        }

        while (i < a.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendDiffOp(ops, new DiffOp(DiffOpType.Remove, a[i], i, -1), ref changedOps, cancellationToken);
            i++;
        }

        while (j < b.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendDiffOp(ops, new DiffOp(DiffOpType.Add, b[j], -1, j), ref changedOps, cancellationToken);
            j++;
        }

        return ops;
    }

    private static int FindMatchIndex(
        IReadOnlyList<string> source,
        int start,
        int lookAhead,
        string expected,
        CancellationToken cancellationToken)
    {
        if (start >= source.Count)
            return -1;

        var end = Math.Min(source.Count - 1, start + lookAhead);
        for (var k = start; k <= end; k++)
        {
            if ((k & 31) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            if (source[k] == expected)
                return k;
        }

        return -1;
    }

    private void AppendDiffOp(
        List<DiffOp> operations,
        DiffOp operation,
        ref long changedOperations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.Type != DiffOpType.Equal)
        {
            var nextChangedOperations = checked(changedOperations + 1);
            _resourceBudgetGovernor.ValidateFileCompareDiffItems(
                checked((nextChangedOperations + 1) / 2));
            changedOperations = nextChangedOperations;
        }
        operations.Add(operation);
    }

    private List<FileCompareDiffItem> BuildWordDiffItems(
        IReadOnlyList<DiffOp> ops,
        CancellationToken cancellationToken)
    {
        var items = new List<FileCompareDiffItem>();
        long diffItems = 0;
        for (var i = 0; i < ops.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var op = ops[i];
            if (i + 1 < ops.Count &&
                ((op.Type == DiffOpType.Remove && ops[i + 1].Type == DiffOpType.Add) ||
                 (op.Type == DiffOpType.Add && ops[i + 1].Type == DiffOpType.Remove)))
            {
                var next = ops[i + 1];
                var remove = op.Type == DiffOpType.Remove ? op : next;
                var add = op.Type == DiffOpType.Add ? op : next;
                _resourceBudgetGovernor.ValidateFileCompareDiffItems(++diffItems);
                var locIndex = remove.IndexA >= 0 ? remove.IndexA : add.IndexB;
                items.Add(new FileCompareDiffItem
                {
                    DiffType = FileCompareDiffType.Modified,
                    OriginalText = remove.Text,
                    CurrentText = add.Text,
                    Location = new FileCompareLocation
                    {
                        DocumentType = "Word",
                        RowIndex = locIndex
                    },
                    DisplayLocation = $"段落{locIndex + 1}"
                });
                i++;
                continue;
            }

            if (op.Type == DiffOpType.Equal)
            {
                var locIndex = op.IndexA;
                items.Add(new FileCompareDiffItem
                {
                    DiffType = FileCompareDiffType.Unchanged,
                    OriginalText = op.Text,
                    CurrentText = op.Text,
                    Location = new FileCompareLocation
                    {
                        DocumentType = "Word",
                        RowIndex = locIndex
                    },
                    DisplayLocation = $"段落{locIndex + 1}"
                });
            }
            else if (op.Type == DiffOpType.Add)
            {
                _resourceBudgetGovernor.ValidateFileCompareDiffItems(++diffItems);
                var locIndex = op.IndexB;
                items.Add(new FileCompareDiffItem
                {
                    DiffType = FileCompareDiffType.Added,
                    CurrentText = op.Text,
                    Location = new FileCompareLocation
                    {
                        DocumentType = "Word",
                        RowIndex = locIndex
                    },
                    DisplayLocation = $"段落{locIndex + 1}"
                });
            }
            else
            {
                _resourceBudgetGovernor.ValidateFileCompareDiffItems(++diffItems);
                var locIndex = op.IndexA;
                items.Add(new FileCompareDiffItem
                {
                    DiffType = FileCompareDiffType.Removed,
                    OriginalText = op.Text,
                    Location = new FileCompareLocation
                    {
                        DocumentType = "Word",
                        RowIndex = locIndex
                    },
                    DisplayLocation = $"段落{locIndex + 1}"
                });
            }
        }

        return items;
    }

    private static string GetParagraphPlainText(
        Paragraph paragraph,
        CancellationToken cancellationToken)
    {
        var builder = new System.Text.StringBuilder();
        var index = 0;
        foreach (var run in paragraph.Descendants<Run>())
        {
            if ((index++ & 63) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            builder.Append(run.InnerText);
        }
        return builder.ToString();
    }

    private static bool TryGetParagraphNumbering(Paragraph paragraph, out int numId, out int ilvl)
    {
        numId = 0;
        ilvl = 0;

        var np = paragraph.ParagraphProperties?.NumberingProperties;
        if (np?.NumberingId?.Val == null)
            return false;

        numId = (int)np.NumberingId.Val.Value;
        ilvl = (int)(np.NumberingLevelReference?.Val?.Value ?? 0);
        return true;
    }

    private static string NormalizeLeadingListPrefix(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var first = text[0];
        if (first is >= '\u2460' and <= '\u2473')
        {
            var n = first - '\u2460' + 1;
            var rest = text.Substring(1).TrimStart();
            rest = System.Text.RegularExpressions.Regex.Replace(rest, @"^[\)\）\.\、\s]+", "");
            return $"{n}、{rest}".TrimEnd();
        }

        if (first is >= '\u2474' and <= '\u2487')
        {
            var n = first - '\u2474' + 1;
            var rest = text.Substring(1).TrimStart();
            rest = System.Text.RegularExpressions.Regex.Replace(rest, @"^[\)\）\.\、\s]+", "");
            return $"{n}、{rest}".TrimEnd();
        }

        var m = System.Text.RegularExpressions.Regex.Match(
            text,
            @"^\s*(?:[\(\（]\s*)?(?<n>\d+)\s*(?:[\)\）]\s*)?(?:(?<sep>[、\.])\s+|\)\s+|）\s+)(?<rest>[\s\S]+)$"
        );
        if (m.Success)
        {
            var n = m.Groups["n"].Value;
            var rest = m.Groups["rest"].Value.TrimStart();
            return $"{n}、{rest}".TrimEnd();
        }

        return text.TrimEnd();
    }

    private static string StripLeadingListPrefix(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        text = NormalizeLeadingListPrefix(text.TrimStart());

        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^\s*\d+、\s*"))
        {
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*\d+、\s*", "");
        }

        return text;
    }

    private Dictionary<WordCellKey, string> BuildExcelCellMap(
        TableData? tableData,
        TableInfo? info,
        ref long nodeCount,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<WordCellKey, string>();
        if (tableData == null || info == null)
            return map;

        var startRow = info.UsedRangeStartRow;
        var startCol = info.UsedRangeStartColumn;
        foreach (var _ in tableData.Headers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _resourceBudgetGovernor.ValidateFileCompareCells(++nodeCount);
        }

        foreach (var row in tableData.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var cell in row.Cells)
            {
                _resourceBudgetGovernor.ValidateFileCompareCells(++nodeCount);
                var value = (cell.Value ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                var absRow = startRow + row.Index;
                var absCol = startCol + cell.ColumnIndex;
                var key = new WordCellKey(absRow, absCol);
                map[key] = value;
            }
        }

        return map;
    }

    private static IEnumerable<WordCellKey> GetUnionKeys(
        Dictionary<WordCellKey, string> mapA,
        Dictionary<WordCellKey, string> mapB,
        CancellationToken cancellationToken)
    {
        var set = new SortedSet<WordCellKey>(
            Comparer<WordCellKey>.Create((left, right) =>
            {
                var row = left.RowIndex.CompareTo(right.RowIndex);
                return row != 0 ? row : left.ColumnIndex.CompareTo(right.ColumnIndex);
            }));
        foreach (var key in mapA.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            set.Add(key);
        }
        foreach (var key in mapB.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            set.Add(key);
        }
        return EnumerateWithCancellation(set, cancellationToken);
    }

    private static IEnumerable<WordCellKey> EnumerateWithCancellation(
        IEnumerable<WordCellKey> source,
        CancellationToken cancellationToken)
    {
        foreach (var key in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return key;
        }
    }

    private static string ToExcelColumnName(int columnNumber)
    {
        var dividend = columnNumber;
        var columnName = string.Empty;
        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            columnName = Convert.ToChar('A' + modulo) + columnName;
            dividend = (dividend - modulo) / 26;
        }

        return columnName;
    }

    private static List<FileCompareHunk> BuildDiffHunks(
        IReadOnlyList<FileCompareDiffItem> items,
        CancellationToken cancellationToken,
        int contextLineCount = 2)
    {
        var changedIndices = new List<int>();
        for (var index = 0; index < items.Count; index++)
        {
            if ((index & 255) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            if (items[index].DiffType != FileCompareDiffType.Unchanged)
                changedIndices.Add(index);
        }

        if (changedIndices.Count == 0)
            return new List<FileCompareHunk>();

        var ranges = new List<(int Start, int End)>();
        foreach (var index in changedIndices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = Math.Max(0, index - contextLineCount);
            var end = Math.Min(items.Count - 1, index + contextLineCount);
            if (ranges.Count == 0 || start > ranges[^1].End + 1)
            {
                ranges.Add((start, end));
                continue;
            }

            var last = ranges[^1];
            ranges[^1] = (last.Start, Math.Max(last.End, end));
        }

        var hunks = new List<FileCompareHunk>();
        foreach (var (start, end) in ranges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hunk = new FileCompareHunk
            {
                StartItemIndex = start + 1,
                EndItemIndex = end + 1,
                RangeText = BuildHunkRangeText(items, start, end)
            };

            for (var i = start; i <= end; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = items[i];
                if (item.DiffType == FileCompareDiffType.Modified)
                {
                    var groupId = $"m-{i + 1}";
                    hunk.Lines.Add(new FileCompareHunkLine
                    {
                        LineType = "Remove",
                        ItemIndex = i + 1,
                        ChangeGroupId = groupId,
                        DisplayLocation = item.DisplayLocation,
                        OriginalText = item.OriginalText
                    });
                    hunk.Lines.Add(new FileCompareHunkLine
                    {
                        LineType = "Add",
                        ItemIndex = i + 1,
                        ChangeGroupId = groupId,
                        DisplayLocation = item.DisplayLocation,
                        CurrentText = item.CurrentText
                    });
                    continue;
                }

                hunk.Lines.Add(new FileCompareHunkLine
                {
                    LineType = item.DiffType switch
                    {
                        FileCompareDiffType.Added => "Add",
                        FileCompareDiffType.Removed => "Remove",
                        _ => "Context"
                    },
                    ItemIndex = i + 1,
                    DisplayLocation = item.DisplayLocation,
                    OriginalText = item.OriginalText,
                    CurrentText = item.CurrentText
                });
            }

            hunks.Add(hunk);
        }

        return hunks;
    }

    private static string BuildHunkRangeText(IReadOnlyList<FileCompareDiffItem> items, int start, int end)
    {
        var first = items[start].DisplayLocation;
        var last = items[end].DisplayLocation;
        if (!string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(last))
        {
            return string.Equals(first, last, StringComparison.Ordinal)
                ? first
                : $"{first} ~ {last}";
        }

        if (!string.IsNullOrWhiteSpace(first))
            return first;
        if (!string.IsNullOrWhiteSpace(last))
            return last;
        return $"第{start + 1}项 ~ 第{end + 1}项";
    }

    private readonly record struct WordCellKey(int RowIndex, int ColumnIndex);

    private sealed class ComparisonDocument : IAsyncDisposable
    {
        private const long MaxLegacyFileBytes = 50L * 1024 * 1024;
        private readonly Func<Stream> _openRead;
        private readonly TemporaryFileLease? _lease;

        private ComparisonDocument(Func<Stream> openRead, TemporaryFileLease? lease = null)
        {
            _openRead = openRead;
            _lease = lease;
        }

        public Stream OpenRead() => _openRead();

        public static async Task<ComparisonDocument> CreateAsync(
            WordFile file,
            IFileStorageService fileStorage,
            IFileCompareTemporaryStorage temporaryStorage,
            IResourceBudgetGovernor resourceBudgetGovernor,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(file.FilePath))
            {
                try
                {
                    using var stored = fileStorage.OpenReadStream(file.FilePath);
                    if (stored.CanSeek)
                        resourceBudgetGovernor.ValidateDocumentSize(stored.Length);
                    return new ComparisonDocument(() => fileStorage.OpenReadStream(file.FilePath));
                }
                catch (Exception exception) when (
                    exception is FileNotFoundException or DirectoryNotFoundException)
                {
                }
            }

            var content = file.FileContent ?? Array.Empty<byte>();
            resourceBudgetGovernor.ValidateDocumentSize(content.LongLength);
            using var source = new MemoryStream(content, writable: false);
            var lease = await temporaryStorage.StageUploadAsync(
                source,
                MaxLegacyFileBytes,
                cancellationToken);
            return new ComparisonDocument(lease.OpenRead, lease);
        }

        public ValueTask DisposeAsync() =>
            _lease?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
