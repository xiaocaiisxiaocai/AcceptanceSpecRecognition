using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using CoreDocumentType = AcceptanceSpecSystem.Core.Documents.Models.DocumentType;

namespace AcceptanceSpecSystem.Api.Services;

public interface IFileCompareService
{
    Task<FileCompareResult> CompareAsync(WordFile fileA, WordFile fileB, CancellationToken cancellationToken = default);
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
    private readonly DocumentServiceFactory _documentServiceFactory;
    private readonly IFileStorageService _fileStorage;

    public FileCompareService(DocumentServiceFactory documentServiceFactory, IFileStorageService fileStorage)
    {
        _documentServiceFactory = documentServiceFactory;
        _fileStorage = fileStorage;
    }

    public async Task<FileCompareResult> CompareAsync(WordFile fileA, WordFile fileB, CancellationToken cancellationToken = default)
    {
        if (fileA.FileType != fileB.FileType)
            throw new InvalidOperationException("仅支持同类型文件对比");

        return fileA.FileType switch
        {
            UploadedFileType.WordDocx => await CompareWordAsync(fileA, fileB, cancellationToken),
            UploadedFileType.ExcelXlsx => await CompareExcelAsync(fileA, fileB, cancellationToken),
            _ => throw new InvalidOperationException("不支持的文件类型")
        };
    }

    private async Task<FileCompareResult> CompareWordAsync(WordFile fileA, WordFile fileB, CancellationToken cancellationToken)
    {
        var bytesA = await ReadFileBytesAsync(fileA, cancellationToken);
        var bytesB = await ReadFileBytesAsync(fileB, cancellationToken);

        var paragraphsA = ExtractWordParagraphs(bytesA);
        var paragraphsB = ExtractWordParagraphs(bytesB);
        var ops = BuildParagraphDiff(paragraphsA, paragraphsB);
        var items = BuildWordDiffItems(ops);

        return new FileCompareResult
        {
            FileType = UploadedFileType.WordDocx,
            Items = items,
            Hunks = BuildDiffHunks(items)
        };
    }

