using AcceptanceSpecSystem.Core.Documents.Interfaces;
using AcceptanceSpecSystem.Core.Documents.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AcceptanceSpecSystem.Core.Documents.Parsers;

/// <summary>
/// Word文档解析器实现
/// </summary>
public class WordDocumentParser : IDocumentParser
{
    /// <summary>
    /// 支持的文件扩展名
    /// </summary>
    private static readonly string[] SupportedExtensions = { ".docx" };

    /// <summary>
    /// 嵌套表格提取的最大深度（避免结构爆炸）
    /// </summary>
    private const int MaxNestedTableDepth = 2;

    /// <summary>
    /// 获取文档中的“顶层表格”（不在任何 TableCell 内的 Table）。
    /// 说明：Word 的 Table 允许嵌套；嵌套表格应作为父表格某个单元格的内容处理，而不应出现在表格列表里。
    /// </summary>
    private static List<Table> GetTopLevelTables(Body body)
    {
        return body
            .Descendants<Table>()
            .Where(t => !t.Ancestors<TableCell>().Any())
            .ToList();
    }

    /// <summary>
    /// 解析器支持的文档类型。
    /// </summary>
    public Models.DocumentType DocumentType => Models.DocumentType.Word;

    /// <summary>
    /// 判断是否可解析指定文件路径的文档。
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>是否可解析</returns>
    public bool CanParse(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedExtensions.Contains(extension);
    }

    /// <summary>
    /// 从文件路径读取并解析文档中的表格信息列表。
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>表格信息列表</returns>
    public Task<IReadOnlyList<TableInfo>> GetTablesAsync(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return GetTablesAsync(stream);
    }

    /// <summary>
    /// 从输入流解析文档中的表格信息列表。
    /// </summary>
    /// <param name="stream">输入流</param>
    /// <returns>表格信息列表</returns>
    public Task<IReadOnlyList<TableInfo>> GetTablesAsync(Stream stream)
    {
        return Task.Run(() =>
        {
            var tables = new List<TableInfo>();

            using var doc = WordprocessingDocument.Open(stream, false);
            var body = doc.MainDocumentPart?.Document.Body;
            if (body == null)
                return (IReadOnlyList<TableInfo>)tables;

            var allTables = GetTopLevelTables(body);

            for (int i = 0; i < allTables.Count; i++)
            {
                var table = allTables[i];
                var tableInfo = ExtractTableInfo(table, i, allTables);
                tables.Add(tableInfo);
            }

            return (IReadOnlyList<TableInfo>)tables;
        });
    }

    /// <summary>
    /// 从文件路径提取指定索引表格的数据。
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="tableIndex">表格索引（从0开始，顶层表格）</param>
    /// <param name="mapping">列映射（可选）</param>
    /// <returns>表格数据</returns>
    public Task<TableData> ExtractTableDataAsync(string filePath, int tableIndex, ColumnMapping? mapping = null)
    {
        using var stream = File.OpenRead(filePath);
        return ExtractTableDataAsync(stream, tableIndex, mapping);
    }

    /// <summary>
    /// 从输入流提取指定索引表格的数据。
    /// </summary>
    /// <param name="stream">输入流</param>
    /// <param name="tableIndex">表格索引（从0开始，顶层表格）</param>
    /// <param name="mapping">列映射（可选）</param>
    /// <returns>表格数据</returns>
    public Task<TableData> ExtractTableDataAsync(Stream stream, int tableIndex, ColumnMapping? mapping = null)
    {
        return Task.Run(() =>
        {
            using var doc = WordprocessingDocument.Open(stream, false);
            var body = doc.MainDocumentPart?.Document.Body;
            if (body == null)
                throw new InvalidOperationException("文档为空或格式无效");

            var tables = GetTopLevelTables(body);
            if (tableIndex < 0 || tableIndex >= tables.Count)
                throw new ArgumentOutOfRangeException(nameof(tableIndex), $"表格索引超出范围。文档共有 {tables.Count} 个表格。");

            var table = tables[tableIndex];
            return ExtractTableData(table, tableIndex, mapping);
        });
    }

