using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class MatchingWorkflowSupportService
{
    private async Task IncrementAcceptanceSpecReferenceCountsAsync(
        FillTaskResult taskResult,
        IReadOnlyDictionary<int, AcceptanceSpec> specLookup,
        CancellationToken cancellationToken)
    {
        var fillResults = taskResult.IsBatchMode
            ? taskResult.TableEntries.SelectMany(entry => entry.FillResults)
            : taskResult.FillResults;
        var increments = fillResults
            .Where(result =>
                result.SpecId > 0 &&
                specLookup.TryGetValue(result.SpecId, out var spec) &&
                (!string.IsNullOrWhiteSpace(spec.Acceptance) ||
                 !string.IsNullOrWhiteSpace(spec.Remark)))
            .GroupBy(result => result.SpecId)
            .Select(group => new { SpecId = group.Key, Count = (long)group.Count() })
            .ToArray();

        foreach (var increment in increments)
        {
            await _unitOfWork.AcceptanceSpecs
                .Query(asNoTracking: false)
                .Where(spec => spec.Id == increment.SpecId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        spec => spec.ReferenceCount,
                        spec => spec.ReferenceCount + increment.Count),
                    cancellationToken);
        }
    }
}
