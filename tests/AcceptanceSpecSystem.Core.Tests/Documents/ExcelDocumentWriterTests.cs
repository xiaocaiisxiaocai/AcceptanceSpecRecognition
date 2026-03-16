using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.Documents.Writers;
using ClosedXML.Excel;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests.Documents;

/// <summary>
/// ExcelDocumentWriter 测试
/// </summary>
public class ExcelDocumentWriterTests
{
    private readonly ExcelDocumentWriter _writer = new();

    [Fact]
    public void CanWrite_ShouldReturnTrue_ForXlsxFile()
    {
        _writer.CanWrite("test.xlsx").Should().BeTrue();
    }

    [Fact]
    public void CanWrite_ShouldReturnFalse_ForNonXlsxFile()
    {
        _writer.CanWrite("test.docx").Should().BeFalse();
        _writer.CanWrite("test.xls").Should().BeFalse();
        _writer.CanWrite("").Should().BeFalse();
    }

    [Fact]
    public async Task WriteTableDataAsync_ShouldUpdateCells()
    {
        using var stream = CreateWorkbook(("Sheet1", new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" }
        }));

        var operations = new List<CellWriteOperation>
        {
            CellWriteOperation.Create(1, 2, "OK"),
            CellWriteOperation.Create(1, 3, "R1")
        };

        var count = await _writer.WriteTableDataAsync(stream, 0, operations);

        count.Should().Be(2);

        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Sheet1");
        sheet.Cell(2, 3).GetString().Should().Be("OK");
        sheet.Cell(2, 4).GetString().Should().Be("R1");
    }

    [Fact]
    public async Task WriteTableDataAsync_ShouldIgnoreInvalidCellPosition()
    {
        using var stream = CreateWorkbook(("Sheet1", new[]
        {
            new[] { "项目", "规格", "验收" },
            new[] { "P1", "S1", "" }
        }));

        var operations = new List<CellWriteOperation>
        {
            CellWriteOperation.Create(1, 2, "OK"),
            CellWriteOperation.Create(99, 99, "无效")
        };

        var count = await _writer.WriteTableDataAsync(stream, 0, operations);

        count.Should().Be(1);

        using var workbook = new XLWorkbook(stream);
        workbook.Worksheet("Sheet1").Cell(2, 3).GetString().Should().Be("OK");
    }

    [Fact]
    public async Task WriteTableDataAsync_ShouldHandleMergedCells()
    {
        using var stream = CreateWorkbook(("Sheet1", new[]
        {
            new[] { "列1", "列2", "验收", "备注" },
            new[] { "合并值", "", "", "" }
        }));

        using (var workbook = new XLWorkbook(stream))
        {
            var sheet = workbook.Worksheet("Sheet1");
            sheet.Range(2, 1, 2, 2).Merge();
            stream.Position = 0;
            stream.SetLength(0);
            workbook.SaveAs(stream);
            stream.Position = 0;
        }

        var operations = new List<CellWriteOperation>
        {
            // 写入合并区域的第二列，期望回写到合并主单元格
            CellWriteOperation.Create(1, 1, "新合并值")
        };

        var count = await _writer.WriteTableDataAsync(stream, 0, operations);
        count.Should().Be(1);

        using var resultWorkbook = new XLWorkbook(stream);
        resultWorkbook.Worksheet("Sheet1").Cell(2, 1).GetString().Should().Be("新合并值");
    }

    [Fact]
    public async Task WriteMultipleTablesAsync_ShouldWriteAllSheets()
    {
        using var stream = CreateWorkbook(
            ("SheetA", new[]
            {
                new[] { "H1", "H2" },
                new[] { "A-OLD-0", "A-OLD-1" }
            }),
            ("SheetB", new[]
            {
                new[] { "X1", "X2" },
                new[] { "B-OLD-0", "B-OLD-1" }
            }));

        var tableOperations = new Dictionary<int, List<CellWriteOperation>>
        {
            [0] = new()
            {
                CellWriteOperation.Create(1, 0, "A-R1C0"),
                CellWriteOperation.Create(1, 1, "A-R1C1")
            },
            [1] = new()
            {
                CellWriteOperation.Create(1, 0, "B-R1C0"),
                CellWriteOperation.Create(1, 1, "B-R1C1")
            }
        };

        var count = await _writer.WriteMultipleTablesAsync(stream, tableOperations);
        count.Should().Be(4);

        using var workbook = new XLWorkbook(stream);
        workbook.Worksheet("SheetA").Cell(2, 1).GetString().Should().Be("A-R1C0");
        workbook.Worksheet("SheetA").Cell(2, 2).GetString().Should().Be("A-R1C1");
        workbook.Worksheet("SheetB").Cell(2, 1).GetString().Should().Be("B-R1C0");
        workbook.Worksheet("SheetB").Cell(2, 2).GetString().Should().Be("B-R1C1");
    }

    [Fact]
    public void DocumentType_ShouldBeExcel()
    {
        _writer.DocumentType.Should().Be(DocumentType.Excel);
    }

    private static MemoryStream CreateWorkbook(params (string Name, string[][] Rows)[] sheets)
    {
        var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            foreach (var (name, rows) in sheets)
            {
                var sheet = workbook.AddWorksheet(name);
                for (var r = 0; r < rows.Length; r++)
                {
                    for (var c = 0; c < rows[r].Length; c++)
                    {
                        sheet.Cell(r + 1, c + 1).Value = rows[r][c] ?? string.Empty;
                    }
                }
            }

            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        return stream;
    }
}