    /// <summary>
    /// 提取文档中所有顶层表格的数据。
    /// </summary>
    /// <param name="stream">输入流</param>
    /// <returns>表格数据列表</returns>
    public Task<IReadOnlyList<TableData>> ExtractAllTablesDataAsync(Stream stream)
    {
        return Task.Run(() =>
        {
            var result = new List<TableData>();

            using var doc = WordprocessingDocument.Open(stream, false);
            var body = doc.MainDocumentPart?.Document.Body;
            if (body == null)
                return (IReadOnlyList<TableData>)result;

            var tables = GetTopLevelTables(body);

            for (int i = 0; i < tables.Count; i++)
            {
                var tableData = ExtractTableData(tables[i], i, null);
                result.Add(tableData);
            }

            return (IReadOnlyList<TableData>)result;
        });
    }

    /// <summary>
    /// 提取表格基本信息
    /// </summary>
    private TableInfo ExtractTableInfo(Table table, int index, List<Table> allTables)
    {
        var rows = table.Elements<TableRow>().ToList();
        var maxColumns = 0;
        var hasMergedCells = false;
        var previewText = string.Empty;
        var headers = new List<string>();

        // 计算最大列数并检查合并单元格
        foreach (var row in rows)
        {
            var cells = row.Elements<TableCell>().ToList();
            var colCount = 0;

            foreach (var cell in cells)
            {
                var gridSpan = cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1;
                colCount += gridSpan;

                // 检查垂直合并
                var vMerge = cell.TableCellProperties?.VerticalMerge;
                if (vMerge != null || gridSpan > 1)
                {
                    hasMergedCells = true;
                }
            }

            maxColumns = Math.Max(maxColumns, colCount);
        }

        // 提取第一行作为预览和表头（仅用于展示：做轻量的空白清洗，避免包含控制字符影响JSON/前端展示）
        if (rows.Count > 0)
        {
            var firstRowCells = rows[0].Elements<TableCell>().ToList();
            var cellTexts = new List<string>();

            foreach (var cell in firstRowCells)
            {
                var text = NormalizeDisplayText(GetCellText(cell));
                cellTexts.Add(text);
                headers.Add(text);
            }

            previewText = string.Join(" | ", cellTexts.Take(5));
            if (cellTexts.Count > 5)
                previewText += " ...";
        }

        // 检查是否为嵌套表格
        var isNested = false;
        int? parentIndex = null;

        var parent = table.Parent;
        while (parent != null)
        {
            if (parent is TableCell parentCell)
            {
                var parentTable = parentCell.Ancestors<Table>().FirstOrDefault();
                if (parentTable != null)
                {
                    isNested = true;
                    parentIndex = allTables.IndexOf(parentTable);
                    break;
                }
            }
            parent = parent.Parent;
        }

        return new TableInfo
        {
            Index = index,
            RowCount = rows.Count,
            ColumnCount = maxColumns,
            IsNested = isNested,
            ParentTableIndex = parentIndex,
            PreviewText = previewText,
            Headers = headers,
            HasMergedCells = hasMergedCells
        };
    }

    /// <summary>
    /// 用于展示的文本清洗：把换行/制表等控制字符压缩为空格，避免影响JSON解析与前端展示
    /// </summary>
    private static string NormalizeDisplayText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // 替换常见控制字符为空格
        var cleaned = text
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\t", " ");

