using AcceptanceSpecSystem.Application.Contracts;
using static AcceptanceSpecSystem.Application.Services.MatchingResultHelpers;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class MatchingWorkflowSupportService
{
    private static void ApplyRegionWriteTarget(
        FillResult fillResult,
        MatchSourceItem? sourceItem,
        int fallbackAcceptanceColumnIndex,
        int? fallbackRemarkColumnIndex)
    {
        fillResult.RegionId = sourceItem?.RegionId;
        fillResult.RegionIndex = sourceItem?.RegionIndex;
        fillResult.AcceptanceColumnIndex = sourceItem?.AcceptanceColumnIndex ?? fallbackAcceptanceColumnIndex;
        fillResult.RemarkColumnIndex = sourceItem?.RemarkColumnIndex ?? fallbackRemarkColumnIndex;
    }

    private void EnsureExecutionPreviewContext(
        int? projectColumnIndex,
        int? specificationColumnIndex,
        int? tableIndex = null)
    {
        if (projectColumnIndex.HasValue && specificationColumnIndex.HasValue)
        {
            return;
        }

        var prefix = tableIndex.HasValue
            ? $"表格{tableIndex.Value}执行填充"
            : "执行填充";
        throw Failure(400, $"{prefix}必须提供项目列索引和规格列索引，请重新预览后再执行");
    }
}
