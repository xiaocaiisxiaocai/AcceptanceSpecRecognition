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

    public async Task<SmartConfigurationLearningResult> ApplyLearningAsync(
        int customerId,
        string? tableName,
        string? tableKind,
        string? recommendation,
        IReadOnlyList<SmartConfigurationLearnedColumn> learnedColumns,
        CancellationToken cancellationToken)
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

            if (await UpsertCustomerLearnedRuleAsync(
                    customerId,
                    pattern,
                    learnedColumn.TargetField,
                    cancellationToken))
            {
                learnedRuleCount++;
            }

            if (await PromoteGlobalRuleIfReadyAsync(
                    pattern,
                    learnedColumn.TargetField,
                    cancellationToken))
            {
                promotedGlobalRuleCount++;
            }
        }

        return new SmartConfigurationLearningResult(
            learnedRuleCount,
            promotedGlobalRuleCount);
    }

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
}

public sealed record SmartConfigurationLearningResult(
    int LearnedRuleCount,
    int PromotedGlobalRuleCount);
