using AcceptanceSpecSystem.Core.Documents.Models;

namespace AcceptanceSpecSystem.Core.Documents;

/// <summary>
/// 深复制文档表格快照，避免缓存对象被并发请求修改。
/// </summary>
public static class DocumentTableSnapshotCloner
{
    public static DocumentTableSnapshot Clone(DocumentTableSnapshot source) =>
        new()
        {
            Tables = source.Tables.Select(CloneTableInfo).ToList(),
            TableData = source.TableData.Select(CloneTableData).ToList()
        };

    public static TableInfo CloneTableInfo(TableInfo source) =>
        new()
        {
            Index = source.Index,
            Name = source.Name,
            RowCount = source.RowCount,
            ColumnCount = source.ColumnCount,
            IsNested = source.IsNested,
            ParentTableIndex = source.ParentTableIndex,
            PreviewText = source.PreviewText,
            Headers = source.Headers?.ToList(),
            HasMergedCells = source.HasMergedCells,
            UsedRangeStartRow = source.UsedRangeStartRow,
            UsedRangeStartColumn = source.UsedRangeStartColumn
        };

    public static TableData CloneTableData(TableData source) =>
        new()
        {
            TableIndex = source.TableIndex,
            Headers = source.Headers.ToList(),
            Rows = source.Rows.Select(CloneRow).ToList(),
            TotalDataRowCount = source.TotalDataRowCount,
            OriginalRowCount = source.OriginalRowCount,
            MergedCells = source.MergedCells
                .Select(merged => new MergedCellInfo
                {
                    StartRow = merged.StartRow,
                    StartColumn = merged.StartColumn,
                    EndRow = merged.EndRow,
                    EndColumn = merged.EndColumn
                })
                .ToList()
        };

    public static RowData CloneRow(RowData source) =>
        new()
        {
            Index = source.Index,
            IsHeader = source.IsHeader,
            Cells = source.Cells.Select(CloneCell).ToList()
        };

    public static CellData CloneCell(CellData source) =>
        new()
        {
            RowIndex = source.RowIndex,
            ColumnIndex = source.ColumnIndex,
            Value = source.Value,
            StructuredValue = CloneStructuredValue(source.StructuredValue),
            IsMerged = source.IsMerged,
            IsMergeStart = source.IsMergeStart,
            RowSpan = source.RowSpan,
            ColSpan = source.ColSpan
        };

    private static StructuredCellValue? CloneStructuredValue(StructuredCellValue? source)
    {
        if (source == null)
        {
            return null;
        }

        return new StructuredCellValue
        {
            Parts = source.Parts
                .Select(part => new StructuredCellPart
                {
                    Type = part.Type,
                    Text = part.Text,
                    Table = CloneStructuredTable(part.Table)
                })
                .ToList()
        };
    }

    private static StructuredTableValue? CloneStructuredTable(StructuredTableValue? source)
    {
        if (source == null)
        {
            return null;
        }

        return new StructuredTableValue
        {
            RowCount = source.RowCount,
            ColumnCount = source.ColumnCount,
            Rows = source.Rows
                .Select(row => row
                    .Select(CloneStructuredValue)
                    .Where(value => value != null)
                    .Select(value => value!)
                    .ToList())
                .ToList()
        };
    }
}
