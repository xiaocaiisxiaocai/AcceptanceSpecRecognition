using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Entities;
using static AcceptanceSpecSystem.Application.Services.MatchingResultHelpers;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class MatchingWorkflowSupportService
{
    private async Task CanonicalizeExecutionRegionSourcesAsync(
        WordFile wordFile,
        BatchTableFillMapping table,
        ExecutionMatchSnapshot snapshot,
        bool filterEmptySourceRows,
        CancellationToken cancellationToken)
    {
        var hasRegions = table.Regions is { Count: > 0 };
        if (!hasRegions &&
            (!table.ProjectColumnIndex.HasValue ||
             !table.SpecificationColumnIndex.HasValue))
        {
            throw Failure(
                400,
                $"表格{table.TableIndex + 1}缺少项目列和规格列，无法验证数据范围，请重新预览后再执行");
        }

        var firstRegion = hasRegions
            ? table.Regions.OrderBy(region => region.RegionIndex).First()
            : null;
        var sourceRows = await ExtractMatchSourceItemsForRegionsAsync(
            wordFile,
            table.TableIndex,
            table.ProjectColumnIndex ?? firstRegion!.ProjectColumnIndex,
            table.SpecificationColumnIndex ?? firstRegion!.SpecificationColumnIndex,
            table.HeaderRowStart,
            table.HeaderRowCount,
            table.DataStartRow,
            table.DataEndRow,
            hasRegions ? table.Regions : null,
            filterEmptySourceRows,
            cancellationToken);

        snapshot.SourceRowLookup.Clear();
        foreach (var sourceRow in sourceRows)
        {
            if (!hasRegions)
            {
                sourceRow.RegionId = $"table-{table.TableIndex}-region-0";
                sourceRow.RegionIndex = 0;
                sourceRow.AcceptanceColumnIndex = table.AcceptanceColumnIndex;
                sourceRow.RemarkColumnIndex = table.RemarkColumnIndex;
            }
            snapshot.SourceRowLookup[sourceRow.RowIndex] = sourceRow;
        }

        var outsideRegionRow = table.Mappings.FirstOrDefault(
            mapping => !snapshot.SourceRowLookup.ContainsKey(mapping.RowIndex));
        if (outsideRegionRow != null)
        {
            throw Failure(
                400,
                $"表格{table.TableIndex + 1}第{outsideRegionRow.RowIndex + 1}行不属于已确认的数据区域，请重新预览后执行");
        }
    }

    private async Task<List<MatchSourceItem>> ExtractMatchSourceItemsForRegionsAsync(
        WordFile wordFile,
        int tableIndex,
        int projectColumnIndex,
        int specificationColumnIndex,
        int? headerRowStart,
        int? headerRowCount,
        int? dataStartRow,
        int? dataEndRow,
        IReadOnlyList<BatchTableRegionConfig>? regions,
        bool filterEmptySourceRows,
        CancellationToken cancellationToken)
    {
        if (regions is not { Count: > 0 })
        {
            var tableItems = await _documentTableAccessService.ExtractMatchSourceItemsAsync(
                wordFile,
                tableIndex,
                projectColumnIndex,
                specificationColumnIndex,
                headerRowStart,
                headerRowCount,
                dataStartRow,
                dataEndRow,
                filterEmptySourceRows,
                cancellationToken);
            OffsetWordRowsToPhysicalCoordinates(
                wordFile,
                tableItems,
                headerRowStart,
                headerRowCount,
                dataStartRow);
            return tableItems;
        }

        var items = new List<MatchSourceItem>();
        foreach (var region in regions.OrderBy(item => item.RegionIndex))
        {
            var regionItems = await _documentTableAccessService.ExtractMatchSourceItemsAsync(
                wordFile,
                tableIndex,
                region.ProjectColumnIndex,
                region.SpecificationColumnIndex,
                region.HeaderRowStart,
                region.HeaderRowCount,
                region.DataStartRow,
                region.DataEndRow,
                filterEmptySourceRows,
                cancellationToken);
            OffsetWordRowsToPhysicalCoordinates(
                wordFile,
                regionItems,
                region.HeaderRowStart,
                region.HeaderRowCount,
                region.DataStartRow);
            foreach (var item in regionItems)
            {
                item.RegionId = string.IsNullOrWhiteSpace(region.RegionId)
                    ? $"table-{tableIndex}-region-{region.RegionIndex}"
                    : region.RegionId;
                item.RegionIndex = region.RegionIndex;
                item.AcceptanceColumnIndex = region.AcceptanceColumnIndex;
                item.RemarkColumnIndex = region.RemarkColumnIndex;
            }
            items.AddRange(regionItems);
        }

        return items
            .GroupBy(item => (item.RegionId, item.RowIndex))
            .Select(group => group.First())
            .OrderBy(item => item.RowIndex)
            .ToList();
    }

    private static void OffsetWordRowsToPhysicalCoordinates(
        WordFile wordFile,
        IEnumerable<MatchSourceItem> items,
        int? headerRowStart,
        int? headerRowCount,
        int? dataStartRow)
    {
        if (wordFile.FileType == UploadedFileType.ExcelXlsx)
            return;

        var normalizedHeaderRowStart = Math.Max(1, headerRowStart.GetValueOrDefault(1));
        var normalizedHeaderRowCount = Math.Max(0, headerRowCount.GetValueOrDefault(1));
        var minimumDataStartRow = normalizedHeaderRowStart + normalizedHeaderRowCount;
        var normalizedDataStartRow = Math.Max(
            minimumDataStartRow,
            dataStartRow.GetValueOrDefault(minimumDataStartRow));
        var physicalRowOffset = normalizedDataStartRow - 1;
        foreach (var item in items)
        {
            item.RowIndex += physicalRowOffset;
        }
    }
}
