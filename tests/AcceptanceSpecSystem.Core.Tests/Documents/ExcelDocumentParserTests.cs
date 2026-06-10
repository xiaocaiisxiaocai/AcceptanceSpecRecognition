using System.IO;
using AcceptanceSpecSystem.Core.Documents.Parsers;
using ClosedXML.Excel;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests.Documents;

public class ExcelDocumentParserTests
{
    private readonly ExcelDocumentParser _parser = new();

    [Fact]
    public async Task ExtractTableDataAsync_WithMaxDataRowCount_ShouldOnlyReturnRequestedRows()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("工作表1");
        sheet.Cell(1, 1).Value = "项目";
        sheet.Cell(1, 2).Value = "规格";
        for (var i = 0; i < 5; i++)
        {
            sheet.Cell(i + 2, 1).Value = $"项目{i + 1}";
            sheet.Cell(i + 2, 2).Value = $"规格{i + 1}";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var tableData = await _parser.ExtractTableDataAsync(
            stream,
            0,
            mapping: null,
            maxDataRowCount: 2);

        tableData.Headers.Should().ContainInOrder("项目", "规格");
        tableData.Rows.Should().HaveCount(2);
        tableData.Rows[0].GetValue(0).Should().Be("项目1");
        tableData.Rows[1].GetValue(1).Should().Be("规格2");
    }
}
