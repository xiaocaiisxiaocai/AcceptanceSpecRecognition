using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class MatchingWorkflowSupportService
{
    private async Task TryLearnColumnMappingsAfterFillAsync(
        WordFile wordFile,
        BatchExecuteFillRequest request,
        int? customerId,
        int totalFilled,
        CancellationToken cancellationToken)
    {
        if (!customerId.HasValue || customerId.Value <= 0 || totalFilled <= 0)
        {
            return;
        }

        foreach (var table in request.Tables)
        {
            try
            {
                if (table.Regions.Count > 0)
                {
                    foreach (var region in table.Regions.OrderBy(region => region.RegionIndex))
                    {
                        await _columnMappingLearningService.LearnFromDocumentTableAsync(
                            customerId,
                            wordFile,
                            table.TableIndex,
                            region.ProjectColumnIndex,
                            region.SpecificationColumnIndex,
                            region.AcceptanceColumnIndex,
                            region.RemarkColumnIndex,
                            region.HeaderRowStart,
                            region.HeaderRowCount,
                            region.DataStartRow,
                            cancellationToken);
                    }
                }
                else
                {
                    await _columnMappingLearningService.LearnFromDocumentTableAsync(
                        customerId,
                        wordFile,
                        table.TableIndex,
                        table.ProjectColumnIndex,
                        table.SpecificationColumnIndex,
                        table.AcceptanceColumnIndex,
                        table.RemarkColumnIndex,
                        table.HeaderRowStart,
                        table.HeaderRowCount,
                        table.DataStartRow,
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "智能填充成功后学习列映射失败: 文件{FileId}, 表{TableIndex}, 客户{CustomerId}",
                    wordFile.Id,
                    table.TableIndex,
                    customerId);
            }
        }
    }
}
