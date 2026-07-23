using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class MatchingWorkflowSupportService
{
    private static IReadOnlyDictionary<int, IReadOnlyDictionary<int, MatchPreviewItem>> BuildAuthoritativeExecutionHistoryPreviewLookup(
        IReadOnlyDictionary<int, ExecutionMatchSnapshot> currentMatchLookups,
        MatchingConfig executionConfig)
    {
        if (currentMatchLookups.Count == 0)
        {
            return new Dictionary<int, IReadOnlyDictionary<int, MatchPreviewItem>>();
        }

        return currentMatchLookups.ToDictionary(
            table => table.Key,
            table => (IReadOnlyDictionary<int, MatchPreviewItem>)table.Value.SourceRowLookup
                .ToDictionary(
                    source => source.Key,
                    source =>
                    {
                        table.Value.MatchLookup.TryGetValue(source.Key, out var matchResult);
                        return new MatchPreviewItem
                        {
                            RegionId = source.Value.RegionId,
                            RegionIndex = source.Value.RegionIndex,
                            AcceptanceColumnIndex = source.Value.AcceptanceColumnIndex,
                            RemarkColumnIndex = source.Value.RemarkColumnIndex,
                            RowIndex = source.Key,
                            SourceProject = source.Value.Project,
                            SourceSpecification = source.Value.Specification,
                            BestMatch = matchResult == null
                                ? null
                                : MatchingResultDtoMapper.ToMatchResultDto(matchResult),
                            ConfidenceLevel = ResolveAuthoritativeConfidenceLevel(matchResult, executionConfig),
                            NoMatchReason = matchResult == null ? "执行时未匹配到可用规格" : null
                        };
                    }));
    }

    private static string ResolveAuthoritativeConfidenceLevel(
        MatchResult? matchResult,
        MatchingConfig executionConfig)
    {
        if (matchResult?.MatchedSpecId == null)
        {
            return "none";
        }

        if (matchResult.Score >= executionConfig.HighConfidenceThreshold)
        {
            return "high";
        }

        return matchResult.Score >= executionConfig.MinScoreThreshold ? "medium" : "low";
    }
}
