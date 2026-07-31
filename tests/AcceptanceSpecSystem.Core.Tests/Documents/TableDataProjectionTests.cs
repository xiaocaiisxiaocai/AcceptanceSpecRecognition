using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Models;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests.Documents;

public class TableDataProjectionTests
{
    [Fact]
    public void Project_ShouldRebuildMultiRowHeaderWithoutChangingOriginalCoordinates()
    {
        var source = new TableData
        {
            TableIndex = 2,
            Headers = ["验规名称", "", "", ""],
            Rows =
            [
                CreateRow(0, "项目", "规格", "厂商确认", "厂商确认"),
                CreateRow(1, "", "", "OK/NG", "Remark"),
                CreateRow(2, "外观", "无划伤", "", "")
            ],
            OriginalRowCount = 4,
            TotalDataRowCount = 3,
            MergedCells =
            [
                new MergedCellInfo
                {
                    StartRow = 1,
                    StartColumn = 2,
                    EndRow = 1,
                    EndColumn = 3
                }
            ]
        };

        var projected = TableDataProjection.Project(
            source,
            new ColumnMapping
            {
                HeaderRowIndex = 1,
                HeaderRowCount = 2,
                DataStartRowIndex = 3
            });

        projected.TableIndex.Should().Be(2);
        projected.Headers.Should().ContainInOrder(
            "项目",
            "规格",
            "厂商确认 / OK/NG",
            "厂商确认 / Remark");
        projected.Rows.Should().ContainSingle();
        projected.Rows[0].Index.Should().Be(0);
        projected.Rows[0].Cells.Should().OnlyContain(cell => cell.RowIndex == 0);
        projected.Rows[0].GetValue(0).Should().Be("外观");
        projected.OriginalRowCount.Should().Be(4);
        projected.TotalDataRowCount.Should().Be(1);
        projected.MergedCells.Should().BeEquivalentTo(source.MergedCells);
    }

    private static RowData CreateRow(int index, params string[] values) =>
        new()
        {
            Index = index,
            Cells = values
                .Select((value, columnIndex) => new CellData
                {
                    RowIndex = index,
                    ColumnIndex = columnIndex,
                    Value = value
                })
                .ToList()
        };
}
