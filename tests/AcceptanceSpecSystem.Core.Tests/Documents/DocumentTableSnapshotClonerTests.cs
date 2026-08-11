using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Models;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests.Documents;

public sealed class DocumentTableSnapshotClonerTests
{
    [Fact]
    public void Clone_ShouldIsolateMutableCollections()
    {
        var source = new DocumentTableSnapshot
        {
            Tables =
            [
                new TableInfo
                {
                    Index = 0,
                    Name = "Sheet1",
                    Headers = ["项目", "规格"]
                }
            ],
            TableData =
            [
                new TableData
                {
                    TableIndex = 0,
                    Headers = ["项目", "规格"],
                    Rows =
                    [
                        new RowData
                        {
                            Index = 0,
                            Cells =
                            [
                                new CellData
                                {
                                    ColumnIndex = 0,
                                    Value = "外观",
                                    StructuredValue = new StructuredCellValue
                                    {
                                        Parts =
                                        [
                                            new StructuredCellPart { Type = "text", Text = "外观" }
                                        ]
                                    }
                                }
                            ]
                        }
                    ],
                    MergedCells =
                    [
                        new MergedCellInfo
                        {
                            StartRow = 0,
                            StartColumn = 0,
                            EndRow = 0,
                            EndColumn = 1
                        }
                    ]
                }
            ]
        };

        var clone = DocumentTableSnapshotCloner.Clone(source);
        clone.TableData[0].Rows[0].Cells[0].Value = "changed";
        clone.TableData[0].Headers[0] = "changed";

        source.TableData[0].Rows[0].Cells[0].Value.Should().Be("外观");
        source.TableData[0].Headers[0].Should().Be("项目");
    }
}
