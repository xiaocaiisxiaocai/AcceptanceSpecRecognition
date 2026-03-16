using AcceptanceSpecSystem.Core.Documents.Interfaces;
using AcceptanceSpecSystem.Core.Documents.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AcceptanceSpecSystem.Core.Documents.Writers;

/// <summary>
/// Word文档写入器实现
/// </summary>
public class WordDocumentWriter : IDocumentWriter
{
    /// <summary>
    /// 支持的文件扩展名
    /// </summary>
    private static readonly string[] SupportedExtensions = { ".docx" };

    /// <summary>
    /// 获取文档中的顶层表格。
    /// 需要与 WordDocumentParser 保持一致，避免解析/写入对表格索引的定义不同。
    /// </summary>
    private static List<Table> GetTopLevelTables(Body body)
    {
        return body
            .Descendants<Table>()
            .Where(t => !t.Ancestors<TableCell>().Any())
            .ToList();
    }

    /// <summary>
    /// 写入器支持的文档类型。
    /// </summary>
    public Models.DocumentType DocumentType => Models.DocumentType.Word;

    /// <summary>
    /// 判断是否可写入指定文件路径的文档。
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>是否可写入</returns>
    public bool CanWrite(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedExtensions.Contains(extension);
    }

    /// <summary>
    /// 对文件路径指定的文档执行批量单元格写入操作。
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="tableIndex">表格索引（从0开始）</param>
    /// <param name="operations">写入操作集合</param>
    /// <returns>成功写入的单元格数量</returns>
    public Task<int> WriteTableDataAsync(string filePath, int tableIndex, IEnumerable<CellWriteOperation> operations)
    {
        return Task.Run(() =>
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite);
            return WriteTableDataInternal(stream, tableIndex, operations);
        });
    }

    /// <summary>
    /// 对输入流指定的文档执行批量单元格写入操作。
    /// </summary>
    /// <param name="stream">输入流（可读写）</param>
    /// <param name="tableIndex">表格索引（从0开始）</param>
    /// <param name="operations">写入操作集合</param>
    /// <returns>成功写入的单元格数量</returns>
    public Task<int> WriteTableDataAsync(Stream stream, int tableIndex, IEnumerable<CellWriteOperation> operations)
    {
        return Task.Run(() => WriteTableDataInternal(stream, tableIndex, operations));
    }

    /// <summary>
    /// 写入单个单元格。
    /// </summary>
    /// <param name="stream">输入流（可读写）</param>
    /// <param name="tableIndex">表格索引（从0开始）</param>
    /// <param name="rowIndex">行索引（从0开始）</param>
    /// <param name="columnIndex">列索引（从0开始）</param>
    /// <param name="value">写入值</param>
    /// <returns>是否写入成功</returns>
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

    /// <summary>
    /// 复制源文件到目标路径，并在新文件上执行批量单元格写入操作。
    /// </summary>
    /// <param name="sourceFilePath">源文件路径</param>
    /// <param name="targetFilePath">目标文件路径</param>
    /// <param name="tableIndex">表格索引（从0开始）</param>
    /// <param name="operations">写入操作集合</param>
    /// <returns>成功写入的单元格数量</returns>
    public Task<int> WriteToNewFileAsync(string sourceFilePath, string targetFilePath, int tableIndex, IEnumerable<CellWriteOperation> operations)
    {
        return Task.Run(() =>
        {
            // 复制源文件到目标路径
            File.Copy(sourceFilePath, targetFilePath, overwrite: true);

            // 在新文件上执行写入操作
            using var stream = File.Open(targetFilePath, FileMode.Open, FileAccess.ReadWrite);
            return WriteTableDataInternal(stream, tableIndex, operations);
        });
    }

    /// <summary>
    /// 批量写入多个表格（一次 Open / Save）
    /// </summary>
    public Task<int> WriteMultipleTablesAsync(Stream stream, Dictionary<int, List<CellWriteOperation>> tableOperations)
    {
        return Task.Run(() => WriteMultipleTablesInternal(stream, tableOperations));
    }

    /// <summary>
    /// 多表写入内部实现
    /// </summary>
    private int WriteMultipleTablesInternal(Stream stream, Dictionary<int, List<CellWriteOperation>> tableOperations)
    {
        if (tableOperations == null || tableOperations.Count == 0)
            return 0;

        using var doc = WordprocessingDocument.Open(stream, true);
        var body = doc.MainDocumentPart?.Document.Body;
        if (body == null)
            throw new InvalidOperationException("文档为空或格式无效");

        var tables = GetTopLevelTables(body);
        int totalSuccess = 0;

        foreach (var (tableIndex, operations) in tableOperations)
        {
            if (operations == null || operations.Count == 0)
                continue;

            if (tableIndex < 0 || tableIndex >= tables.Count)
                throw new ArgumentOutOfRangeException(nameof(tableIndex),
                    $"表格索引 {tableIndex} 超出范围。文档共有 {tables.Count} 个表格。");

            var table = tables[tableIndex];
            var rows = table.Elements<TableRow>().ToList();
            var cellMap = BuildCellMap(rows);

            foreach (var operation in operations)
            {
                if (TryWriteCell(cellMap, rows, operation))
                {
                    totalSuccess++;
                }
            }
        }

        doc.MainDocumentPart?.Document.Save();
        return totalSuccess;
    }

    /// <summary>
    /// 内部写入实现
    /// </summary>
    private int WriteTableDataInternal(Stream stream, int tableIndex, IEnumerable<CellWriteOperation> operations)
    {
        var operationsList = operations.ToList();
        if (operationsList.Count == 0)
            return 0;

        using var doc = WordprocessingDocument.Open(stream, true);
        var body = doc.MainDocumentPart?.Document.Body;
        if (body == null)
            throw new InvalidOperationException("文档为空或格式无效");

        var tables = GetTopLevelTables(body);
        if (tableIndex < 0 || tableIndex >= tables.Count)
            throw new ArgumentOutOfRangeException(nameof(tableIndex), $"表格索引超出范围。文档共有 {tables.Count} 个表格。");

        var table = tables[tableIndex];
        var rows = table.Elements<TableRow>().ToList();

        // 构建单元格位置映射
        var cellMap = BuildCellMap(rows);

        int successCount = 0;

        foreach (var operation in operationsList)
        {
            if (TryWriteCell(cellMap, rows, operation))
            {
                successCount++;
            }
        }

        // 保存更改
        doc.MainDocumentPart?.Document.Save();

        return successCount;
    }

    /// <summary>
    /// 构建单元格位置映射
    /// </summary>
    private Dictionary<(int row, int col), TableCell> BuildCellMap(List<TableRow> rows)
    {
        var map = new Dictionary<(int row, int col), TableCell>();
        var verticalMergeStarts = new Dictionary<int, (TableCell cell, int startRow)>();

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
                    verticalMergeStarts[colIndex] = (cell, rowIndex);
                    for (int c = colIndex; c < colIndex + gridSpan; c++)
                    {
                        map[(rowIndex, c)] = cell;
                    }
                }
                else if (vMerge != null && verticalMergeStarts.ContainsKey(colIndex))
                {
                    // 继续垂直合并 - 指向起始单元格
                    var startCell = verticalMergeStarts[colIndex].cell;
                    for (int c = colIndex; c < colIndex + gridSpan; c++)
                    {
                        map[(rowIndex, c)] = startCell;
                    }
                }
                else
                {
                    // 普通单元格或水平合并
                    if (verticalMergeStarts.ContainsKey(colIndex))
                    {
                        verticalMergeStarts.Remove(colIndex);
                    }

                    for (int c = colIndex; c < colIndex + gridSpan; c++)
                    {
                        map[(rowIndex, c)] = cell;
                    }
                }

                colIndex += gridSpan;
            }
        }

        return map;
    }

    /// <summary>
    /// 尝试写入单个单元格
    /// </summary>
    private bool TryWriteCell(Dictionary<(int row, int col), TableCell> cellMap, List<TableRow> rows, CellWriteOperation operation)
    {
        if (!cellMap.TryGetValue((operation.RowIndex, operation.ColumnIndex), out var cell))
        {
            return false;
        }

        try
        {
            if (operation.PreserveFormatting)
            {
                // 保留格式，只修改文本内容
                SetCellTextPreserveFormat(cell, operation.Value);
            }
            else
            {
                // 直接设置文本
                SetCellText(cell, operation.Value);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 设置单元格文本（保留格式）
    /// </summary>
    private void SetCellTextPreserveFormat(TableCell cell, string value)
    {
        var paragraphs = cell.Elements<Paragraph>().ToList();

        if (paragraphs.Count == 0)
        {
            // 没有段落，创建新段落
            var paragraph = new Paragraph(new Run(new Text(value)));
            cell.AppendChild(paragraph);
            return;
        }

        // 处理多行文本
        var lines = value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        // 使用第一个段落
        var firstParagraph = paragraphs[0];
        var firstRun = firstParagraph.Elements<Run>().FirstOrDefault();
        RunProperties? runProps = null;

        if (firstRun != null)
        {
            // 保存原有格式
            runProps = firstRun.RunProperties?.CloneNode(true) as RunProperties;

            // 清除所有runs
            foreach (var run in firstParagraph.Elements<Run>().ToList())
            {
                run.Remove();
            }
        }

        // 设置第一行文本
        var newRun = new Run();
        if (runProps != null)
        {
            newRun.RunProperties = runProps.CloneNode(true) as RunProperties;
        }
        newRun.AppendChild(new Text(lines[0]) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve });
        firstParagraph.AppendChild(newRun);

        // 移除多余的段落
        for (int i = 1; i < paragraphs.Count; i++)
        {
            paragraphs[i].Remove();
        }

        // 添加额外的行（如果有多行文本）
        for (int i = 1; i < lines.Length; i++)
        {
            var newParagraph = firstParagraph.CloneNode(false) as Paragraph;
            if (newParagraph != null)
            {
                var lineRun = new Run();
                if (runProps != null)
                {
                    lineRun.RunProperties = runProps.CloneNode(true) as RunProperties;
                }
                lineRun.AppendChild(new Text(lines[i]) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve });
                newParagraph.AppendChild(lineRun);
                cell.AppendChild(newParagraph);
            }
        }
    }

    /// <summary>
    /// 设置单元格文本（不保留格式）
    /// </summary>
    private void SetCellText(TableCell cell, string value)
    {
        // 移除所有现有段落
        foreach (var paragraph in cell.Elements<Paragraph>().ToList())
        {
            paragraph.Remove();
        }

        // 处理多行文本
        var lines = value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        foreach (var line in lines)
        {
            var paragraph = new Paragraph(
                new Run(
                    new Text(line) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }
                )
            );
            cell.AppendChild(paragraph);
        }
    }
}
