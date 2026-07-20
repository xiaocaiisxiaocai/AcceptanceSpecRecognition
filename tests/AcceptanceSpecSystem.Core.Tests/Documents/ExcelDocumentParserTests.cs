using System.IO;
using AcceptanceSpecSystem.Core.Documents.Parsers;
using AcceptanceSpecSystem.Core.Documents.Models;
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

    [Fact]
    public async Task ExtractTableDataAsync_WhenLeafHeaderUsesMergedParent_ShouldResolveMasterValueFromLastHeaderRow()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("工作表1");
        sheet.Range("A1:A2").Merge().Value = "细项";
        sheet.Range("B1:B2").Merge().Value = "规格";
        sheet.Cell(1, 3).Value = "厂商确认";
        sheet.Cell(2, 3).Value = "OK/NG";
        sheet.Cell(2, 4).Value = "Remark";
        sheet.Cell(3, 1).Value = "装机前验机";
        sheet.Cell(3, 2).Value = "验收要求";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var tableData = await _parser.ExtractTableDataAsync(
            stream,
            0,
            new ColumnMapping
            {
                HeaderRowIndex = 1,
                HeaderRowCount = 1,
                DataStartRowIndex = 2
            });

        tableData.Headers.Should().ContainInOrder("细项", "规格", "OK/NG", "Remark");
    }
}
