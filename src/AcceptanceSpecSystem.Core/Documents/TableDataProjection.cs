using AcceptanceSpecSystem.Core.Documents.Models;

namespace AcceptanceSpecSystem.Core.Documents;

/// <summary>
/// 基于已完整提取的表格数据重新投影表头和数据区，避免重复打开源文档。
/// </summary>
public static class TableDataProjection
{
    public static TableData Project(
        TableData source,
        ColumnMapping mapping,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(mapping);
        cancellationToken.ThrowIfCancellationRequested();

        var logicalRows = BuildLogicalRows(source, cancellationToken);
        var headerRowIndex = Math.Max(0, mapping.HeaderRowIndex);
        var headerRowCount = Math.Max(1, mapping.HeaderRowCount);
        var dataStartRowIndex = Math.Max(0, mapping.DataStartRowIndex);
        var columnCount = Math.Max(
            source.ColumnCount,
            logicalRows
                .SelectMany(row => row.Cells)
                .Select(cell => cell.ColumnIndex + 1)
                .DefaultIfEmpty(0)
                .Max());

        var headers = new string[columnCount];
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parts = new List<string>(headerRowCount);
            for (var rowOffset = 0; rowOffset < headerRowCount; rowOffset++)
            {
                var rowIndex = headerRowIndex + rowOffset;
                if (rowIndex >= logicalRows.Count)
                {
                    break;
                }

                var value = logicalRows[rowIndex].GetValue(columnIndex)?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    parts.Add(value);
                }
            }

            headers[columnIndex] = string.Join(" / ", parts);
        }

        var rows = logicalRows
            .Skip(Math.Min(dataStartRowIndex, logicalRows.Count))
            .Select((row, index) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return CloneRow(row, index);
            })
            .ToList();
        var originalRowCount = source.OriginalRowCount ?? logicalRows.Count;

        return new TableData
        {
            TableIndex = source.TableIndex,
            Headers = headers,
            Rows = rows,
            TotalDataRowCount = Math.Max(0, originalRowCount - dataStartRowIndex),
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
    }

    private static List<RowData> BuildLogicalRows(
        TableData source,
        CancellationToken cancellationToken)
    {
        var rows = new List<RowData>();
        if (source.Headers.Count > 0)
        {
            rows.Add(new RowData
            {
                Index = 0,
                IsHeader = true,
                Cells = source.Headers
                    .Select((value, columnIndex) => new CellData
                    {
                        RowIndex = 0,
                        ColumnIndex = columnIndex,
                        Value = value
                    })
                    .ToList()
            });
        }

        var rowOffset = rows.Count;
        rows.AddRange(source.Rows.Select((row, index) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CloneRow(row, rowOffset + index);
        }));
        return rows;
    }

    private static RowData CloneRow(RowData row, int rowIndex) =>
        new()
        {
            Index = rowIndex,
            IsHeader = row.IsHeader,
            Cells = row.Cells
                .Select(cell => new CellData
                {
                    RowIndex = rowIndex,
                    ColumnIndex = cell.ColumnIndex,
                    Value = cell.Value,
                    StructuredValue = cell.StructuredValue == null
                        ? null
                        : DocumentTableSnapshotCloner.CloneCell(cell).StructuredValue,
                    IsMerged = cell.IsMerged,
                    IsMergeStart = cell.IsMergeStart,
                    RowSpan = cell.RowSpan,
                    ColSpan = cell.ColSpan
                })
                .ToList()
        };
}
