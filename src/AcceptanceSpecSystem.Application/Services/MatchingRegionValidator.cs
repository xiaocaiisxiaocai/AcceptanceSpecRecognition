using AcceptanceSpecSystem.Application.Contracts;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// Matching 预览与执行共用的多区域输入校验，避免两个入口对同一请求采用不同口径。
/// </summary>
internal static class MatchingRegionValidator
{
    public static string? GetValidationError(
        IReadOnlyCollection<BatchTableRegionConfig>? regions,
        int tableIndex)
    {
        if (regions == null || regions.Count == 0)
        {
            return null;
        }

        var label = $"表格{tableIndex + 1}";
        if (regions.Any(region => region.RegionIndex < 0))
        {
            return $"{label}存在非法的区域序号";
        }

        if (regions.Select(region => region.RegionIndex).Distinct().Count() != regions.Count)
        {
            return $"{label}存在重复的区域序号";
        }

        var explicitIds = regions
            .Select(region => region.RegionId?.Trim())
            .Where(regionId => !string.IsNullOrWhiteSpace(regionId))
            .ToList();
        if (explicitIds.Distinct(StringComparer.Ordinal).Count() != explicitIds.Count)
        {
            return $"{label}存在重复的区域标识";
        }

        foreach (var region in regions)
        {
            var regionLabel = $"{label}区域{region.RegionIndex + 1}";
            var columns = new int?[]
            {
                region.ProjectColumnIndex,
                region.SpecificationColumnIndex,
                region.AcceptanceColumnIndex,
                region.RemarkColumnIndex
            };
            if (columns.Where(column => column.HasValue).Any(column => column!.Value < 0))
            {
                return $"{regionLabel}存在非法列索引";
            }

            var mappedColumns = columns
                .Where(column => column.HasValue)
                .Select(column => column!.Value)
                .ToList();
            if (mappedColumns.Distinct().Count() != mappedColumns.Count)
            {
                return $"{regionLabel}的字段列不能重复";
            }

            if (region.HeaderRowStart is <= 0 || region.HeaderRowCount is <= 0 || region.DataStartRow is <= 0)
            {
                return $"{regionLabel}的表头或数据起始行不合法";
            }

            if (region.HeaderRowStart.HasValue &&
                region.HeaderRowCount.HasValue &&
                region.DataStartRow.HasValue &&
                region.DataStartRow.Value < region.HeaderRowStart.Value + region.HeaderRowCount.Value)
            {
                return $"{regionLabel}的数据起始行必须位于表头之后";
            }

            if (region.DataEndRow.HasValue &&
                (!region.DataStartRow.HasValue || region.DataEndRow.Value < region.DataStartRow.Value))
            {
                return $"{regionLabel}的数据结束行不能早于数据起始行";
            }
        }

        if (regions.Count > 1 &&
            regions.Any(region => !region.DataStartRow.HasValue || !region.DataEndRow.HasValue))
        {
            return $"{label}的多区域配置必须提供每段数据起止行";
        }

        var rangedRegions = regions
            .Where(region => region.DataStartRow.HasValue && region.DataEndRow.HasValue)
            .OrderBy(region => region.DataStartRow)
            .ThenBy(region => region.DataEndRow)
            .ToList();
        for (var index = 1; index < rangedRegions.Count; index++)
        {
            if (rangedRegions[index].DataStartRow <= rangedRegions[index - 1].DataEndRow)
            {
                return $"{label}的数据区域不能重叠";
            }

            if (rangedRegions[index].HeaderRowStart.HasValue &&
                rangedRegions[index].HeaderRowStart <= rangedRegions[index - 1].DataEndRow)
            {
                return $"{label}的后续区域表头不能落入前一区域数据范围";
            }
        }

        return null;
    }
}
