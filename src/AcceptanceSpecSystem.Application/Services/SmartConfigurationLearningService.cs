using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Application.Services;

public sealed class SmartConfigurationLearningService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly SmartConfigurationOptions _options;

    public SmartConfigurationLearningService(
        IUnitOfWork unitOfWork,
        IOptions<SmartConfigurationOptions> options)
    {
        _unitOfWork = unitOfWork;
        _options = options.Value;
    }

    public Task<SmartConfigurationLearningResult> ApplyLearningAsync(
        int customerId,
        string? tableName,
        string? tableKind,
        string? recommendation,
        IReadOnlyList<SmartConfigurationLearnedColumn> learnedColumns,
        CancellationToken cancellationToken) =>
        ApplyLearningCoreAsync(
            customerId,
            learnedColumns,
            cancellationToken,
            operationLocksAlreadyHeld: false);

    internal Task<SmartConfigurationLearningResult> ApplyLearningWithLocksHeldAsync(
        int customerId,
        IReadOnlyList<SmartConfigurationLearnedColumn> learnedColumns,
        CancellationToken cancellationToken) =>
        ApplyLearningCoreAsync(
            customerId,
            learnedColumns,
            cancellationToken,
            operationLocksAlreadyHeld: true);

    private async Task<SmartConfigurationLearningResult> ApplyLearningCoreAsync(
        int customerId,
        IReadOnlyList<SmartConfigurationLearnedColumn> learnedColumns,
        CancellationToken cancellationToken,
        bool operationLocksAlreadyHeld)
    {
        var learnedRuleCount = 0;
        var promotedGlobalRuleCount = 0;
        foreach (var learnedColumn in learnedColumns)
        {
            var pattern = learnedColumn.Header.Trim();
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            var normalizedPattern = ColumnMappingRule.NormalizePattern(pattern);
            SmartConfigurationColumnLearningResult columnResult;
            if (operationLocksAlreadyHeld)
            {
                columnResult = await ApplyColumnLearningAsync(
                    customerId,
                    pattern,
                    normalizedPattern,
                    learnedColumn.TargetField,
                    cancellationToken);
            }
            else
            {
                await using var patternLock = await _unitOfWork.AcquireOperationLockAsync(
                    BuildOperationLockKey(normalizedPattern),
                    cancellationToken);
                columnResult = await ApplyColumnLearningAsync(
                    customerId,
                    pattern,
                    normalizedPattern,
                    learnedColumn.TargetField,
                    cancellationToken);
            }

            if (columnResult.LearnedRuleCreated)
            {
                learnedRuleCount++;
            }

            if (columnResult.GlobalRulePromoted)
            {
                promotedGlobalRuleCount++;
            }
        }

        return new SmartConfigurationLearningResult(
            learnedRuleCount,
            promotedGlobalRuleCount);
    }

    internal async Task<IAsyncDisposable> AcquireOperationLocksAsync(
        IReadOnlyList<SmartConfigurationLearnedColumn> learnedColumns,
        CancellationToken cancellationToken)
    {
        var normalizedPatterns = learnedColumns
            .Select(column => ColumnMappingRule.NormalizePattern(column.Header))
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(pattern => pattern, StringComparer.Ordinal)
            .ToList();
        var leases = new List<IAsyncDisposable>(normalizedPatterns.Count);
        try
        {
            foreach (var normalizedPattern in normalizedPatterns)
            {
                leases.Add(await _unitOfWork.AcquireOperationLockAsync(
                    BuildOperationLockKey(normalizedPattern),
                    cancellationToken));
            }

            return new CompositeOperationLockLease(leases);
        }
        catch
        {
            for (var index = leases.Count - 1; index >= 0; index--)
            {
                try
                {
                    await leases[index].DisposeAsync();
                }
                catch
                {
                    // 获取后续锁失败时保留原始异常，同时尽力释放已取得的锁。
                }
            }

            throw;
        }
    }

    private async Task<SmartConfigurationColumnLearningResult> ApplyColumnLearningAsync(
        int customerId,
        string pattern,
        string normalizedPattern,
        ColumnMappingTargetField targetField,
        CancellationToken cancellationToken)
    {
        if (await HasCoveringGlobalRuleAsync(
                normalizedPattern,
                targetField,
                cancellationToken))
        {
            await RemoveRedundantCustomerLearnedRulesAsync(
                normalizedPattern,
                targetField,
                cancellationToken);
            return new SmartConfigurationColumnLearningResult(false, false);
        }

        var learnedRuleCreated = await UpsertCustomerLearnedRuleAsync(
            customerId,
            pattern,
            targetField,
            cancellationToken);
        var globalRulePromoted = await PromoteGlobalRuleIfReadyAsync(
            pattern,
            targetField,
            cancellationToken);

        // 唯一索引仍是跨实例并发提升的最终裁决者。无论本请求是否为赢家，
        // 只要现在已有可安全覆盖的全局规则，就在同一匹配词锁内收敛客户副本。
        if (await HasCoveringGlobalRuleAsync(
                normalizedPattern,
                targetField,
                cancellationToken))
        {
            await RemoveRedundantCustomerLearnedRulesAsync(
                normalizedPattern,
                targetField,
                cancellationToken);
        }

        return new SmartConfigurationColumnLearningResult(
            learnedRuleCreated,
            globalRulePromoted);
    }

    private static string BuildOperationLockKey(string normalizedPattern) =>
        $"column-mapping-learning:{normalizedPattern}";

    private async Task<bool> UpsertCustomerLearnedRuleAsync(
        int customerId,
        string pattern,
        ColumnMappingTargetField targetField,
        CancellationToken cancellationToken)
    {
        var scopeKey = ColumnMappingRule.BuildScopeKey(customerId);
        var normalizedPattern = ColumnMappingRule.NormalizePattern(pattern);
        var existing = await _unitOfWork.ColumnMappingRules.Query(asNoTracking: false)
            .FirstOrDefaultAsync(rule =>
                rule.ScopeKey == scopeKey &&
                rule.TargetField == targetField &&
                rule.NormalizedPattern == normalizedPattern,
                cancellationToken);

        if (existing != null)
        {
            // 用户手工配置（含显式禁用）优先于自动学习，确认流程不得篡改其来源、
            // 匹配模式或启用状态。仅维护已经启用的 Learned 规则。
            if (!existing.Enabled || existing.Source != ColumnMappingRuleSource.Learned)
            {
                return false;
            }

            existing.Source = ColumnMappingRuleSource.Learned;
            existing.MatchMode = ColumnMappingMatchMode.Equals;
            existing.Enabled = true;
            existing.Priority = Math.Max(existing.Priority, 100);
            existing.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return false;
        }

        var candidate = new ColumnMappingRule
        {
            CustomerId = customerId,
            TargetField = targetField,
            MatchMode = ColumnMappingMatchMode.Equals,
            Pattern = pattern,
            Priority = 100,
            Enabled = true,
            Source = ColumnMappingRuleSource.Learned,
            CreatedAt = DateTime.UtcNow
        };
        candidate.RefreshUniqueIdentity();
        await _unitOfWork.ColumnMappingRules.AddAsync(candidate, cancellationToken);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            _unitOfWork.ColumnMappingRules.Remove(candidate);
            var concurrentWinner = await FindRuleByIdentityAsync(
                scopeKey,
                targetField,
                normalizedPattern,
                cancellationToken);
            if (concurrentWinner is null)
                throw;

            return false;
        }
    }

    private Task<bool> HasCoveringGlobalRuleAsync(
        string normalizedPattern,
        ColumnMappingTargetField targetField,
        CancellationToken cancellationToken) =>
        _unitOfWork.ColumnMappingRules.Query()
            .AnyAsync(rule =>
                rule.CustomerId == null &&
                rule.Enabled &&
                rule.TargetField == targetField &&
                rule.NormalizedPattern == normalizedPattern &&
                (rule.MatchMode == ColumnMappingMatchMode.Equals ||
                 rule.MatchMode == ColumnMappingMatchMode.Contains),
                cancellationToken);

    private async Task RemoveRedundantCustomerLearnedRulesAsync(
        string normalizedPattern,
        ColumnMappingTargetField targetField,
        CancellationToken cancellationToken)
    {
        var redundantRules = await _unitOfWork.ColumnMappingRules.Query(asNoTracking: false)
            .Where(rule =>
                rule.CustomerId != null &&
                rule.Enabled &&
                rule.Source == ColumnMappingRuleSource.Learned &&
                rule.MatchMode == ColumnMappingMatchMode.Equals &&
                rule.TargetField == targetField &&
                rule.NormalizedPattern == normalizedPattern)
            .ToListAsync(cancellationToken);
        if (redundantRules.Count == 0)
        {
            return;
        }

        _unitOfWork.ColumnMappingRules.RemoveRange(redundantRules);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> PromoteGlobalRuleIfReadyAsync(
        string pattern,
        ColumnMappingTargetField targetField,
        CancellationToken cancellationToken)
    {
        var scopeKey = ColumnMappingRule.BuildScopeKey(customerId: null);
        var normalizedPattern = ColumnMappingRule.NormalizePattern(pattern);
        var existingGlobal = await _unitOfWork.ColumnMappingRules.Query()
            .FirstOrDefaultAsync(rule =>
                rule.ScopeKey == scopeKey &&
                rule.NormalizedPattern == normalizedPattern,
                cancellationToken);
        if (existingGlobal is not null)
        {
            // 同一全局表头已经归属任意字段时都不再提升，防止学习结果跨客户污染。
            return false;
        }

        var learnedCustomerCount = await _unitOfWork.ColumnMappingRules.Query()
            .Where(rule =>
                rule.CustomerId != null &&
                rule.Source == ColumnMappingRuleSource.Learned &&
                rule.TargetField == targetField &&
                rule.NormalizedPattern == normalizedPattern &&
                rule.Enabled)
            .Select(rule => rule.CustomerId!.Value)
            .Distinct()
            .CountAsync(cancellationToken);
        var promotionThreshold = Math.Max(1, _options.GlobalRulePromotionCustomerThreshold);
        if (learnedCustomerCount < promotionThreshold)
        {
            return false;
        }

        var candidate = new ColumnMappingRule
        {
            CustomerId = null,
            TargetField = targetField,
            MatchMode = ColumnMappingMatchMode.Equals,
            Pattern = pattern,
            Priority = 80,
            Enabled = true,
            Source = ColumnMappingRuleSource.Learned,
            CreatedAt = DateTime.UtcNow
        };
        candidate.RefreshUniqueIdentity();
        await _unitOfWork.ColumnMappingRules.AddAsync(candidate, cancellationToken);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            _unitOfWork.ColumnMappingRules.Remove(candidate);
            // GlobalNormalizedPatternKey 的唯一约束负责跨实例裁决。同字段或其他
            // 字段的并发赢家都意味着本次无需再次提升。
            return false;
        }
    }

    private Task<ColumnMappingRule?> FindRuleByIdentityAsync(
        string scopeKey,
        ColumnMappingTargetField targetField,
        string normalizedPattern,
        CancellationToken cancellationToken) =>
        _unitOfWork.ColumnMappingRules.Query()
            .FirstOrDefaultAsync(rule =>
                rule.ScopeKey == scopeKey &&
                rule.TargetField == targetField &&
                rule.NormalizedPattern == normalizedPattern,
                cancellationToken);

    private sealed class CompositeOperationLockLease : IAsyncDisposable
    {
        private List<IAsyncDisposable>? _leases;

        public CompositeOperationLockLease(List<IAsyncDisposable> leases)
        {
            _leases = leases;
        }

        public async ValueTask DisposeAsync()
        {
            var leases = Interlocked.Exchange(ref _leases, null);
            if (leases is null)
            {
                return;
            }

            Exception? firstException = null;
            for (var index = leases.Count - 1; index >= 0; index--)
            {
                try
                {
                    await leases[index].DisposeAsync();
                }
                catch (Exception exception)
                {
                    firstException ??= exception;
                }
            }

            if (firstException is not null)
            {
                throw firstException;
            }
        }
    }
}

internal sealed record SmartConfigurationColumnLearningResult(
    bool LearnedRuleCreated,
    bool GlobalRulePromoted);

public sealed record SmartConfigurationLearningResult(
    int LearnedRuleCount,
    int PromotedGlobalRuleCount);
