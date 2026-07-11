using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 为匹配实现增加输入预算和进程内并发闸门，不改变匹配排序与决策语义。
/// </summary>
public sealed class ResourceGovernedMatchingService : IMatchingService
{
    private readonly IMatchingService _inner;
    private readonly IResourceBudgetGovernor _resourceBudgetGovernor;

    public ResourceGovernedMatchingService(
        IMatchingService inner,
        IResourceBudgetGovernor resourceBudgetGovernor)
    {
        _inner = inner;
        _resourceBudgetGovernor = resourceBudgetGovernor;
    }

    public async Task<List<MatchResult>> FindMatchesAsync(
        MatchSource source,
        IEnumerable<MatchCandidate> candidates,
        MatchingConfig? config = null)
    {
        var candidateList = candidates as IReadOnlyCollection<MatchCandidate> ?? candidates.ToList();
        _resourceBudgetGovernor.ValidateMatchingItems(candidateList.Count + 1);
        using var resourceLease = await _resourceBudgetGovernor.AcquireAsync(ResourceWorkload.HighCostMatching);
        return await _inner.FindMatchesAsync(source, candidateList, config);
    }

    public async Task<BatchMatchResult> BatchMatchAsync(
        IEnumerable<MatchSource> sources,
        IEnumerable<MatchCandidate> candidates,
        MatchingConfig? config = null,
        IProgress<BatchMatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourceList = sources as IReadOnlyCollection<MatchSource> ?? sources.ToList();
        var candidateList = candidates as IReadOnlyCollection<MatchCandidate> ?? candidates.ToList();
        _resourceBudgetGovernor.ValidateMatchingItems(checked(sourceList.Count + candidateList.Count));
        using var resourceLease = await _resourceBudgetGovernor.AcquireAsync(
            ResourceWorkload.HighCostMatching,
            cancellationToken);
        return await _inner.BatchMatchAsync(
            sourceList,
            candidateList,
            config,
            progress,
            cancellationToken);
    }
}
