using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AcceptanceSpecSystem.Core.Tests.Helpers;

/// <summary>
/// 测试Word文档生成帮助类
/// </summary>
public static class TestWordDocumentHelper
{
    /// <summary>
    /// 创建包含简单表格的Word文档
    /// </summary>
    public static MemoryStream CreateSimpleTableDocument(string[,] data)
    {
        var stream = new MemoryStream();

        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());

            var table = CreateTable(data);
            mainPart.Document.Body!.Append(table);
            mainPart.Document.Save();
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// 创建包含多个表格的Word文档
    /// </summary>
    public static MemoryStream CreateMultiTableDocument(params string[][,] tables)
    {
        var stream = new MemoryStream();

        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());

            foreach (var tableData in tables)
            {
                var table = CreateTable(tableData);
                mainPart.Document.Body!.Append(table);
                // 添加段落分隔
                mainPart.Document.Body!.Append(new Paragraph());
            }

            mainPart.Document.Save();
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// 创建包含水平合并单元格的表格文档
    /// </summary>
    public static MemoryStream CreateHorizontalMergedTableDocument()
    {
        var stream = new MemoryStream();

        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());

            var table = new Table();

            // 第一行：4个独立单元格
            var row1 = new TableRow();
            row1.Append(CreateCell("A1"));
            row1.Append(CreateCell("B1"));
            row1.Append(CreateCell("C1"));
            row1.Append(CreateCell("D1"));
            table.Append(row1);

            // 第二行：2个单元格，第一个跨2列
            var row2 = new TableRow();
            row2.Append(CreateMergedCell("A2-B2", 2));
            row2.Append(CreateCell("C2"));
            row2.Append(CreateCell("D2"));
            table.Append(row2);

            // 第三行：4个独立单元格
            var row3 = new TableRow();
            row3.Append(CreateCell("A3"));
            row3.Append(CreateCell("B3"));
            row3.Append(CreateCell("C3"));
            row3.Append(CreateCell("D3"));
            table.Append(row3);

            mainPart.Document.Body!.Append(table);
            mainPart.Document.Save();
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// 创建包含垂直合并单元格的表格文档
    /// </summary>
    public static MemoryStream CreateVerticalMergedTableDocument()
    {
        var stream = new MemoryStream();

        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());

            var table = new Table();

            // 第一行：第一个单元格开始垂直合并
            var row1 = new TableRow();
            row1.Append(CreateVerticalMergeStartCell("A1-A2"));
            row1.Append(CreateCell("B1"));
            row1.Append(CreateCell("C1"));
            table.Append(row1);

            // 第二行：第一个单元格继续垂直合并
            var row2 = new TableRow();
            row2.Append(CreateVerticalMergeContinueCell());
            row2.Append(CreateCell("B2"));
            row2.Append(CreateCell("C2"));
            table.Append(row2);

            // 第三行：独立单元格
            var row3 = new TableRow();
            row3.Append(CreateCell("A3"));
            row3.Append(CreateCell("B3"));
            row3.Append(CreateCell("C3"));
            table.Append(row3);

            mainPart.Document.Body!.Append(table);
            mainPart.Document.Save();
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// 创建表格
    /// </summary>
    private static Table CreateTable(string[,] data)
    {
        var table = new Table();
        var rows = data.GetLength(0);
        var cols = data.GetLength(1);

        for (int r = 0; r < rows; r++)
        {
            var row = new TableRow();
            for (int c = 0; c < cols; c++)
            {
                row.Append(CreateCell(data[r, c]));
            }
            table.Append(row);
        }

        return table;
    }

    /// <summary>
    /// 创建普通单元格
    /// </summary>
    private static TableCell CreateCell(string text)
    {
        var cell = new TableCell();
        cell.Append(new Paragraph(new Run(new Text(text))));
        return cell;
    }

    /// <summary>
    /// 创建水平合并单元格
    /// </summary>
    private static TableCell CreateMergedCell(string text, int gridSpan)
    {
        var cell = new TableCell();
        var cellProperties = new TableCellProperties
        {
            GridSpan = new GridSpan { Val = gridSpan }
        };
        cell.Append(cellProperties);
        cell.Append(new Paragraph(new Run(new Text(text))));
        return cell;
    }

    /// <summary>
    /// 创建垂直合并起始单元格
    /// </summary>
    private static TableCell CreateVerticalMergeStartCell(string text)
    {
        var cell = new TableCell();
        var cellProperties = new TableCellProperties
        {
            VerticalMerge = new VerticalMerge { Val = MergedCellValues.Restart }
        };
        cell.Append(cellProperties);
        cell.Append(new Paragraph(new Run(new Text(text))));
        return cell;
    }

    /// <summary>
    /// 创建垂直合并继续单元格
    /// </summary>
    private static TableCell CreateVerticalMergeContinueCell()
    {
        var cell = new TableCell();
        var cellProperties = new TableCellProperties
        {
            VerticalMerge = new VerticalMerge()
        };
        cell.Append(cellProperties);
        cell.Append(new Paragraph());
        return cell;
    }
}
