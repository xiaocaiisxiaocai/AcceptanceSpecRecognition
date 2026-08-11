using AcceptanceSpecSystem.Core.Documents.Models;

namespace AcceptanceSpecSystem.Core.Documents;

/// <summary>
/// 估算文档表格快照占用的内存字节数，用于有界缓存计费。
/// </summary>
public static class DocumentTableSnapshotSizeEstimator
{
    private const int ObjectOverheadBytes = 64;
    private const int CollectionOverheadBytes = 48;

    public static long EstimateBytes(DocumentTableSnapshot snapshot)
    {
        long total = ObjectOverheadBytes * 2;
        total += EstimateTables(snapshot.Tables);
        total += EstimateTableDataList(snapshot.TableData);
        return total;
    }

    private static long EstimateTables(IReadOnlyList<TableInfo> tables)
    {
        long total = CollectionOverheadBytes;
        foreach (var table in tables)
        {
            total += ObjectOverheadBytes;
            total += EstimateString(table.Name);
            total += EstimateString(table.PreviewText);
            if (table.Headers != null)
            {
                total += CollectionOverheadBytes;
                foreach (var header in table.Headers)
                {
                    total += EstimateString(header);
                }
            }
        }

        return total;
    }

    private static long EstimateTableDataList(IReadOnlyList<TableData> tableDataList)
    {
        long total = CollectionOverheadBytes;
        foreach (var tableData in tableDataList)
        {
            total += EstimateTableData(tableData);
        }

        return total;
    }

    private static long EstimateTableData(TableData tableData)
    {
        long total = ObjectOverheadBytes;
        total += CollectionOverheadBytes;
        foreach (var header in tableData.Headers)
        {
            total += EstimateString(header);
        }

        total += CollectionOverheadBytes;
        foreach (var row in tableData.Rows)
        {
            total += ObjectOverheadBytes;
            total += CollectionOverheadBytes;
            foreach (var cell in row.Cells)
            {
                total += EstimateCell(cell);
            }
        }

        total += CollectionOverheadBytes;
        total += tableData.MergedCells.Count * ObjectOverheadBytes;
        return total;
    }

    private static long EstimateCell(CellData cell)
    {
        long total = ObjectOverheadBytes + EstimateString(cell.Value);
        if (cell.StructuredValue != null)
        {
            total += EstimateStructuredValue(cell.StructuredValue);
        }

        return total;
    }

    private static long EstimateStructuredValue(StructuredCellValue structuredValue)
    {
        long total = ObjectOverheadBytes + CollectionOverheadBytes;
        foreach (var part in structuredValue.Parts)
        {
            total += ObjectOverheadBytes + EstimateString(part.Text);
            if (part.Table != null)
            {
                total += ObjectOverheadBytes + CollectionOverheadBytes;
                foreach (var row in part.Table.Rows)
                {
                    total += CollectionOverheadBytes;
                    foreach (var nested in row)
                    {
                        if (nested != null)
                        {
                            total += EstimateStructuredValue(nested);
                        }
                    }
                }
            }
        }

        return total;
    }

    private static long EstimateString(string? value) =>
        string.IsNullOrEmpty(value) ? 0 : value.Length * sizeof(char);
}
