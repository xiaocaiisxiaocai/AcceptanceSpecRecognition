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
            .Select(group => new
            {
                SpecId = group.Key,
                ReferenceVersion = specLookup[group.Key].ReferenceVersion,
                Count = group.Count()
            })
            .OrderBy(increment => increment.SpecId)
            .ToArray();

        if (increments.Length == 0)
        {
            return;
        }

        var referencedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var referenceEvents = increments
            .SelectMany(increment => Enumerable.Range(1, increment.Count)
                .Select(occurrenceIndex => new AcceptanceSpecReferenceEvent
                {
                    AcceptanceSpecId = increment.SpecId,
                    ReferenceVersion = increment.ReferenceVersion,
                    TaskId = taskResult.TaskId,
                    TaskOccurrenceIndex = occurrenceIndex,
                    OccurrenceCount = 1,
                    ReferencedAtUtc = referencedAtUtc
                }))
            .ToArray();

        await _unitOfWork.AcceptanceSpecReferenceEvents.AddRangeAsync(
            referenceEvents,
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var increment in increments)
        {
            await _unitOfWork.AcceptanceSpecs
                .Query(asNoTracking: false)
                .Where(spec =>
                    spec.Id == increment.SpecId &&
                    spec.ReferenceVersion == increment.ReferenceVersion)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        spec => spec.ReferenceCount,
                        spec => spec.ReferenceCount + increment.Count),
                    cancellationToken);
        }
    }
}
