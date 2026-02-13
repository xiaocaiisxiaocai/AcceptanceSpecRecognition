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
}

public class FileCompareDiffItem
{
    public FileCompareDiffType DiffType { get; set; }
    public FileCompareLocation Location { get; set; } = new();
    public string? OriginalText { get; set; }
    public string? CurrentText { get; set; }
    public string? DisplayLocation { get; set; }
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
            Items = items
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
            Items = items
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

    private readonly record struct WordCellKey(int RowIndex, int ColumnIndex);
}
