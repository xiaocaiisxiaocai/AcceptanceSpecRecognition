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
    public async Task WriteMultipleTablesAsync_ShouldWriteLargeWorksheetWithExcelTable()
    {
        const int dataRowCount = 1000;
        using var stream = CreateWorkbookWithExcelTable(dataRowCount);
        var operations = Enumerable.Range(1, dataRowCount)
            .SelectMany(rowIndex => new[]
            {
                CellWriteOperation.Create(rowIndex, 3, $"AC-{rowIndex}"),
                CellWriteOperation.Create(rowIndex, 4, $"RM-{rowIndex}")
            })
            .ToList();

        var count = await _writer.WriteMultipleTablesAsync(stream, new Dictionary<int, List<CellWriteOperation>>
        {
            [0] = operations
        });

        count.Should().Be(dataRowCount * 2);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Sheet1");
        sheet.Tables.Should().ContainSingle(table => table.Name == "TargetFillTable");
        sheet.Cell(2, 4).GetString().Should().Be("AC-1");
        sheet.Cell(2, 5).GetString().Should().Be("RM-1");
        sheet.Cell(1001, 4).GetString().Should().Be("AC-1000");
        sheet.Cell(1001, 5).GetString().Should().Be("RM-1000");
    }

    [Fact]
    public async Task WriteTableDataAsync_ShouldRejectOperationsThatCollapseToSameMergedCell()
    {
        using var stream = CreateWorkbook(("Sheet1", new[]
        {
            new[] { "项目", "验收", "备注" },
            new[] { "P1", "", "" }
        }));

        using (var workbook = new XLWorkbook(stream))
        {
            workbook.Worksheet("Sheet1").Range(2, 2, 2, 3).Merge();
            stream.Position = 0;
            stream.SetLength(0);
            workbook.SaveAs(stream);
            stream.Position = 0;
        }

        var act = () => _writer.WriteTableDataAsync(stream, 0, new[]
        {
            CellWriteOperation.Create(1, 1, "OK"),
            CellWriteOperation.Create(1, 2, "Remark")
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*同一单元格*");
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

    private static MemoryStream CreateWorkbookWithExcelTable(int dataRowCount)
    {
        var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Sheet1");
            sheet.Cell(1, 1).Value = "序号";
            sheet.Cell(1, 2).Value = "项目";
            sheet.Cell(1, 3).Value = "规格";
            sheet.Cell(1, 4).Value = "验收";
            sheet.Cell(1, 5).Value = "备注";
            sheet.Cell(1, 6).Value = "期望命中键";

            for (var rowIndex = 1; rowIndex <= dataRowCount; rowIndex++)
            {
                var rowNumber = rowIndex + 1;
                sheet.Cell(rowNumber, 1).Value = rowIndex;
                sheet.Cell(rowNumber, 2).Value = $"P-{rowIndex:0000}";
                sheet.Cell(rowNumber, 3).Value = $"S-{rowIndex:0000}";
                sheet.Cell(rowNumber, 4).Value = string.Empty;
                sheet.Cell(rowNumber, 5).Value = string.Empty;
                sheet.Cell(rowNumber, 6).Value = $"P-{rowIndex:0000}|S-{rowIndex:0000}";
            }

            sheet.Range(1, 1, dataRowCount + 1, 6).CreateTable("TargetFillTable");
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        return stream;
    }
}