    private async Task<FileCompareResult> CompareExcelAsync(WordFile fileA, WordFile fileB, CancellationToken cancellationToken)
    {
        var parser = _documentServiceFactory.GetParser(CoreDocumentType.Excel);
        if (parser == null)
            throw new InvalidOperationException("文档解析器不可用");

        var bytesA = await ReadFileBytesAsync(fileA, cancellationToken);
        var bytesB = await ReadFileBytesAsync(fileB, cancellationToken);

        var sheetsA = await parser.GetTablesAsync(new MemoryStream(bytesA));
        var sheetsB = await parser.GetTablesAsync(new MemoryStream(bytesB));
        var max = Math.Max(sheetsA.Count, sheetsB.Count);

        var mapping = new ColumnMapping
        {
            HeaderRowIndex = 0,
            HeaderRowCount = 1,
            DataStartRowIndex = 0
        };

        var items = new List<FileCompareDiffItem>();

        for (var sheetIndex = 0; sheetIndex < max; sheetIndex++)
        {
            TableInfo? infoA = sheetIndex < sheetsA.Count ? sheetsA[sheetIndex] : null;
            TableInfo? infoB = sheetIndex < sheetsB.Count ? sheetsB[sheetIndex] : null;

            TableData? tableA = null;
            TableData? tableB = null;

            if (infoA != null)
                tableA = await parser.ExtractTableDataAsync(new MemoryStream(bytesA), sheetIndex, mapping);
            if (infoB != null)
                tableB = await parser.ExtractTableDataAsync(new MemoryStream(bytesB), sheetIndex, mapping);

            var mapA = BuildExcelCellMap(tableA, infoA);
            var mapB = BuildExcelCellMap(tableB, infoB);

            foreach (var key in GetUnionKeys(mapA, mapB))
            {
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
            Hunks = BuildDiffHunks(items)
        };
    }

    private async Task<byte[]> ReadFileBytesAsync(WordFile file, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(file.FilePath))
        {
            var fullPath = _fileStorage.GetAbsolutePath(file.FilePath);
            if (File.Exists(fullPath))
            {
                return await File.ReadAllBytesAsync(fullPath, cancellationToken);
            }
        }

        return file.FileContent ?? Array.Empty<byte>();
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

    private static List<string> ExtractWordParagraphs(byte[] bytes)
    {
        var list = new List<string>();
        using var stream = new MemoryStream(bytes);
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null)
            return list;

        var counters = new Dictionary<(int NumId, int Level), int>();

        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            var text = GetParagraphPlainText(paragraph).Trim();
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

    private static List<DiffOp> BuildParagraphDiff(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var n = a.Count;
        var m = b.Count;

        // 大文档走分块近似算法，避免 O(n*m) 动态规划造成高延迟与高内存占用
        if ((long)n * m > MaxLcsMatrixCells)
        {
            return BuildParagraphDiffByChunk(a, b, ChunkLookAhead);
        }

        var dp = new int[n + 1, m + 1];

        for (var i = 1; i <= n; i++)
        {
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
            if (a[x - 1] == b[y - 1])
            {
                ops.Add(new DiffOp(DiffOpType.Equal, a[x - 1], x - 1, y - 1));
                x--;
                y--;
            }
            else if (dp[x - 1, y] >= dp[x, y - 1])
            {
                ops.Add(new DiffOp(DiffOpType.Remove, a[x - 1], x - 1, -1));
                x--;
            }
            else
            {
                ops.Add(new DiffOp(DiffOpType.Add, b[y - 1], -1, y - 1));
                y--;
            }
        }

        while (x > 0)
        {
            ops.Add(new DiffOp(DiffOpType.Remove, a[x - 1], x - 1, -1));
            x--;
        }

        while (y > 0)
        {
            ops.Add(new DiffOp(DiffOpType.Add, b[y - 1], -1, y - 1));
            y--;
        }

        ops.Reverse();
        return ops;
    }

    private static List<DiffOp> BuildParagraphDiffByChunk(
        IReadOnlyList<string> a,
        IReadOnlyList<string> b,
        int lookAhead)
    {
        var ops = new List<DiffOp>();
        var i = 0;
        var j = 0;

        while (i < a.Count && j < b.Count)
        {
            if (a[i] == b[j])
            {
                ops.Add(new DiffOp(DiffOpType.Equal, a[i], i, j));
                i++;
                j++;
                continue;
            }

            var matchInB = FindMatchIndex(b, j + 1, lookAhead, a[i]);
            var matchInA = FindMatchIndex(a, i + 1, lookAhead, b[j]);

            if (matchInB >= 0 && (matchInA < 0 || matchInB - j <= matchInA - i))
            {
                while (j < matchInB)
                {
                    ops.Add(new DiffOp(DiffOpType.Add, b[j], -1, j));
                    j++;
                }
                continue;
            }

            if (matchInA >= 0)
            {
                while (i < matchInA)
                {
                    ops.Add(new DiffOp(DiffOpType.Remove, a[i], i, -1));
                    i++;
                }
                continue;
            }

            // 无近邻锚点：按同位置差异处理，后续会组合为 Modified
            ops.Add(new DiffOp(DiffOpType.Remove, a[i], i, -1));
            ops.Add(new DiffOp(DiffOpType.Add, b[j], -1, j));
            i++;
            j++;
        }

        while (i < a.Count)
        {
            ops.Add(new DiffOp(DiffOpType.Remove, a[i], i, -1));
            i++;
        }

        while (j < b.Count)
        {
            ops.Add(new DiffOp(DiffOpType.Add, b[j], -1, j));
            j++;
        }

        return ops;
    }

    private static int FindMatchIndex(IReadOnlyList<string> source, int start, int lookAhead, string expected)
    {
        if (start >= source.Count)
            return -1;

        var end = Math.Min(source.Count - 1, start + lookAhead);
        for (var k = start; k <= end; k++)
        {
            if (source[k] == expected)
                return k;
        }

        return -1;
    }

    private static List<FileCompareDiffItem> BuildWordDiffItems(IReadOnlyList<DiffOp> ops)
    {
        var items = new List<FileCompareDiffItem>();
        for (var i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            if (op.Type == DiffOpType.Remove && i + 1 < ops.Count && ops[i + 1].Type == DiffOpType.Add)
            {
                var add = ops[i + 1];
                var locIndex = op.IndexA >= 0 ? op.IndexA : add.IndexB;
                items.Add(new FileCompareDiffItem
                {
                    DiffType = FileCompareDiffType.Modified,
                    OriginalText = op.Text,
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

    private static string GetParagraphPlainText(Paragraph paragraph)
    {
        return string.Join("", paragraph.Descendants<Run>().Select(r => r.InnerText));
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

    private static Dictionary<WordCellKey, string> BuildExcelCellMap(TableData? tableData, TableInfo? info)
    {
        var map = new Dictionary<WordCellKey, string>();
        if (tableData == null || info == null)
            return map;

        var startRow = info.UsedRangeStartRow;
        var startCol = info.UsedRangeStartColumn;

        foreach (var row in tableData.Rows)
        {
            foreach (var cell in row.Cells)
            {
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
        Dictionary<WordCellKey, string> mapB)
    {
        var set = new HashSet<WordCellKey>(mapA.Keys);
        set.UnionWith(mapB.Keys);
        return set.OrderBy(k => k.RowIndex).ThenBy(k => k.ColumnIndex);
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

    private static List<FileCompareHunk> BuildDiffHunks(IReadOnlyList<FileCompareDiffItem> items, int contextLineCount = 2)
    {
        var changedIndices = items
            .Select((item, index) => new { item, index })
            .Where(x => x.item.DiffType != FileCompareDiffType.Unchanged)
            .Select(x => x.index)
            .ToList();

        if (changedIndices.Count == 0)
            return new List<FileCompareHunk>();

        var ranges = new List<(int Start, int End)>();
        foreach (var index in changedIndices)
        {
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
            var hunk = new FileCompareHunk
            {
                StartItemIndex = start + 1,
                EndItemIndex = end + 1,
                RangeText = BuildHunkRangeText(items, start, end)
            };

            for (var i = start; i <= end; i++)
            {
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
}
