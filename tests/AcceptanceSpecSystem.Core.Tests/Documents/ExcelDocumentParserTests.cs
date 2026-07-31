using System.IO;
using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Parsers;
using AcceptanceSpecSystem.Core.Documents.Models;
using ClosedXML.Excel;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests.Documents;

public class ExcelDocumentParserTests
{
    private readonly ExcelDocumentParser _parser = new();

    [Fact]
    public async Task ExtractDocumentSnapshotAsync_ShouldReturnSheetInfoAndDataTogether()
    {
        using var workbook = new XLWorkbook();
        var first = workbook.AddWorksheet("首张表");
        first.Cell(1, 1).Value = "项目";
        first.Cell(2, 1).Value = "外观";
        var second = workbook.AddWorksheet("第二张表");
        second.Cell(3, 2).Value = "规格";
        second.Cell(4, 2).Value = "无划伤";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var snapshot = await _parser.ExtractDocumentSnapshotAsync(stream);

        snapshot.Tables.Should().HaveCount(2);
        snapshot.TableData.Should().HaveCount(2);
        snapshot.Tables.Select(table => table.Name)
            .Should().ContainInOrder("首张表", "第二张表");
        snapshot.Tables[1].UsedRangeStartRow.Should().Be(3);
        snapshot.Tables[1].UsedRangeStartColumn.Should().Be(2);
        snapshot.TableData[0].Headers.Should().ContainSingle().Which.Should().Be("项目");
        snapshot.TableData[1].Rows.Should().ContainSingle();
        snapshot.TableData[1].Rows[0].GetValue(0).Should().Be("无划伤");
    }

    [Fact]
    public async Task ProjectedSnapshotData_ShouldMatchDirectComplexHeaderExtraction()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("多行表头");
        sheet.Cell(1, 1).Value = "验规";
        sheet.Cell(2, 1).Value = "项目";
        sheet.Cell(2, 2).Value = "规格";
        sheet.Cell(2, 3).Value = "厂商确认";
        sheet.Cell(3, 3).Value = "OK/NG";
        sheet.Cell(3, 4).Value = "Remark";
        sheet.Cell(4, 1).Value = "外观";
        sheet.Cell(4, 2).Value = "无划伤";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        var snapshot = await _parser.ExtractDocumentSnapshotAsync(stream);
        var mapping = new ColumnMapping
        {
            HeaderRowIndex = 1,
            HeaderRowCount = 2,
            DataStartRowIndex = 3
        };

        var projected = TableDataProjection.Project(snapshot.TableData[0], mapping);
        stream.Position = 0;
        var direct = await _parser.ExtractTableDataAsync(stream, 0, mapping);

        projected.Headers.Should().Equal(direct.Headers);
        projected.Rows.Select(row => row.Cells.Select(cell => cell.Value))
            .Should().BeEquivalentTo(
                direct.Rows.Select(row => row.Cells.Select(cell => cell.Value)),
                options => options.WithStrictOrdering());
        projected.TotalDataRowCount.Should().Be(direct.TotalDataRowCount);
        projected.OriginalRowCount.Should().Be(direct.OriginalRowCount);
        projected.MergedCells.Should().BeEquivalentTo(direct.MergedCells);
    }

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

    [Fact]
    public async Task ExtractTableDataAsync_WithVerticalMerge_ShouldExposeRelativeMergedCellCoordinates()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("工作表1");
        sheet.Cell(5, 2).Value = "项目";
        sheet.Cell(5, 3).Value = "规格";
        sheet.Cell(5, 4).Value = "Remark";
        sheet.Cell(5, 5).Value = "備註";
        sheet.Cell(6, 2).Value = "外观";
        sheet.Cell(6, 3).Value = "无划伤";
        sheet.Range("D6:D8").Merge();
        sheet.Cell(6, 5).Value = "逐行备注";
        sheet.Cell(8, 3).Value = "尺寸";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var tableData = await _parser.ExtractTableDataAsync(stream, 0);

        tableData.MergedCells.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new MergedCellInfo
            {
                StartRow = 1,
                StartColumn = 2,
                EndRow = 3,
                EndColumn = 2
            });
    }
}