        // 压缩多空白
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, "\\s+", " ");
        return cleaned.Trim();
    }

    /// <summary>
    /// 提取表格数据
    /// </summary>
    private TableData ExtractTableData(Table table, int tableIndex, ColumnMapping? mapping)
    {
        var tableData = new TableData { TableIndex = tableIndex };
        var rows = table.Elements<TableRow>().ToList();

        if (rows.Count == 0)
            return tableData;

        // 构建合并单元格映射
        var mergedCellsMap = BuildMergedCellsMap(rows, out var mergeStartValues, out var mergeStartStructuredValues);
        tableData.MergedCells = mergedCellsMap.Values
            .Where(m => m.RowSpan > 1 || m.ColSpan > 1)
            .Distinct()
            .ToList();

        // 确定表头行和数据起始行
        var headerRowIndex = mapping?.HeaderRowIndex ?? 0;
        var dataStartRowIndex = mapping?.DataStartRowIndex ?? 1;

        // 提取表头
        if (headerRowIndex < rows.Count)
        {
            var headerRow = rows[headerRowIndex];
            var headerCells = headerRow.Elements<TableCell>().ToList();
            int colIndex = 0;

            foreach (var cell in headerCells)
            {
                var text = GetCellText(cell);
                tableData.Headers.Add(text);

                var gridSpan = cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1;
                // 为水平合并的表头添加占位（保持列数一致）
                for (int i = 1; i < gridSpan; i++)
                {
                    tableData.Headers.Add(text);
                }
                colIndex += gridSpan;
            }
        }

        // 提取数据行
        for (int rowIndex = dataStartRowIndex; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var rowData = ExtractRowData(row, rowIndex, mergedCellsMap, mergeStartValues, mergeStartStructuredValues);
            tableData.Rows.Add(rowData);
        }

        return tableData;
    }

    /// <summary>
    /// 提取单行数据
    /// </summary>
    private RowData ExtractRowData(
        TableRow row,
        int rowIndex,
        Dictionary<(int, int), MergedCellInfo> mergedCellsMap,
        Dictionary<(int, int), string> mergeStartValues,
        Dictionary<(int, int), StructuredCellValue> mergeStartStructuredValues)
    {
        var rowData = new RowData { Index = rowIndex };
        var cells = row.Elements<TableCell>().ToList();
        int colIndex = 0;

        foreach (var cell in cells)
        {
            var text = GetCellText(cell);
            var structured = GetCellStructuredValue(cell, MaxNestedTableDepth);
            var gridSpan = cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1;

            // 检查垂直合并
            var vMerge = cell.TableCellProperties?.VerticalMerge;
            var isMergeStart = vMerge?.Val?.Value == MergedCellValues.Restart;
            var isMergedVertically = vMerge != null && !isMergeStart;

            // 如果是垂直合并的后续单元格，填充为起始单元格的值（用户期望“拆到每一行都有值”）
            if (isMergedVertically && mergedCellsMap.TryGetValue((rowIndex, colIndex), out var mergeInfo))
            {
                if (mergeInfo.StartRow < rowIndex)
                {
                    // 优先按列索引取值（兼容跨列合并场景）
                    if (mergeStartValues.TryGetValue((mergeInfo.StartRow, colIndex), out var v))
                    {
                        text = v;
                    }
                    else if (mergeStartValues.TryGetValue((mergeInfo.StartRow, mergeInfo.StartColumn), out var v2))
                    {
                        text = v2;
                    }

                    // 同步结构化值（若不存在则保持当前解析结果）
                    if (mergeStartStructuredValues.TryGetValue((mergeInfo.StartRow, colIndex), out var sv))
                    {
                        structured = sv;
                    }
                    else if (mergeStartStructuredValues.TryGetValue((mergeInfo.StartRow, mergeInfo.StartColumn), out var sv2))
                    {
                        structured = sv2;
                    }
                }
            }

            var cellData = new CellData
            {
                Value = text,
                StructuredValue = structured,
                RowIndex = rowIndex,
                ColumnIndex = colIndex,
                IsMerged = gridSpan > 1 || vMerge != null,
                IsMergeStart = gridSpan > 1 || isMergeStart,
                ColSpan = gridSpan,
                RowSpan = 1 // 会在后续处理中更新
            };

            rowData.Cells.Add(cellData);

            // 为水平合并的单元格添加占位数据
            for (int i = 1; i < gridSpan; i++)
            {
                rowData.Cells.Add(new CellData
                {
                    Value = text, // 填充相同内容（按设计要求）
                    StructuredValue = structured,
                    RowIndex = rowIndex,
                    ColumnIndex = colIndex + i,
                    IsMerged = true,
                    IsMergeStart = false,
                    ColSpan = 1,
                    RowSpan = 1
                });
            }

            colIndex += gridSpan;
        }

        return rowData;
    }

    /// <summary>
    /// 构建合并单元格映射
    /// </summary>
    private Dictionary<(int, int), MergedCellInfo> BuildMergedCellsMap(
        List<TableRow> rows,
        out Dictionary<(int, int), string> mergeStartValues,
        out Dictionary<(int, int), StructuredCellValue> mergeStartStructuredValues)
    {
        var map = new Dictionary<(int, int), MergedCellInfo>();
        var verticalMergeStarts = new Dictionary<int, (int startRow, string value)>();
        mergeStartValues = new Dictionary<(int, int), string>();
        mergeStartStructuredValues = new Dictionary<(int, int), StructuredCellValue>();

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var cells = row.Elements<TableCell>().ToList();
            int colIndex = 0;

            foreach (var cell in cells)
            {
                var gridSpan = cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1;
                var vMerge = cell.TableCellProperties?.VerticalMerge;

                // 处理垂直合并
                if (vMerge?.Val?.Value == MergedCellValues.Restart)
                {
                    // 开始新的垂直合并
                    var startValue = GetCellText(cell);
                    var startStructured = GetCellStructuredValue(cell, MaxNestedTableDepth);
                    verticalMergeStarts[colIndex] = (rowIndex, startValue);
                    // 记录起始值（按 span 展开到每一列，方便后续填充）
                    for (int c = colIndex; c < colIndex + gridSpan; c++)
                    {
                        mergeStartValues[(rowIndex, c)] = startValue;
                        mergeStartStructuredValues[(rowIndex, c)] = startStructured;
                    }
                }
                else if (vMerge != null && verticalMergeStarts.ContainsKey(colIndex))
                {
                    // 继续垂直合并
                    var startInfo = verticalMergeStarts[colIndex];
                    var mergeInfo = new MergedCellInfo
                    {
                        StartRow = startInfo.startRow,
                        StartColumn = colIndex,
                        EndRow = rowIndex,
                        EndColumn = colIndex + gridSpan - 1
                    };

                    // 更新所有相关单元格的映射
                    for (int r = startInfo.startRow; r <= rowIndex; r++)
                    {
                        for (int c = colIndex; c < colIndex + gridSpan; c++)
                        {
                            map[(r, c)] = mergeInfo;
                        }
                    }
                }
                else if (vMerge == null && verticalMergeStarts.ContainsKey(colIndex))
                {
                    // 垂直合并结束
                    verticalMergeStarts.Remove(colIndex);
                }

                // 处理水平合并
                if (gridSpan > 1)
                {
                    var mergeInfo = new MergedCellInfo
                    {
                        StartRow = rowIndex,
                        StartColumn = colIndex,
                        EndRow = rowIndex,
                        EndColumn = colIndex + gridSpan - 1
                    };

                    for (int c = colIndex; c < colIndex + gridSpan; c++)
                    {
                        if (!map.ContainsKey((rowIndex, c)))
                        {
                            map[(rowIndex, c)] = mergeInfo;
                        }
                    }
                }

                colIndex += gridSpan;
            }
        }

        return map;
    }

    /// <summary>
    /// 获取单元格结构化内容（文本片段 + 嵌套表格片段），用于前端 JSON 预览/后续对比。
    /// </summary>
    private static StructuredCellValue GetCellStructuredValue(TableCell cell, int depthRemaining)
    {
        var result = new StructuredCellValue();

        // 1) 段落文字：保留换行（前端可 pre-wrap 展示），并尽量保留段落前的序号（Word 编号列表）
        var paragraphTexts = GetCellParagraphLinesWithNumbering(cell, includeIndentation: true);

        if (paragraphTexts.Count > 0)
        {
            result.Parts.Add(new StructuredCellPart
            {
                Type = "text",
                Text = string.Join("\n", paragraphTexts)
            });
        }

        // 2) 嵌套表格：递归提取（深度限制），避免爆炸
        foreach (var nested in cell.Elements<Table>())
        {
            if (depthRemaining > 0)
            {
                result.Parts.Add(new StructuredCellPart
                {
                    Type = "table",
                    Table = ExtractStructuredTableValue(nested, depthRemaining - 1)
                });
            }
            else
            {
                // 达到深度限制：降级为纯文本
                result.Parts.Add(new StructuredCellPart
                {
                    Type = "text",
                    Text = SerializeTableToText(nested)
                });
            }
        }

        return result;
    }

    private static StructuredTableValue ExtractStructuredTableValue(Table table, int depthRemaining)
    {
        var rows = table.Elements<TableRow>().ToList();
        var maxColumns = 0;
        foreach (var r in rows)
        {
            var colCount = 0;
            foreach (var c in r.Elements<TableCell>())
            {
                colCount += c.TableCellProperties?.GridSpan?.Val?.Value ?? 1;
            }
            maxColumns = Math.Max(maxColumns, colCount);
        }

        var result = new StructuredTableValue
        {
            RowCount = rows.Count,
            ColumnCount = maxColumns
        };

        foreach (var row in rows)
        {
            var rowCells = new List<StructuredCellValue>();
            var cells = row.Elements<TableCell>().ToList();
            foreach (var cell in cells)
            {
                var gridSpan = cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1;
                var cellValue = GetCellStructuredValue(cell, depthRemaining);
                rowCells.Add(cellValue);
                for (int i = 1; i < gridSpan; i++)
                {
                    rowCells.Add(cellValue);
                }
            }

            // 补齐到最大列数（避免前端渲染/对比时出现不规则数组）
            while (rowCells.Count < maxColumns)
            {
                rowCells.Add(new StructuredCellValue());
            }

            result.Rows.Add(rowCells);
        }

        return result;
    }

    /// <summary>
    /// 获取单元格文本内容
    /// </summary>
    private static string GetCellText(TableCell cell)
    {
        var texts = new List<string>(GetCellParagraphLinesWithNumbering(cell, includeIndentation: false));

        // 2) 嵌套表格（Table in TableCell）：序列化为多行文本，便于前端在预览中展示
        // 注意：只处理单元格内的嵌套表格，不包含外层表格本身
        foreach (var nested in cell.Elements<Table>())
        {
            texts.Add(SerializeTableToText(nested));
        }

        return string.Join("\n", texts).Trim();
    }

    private static string SerializeTableToText(Table table)
    {
        var lines = new List<string>();
        var rows = table.Elements<TableRow>().ToList();
        foreach (var row in rows)
        {
            var cells = row.Elements<TableCell>().ToList();
            var parts = new List<string>();
            foreach (var cell in cells)
            {
                // 递归读取嵌套表格的文字会导致指数膨胀，这里只取该单元格的段落文字（含编号前缀）
                var cellText = string.Join(" ", GetCellParagraphLinesWithNumbering(cell, includeIndentation: false)).Trim();
                parts.Add(cellText);
            }
            if (parts.Count > 0)
            {
                lines.Add(string.Join(" | ", parts));
            }
        }
        return string.Join("\n", lines).Trim();
    }

    /// <summary>
    /// 读取单元格中每个段落的文本，并尽量保留 Word 编号列表的“段落前序号”（1、2、3…）。
    /// 说明：Word 的编号不在 Run 文本里，而在 ParagraphProperties.NumberingProperties 里。
    /// 这里按“每个单元格内、每个 numId 单独计数”的方式生成序号，满足预览/导入的可读性需求。
    /// </summary>
    private static List<string> GetCellParagraphLinesWithNumbering(TableCell cell, bool includeIndentation)
    {
        var lines = new List<string>();

        // 先收集段落，判断“是否需要保留序号”
        var items = new List<(string Text, bool HasNumbering, int Level)>();
        foreach (var paragraph in cell.Elements<Paragraph>())
        {
            var text = GetParagraphPlainText(paragraph);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            text = text.Trim();
            var hasNum = TryGetParagraphNumbering(paragraph, out _, out var level);

            // 统一把段落开头的“①② / (1) / 1、 / 1. ”规范成 “1、” 这种格式（仅处理明确的列表前缀）
            // 注意：必须避免把 “2.4” 这类小数误判为编号前缀
            if (hasNum || HasExplicitLeadingListPrefix(text))
            {
                text = NormalizeLeadingListPrefix(text);
            }
            items.Add((text, hasNum, level));
        }

        // 规则：只有当同一个单元格里出现 2 条及以上“编号段落”时，才补上序号；否则不补（避免单条内容也显示 1、 的噪音）
        var shouldPrefix = items.Count(i => i.HasNumbering) >= 2;
        var seq = 0;

        foreach (var it in items)
        {
            var lineText = it.Text;
            if (shouldPrefix && it.HasNumbering)
            {
                seq++;
                // 多条列表时：统一用系统生成的 1、2、…；若段落文本里本身带了前缀则去掉，避免重复
                var bodyText = StripLeadingListPrefix(it.Text).Trim();
                lineText = $"{seq}、{bodyText}";
            }

            if (includeIndentation && it.HasNumbering && it.Level > 0)
            {
                lineText = new string(' ', it.Level * 2) + lineText;
            }

            lines.Add(lineText);
        }

        return lines;
    }

    /// <summary>
    /// 把段落开头的“圈号/括号数字/点号编号/顿号编号”等统一成 “1、xxx” 的格式（仅处理开头前缀）。
    /// 例如：①xxx -> 1、xxx； 2. xxx -> 2、xxx；（3）xxx -> 3、xxx
    /// </summary>
    private static string NormalizeLeadingListPrefix(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // ①..⑳
        var first = text[0];
        if (first is >= '\u2460' and <= '\u2473')
        {
            var n = first - '\u2460' + 1;
            var rest = text.Substring(1).TrimStart();
            rest = System.Text.RegularExpressions.Regex.Replace(rest, @"^[\)\）\.\、\s]+", "");
            return $"{n}、{rest}".TrimEnd();
        }

        // ⑴..⒇（带括号的数字）
        if (first is >= '\u2474' and <= '\u2487')
        {
            var n = first - '\u2474' + 1;
            var rest = text.Substring(1).TrimStart();
            rest = System.Text.RegularExpressions.Regex.Replace(rest, @"^[\)\）\.\、\s]+", "");
            return $"{n}、{rest}".TrimEnd();
        }

        // 常见：1、xxx / 1. xxx / 1) xxx / (1) xxx / （1）xxx -> 1、xxx
        // 关键：要求“分隔符后面有空白”或“括号后面有空白”，避免把 2.4 这种小数当成编号
        var m = System.Text.RegularExpressions.Regex.Match(
            text,
            @"^\s*(?:[\(\（]\s*)?(?<n>\d+)\s*(?:[\)\）]\s*)?(?:(?<sep>[、\.])\s+|\)\s+|）\s+)(?<rest>[\s\S]+)$"
        );
        if (m.Success)
        {
            var n = m.Groups["n"].Value;
            var rest = m.Groups["rest"].Value.TrimStart();
            // 额外保护：如果是 “2.4xxx” 且没有空白，本规则不会命中；但为保险，rest 以数字开头且原文是 “n.<digit>” 也不处理
            return $"{n}、{rest}".TrimEnd();
        }

        return text.TrimEnd();
    }

    /// <summary>
    /// 移除段落开头的编号前缀（用于“多条列表时由系统统一重新编号”，避免出现 1、1、xxx）。
    /// </summary>
    private static string StripLeadingListPrefix(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // 先把①②这种转成数字前缀，便于统一剥离
        text = NormalizeLeadingListPrefix(text.TrimStart());

        // 去掉开头的 “1、” （仅当明确是列表前缀时才剥离）
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^\s*\d+、\s*"))
        {
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*\d+、\s*", "");
        }

        return text;
    }

    private static bool HasLeadingNumberPrefix(string text)
    {
        // 已经包含“1、 / 1. / 1) / （1）”等前缀的，就不再重复添加
        // 说明：这里不追求覆盖所有编号格式，覆盖常见格式即可
        return System.Text.RegularExpressions.Regex.IsMatch(
            text,
            @"^\s*[\(\（]?\d+[\)\）\.\、]\s*"
        );
    }

    /// <summary>
    /// 判断文本是否有“明确的列表前缀”（避免把 2.4 这类小数误判）。
    /// </summary>
    private static bool HasExplicitLeadingListPrefix(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // ①..⑳ / ⑴..⒇
        var first = text.TrimStart()[0];
        if ((first is >= '\u2460' and <= '\u2473') || (first is >= '\u2474' and <= '\u2487'))
            return true;

        // (1) xxx / （1）xxx / 1、xxx / 1. xxx / 1) xxx —— 必须有空白分隔
        return System.Text.RegularExpressions.Regex.IsMatch(
            text,
            @"^\s*(?:[\(\（]\s*)?\d+\s*(?:[\)\）]\s*)?(?:[、\.]\s+|\)\s+|）\s+)\S+"
        );
    }

    private static string GetParagraphPlainText(Paragraph paragraph)
    {
        // paragraph.Elements<Run>() 会漏掉 Hyperlink 等容器里的 Run，这里用 Descendants 更稳
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
}
