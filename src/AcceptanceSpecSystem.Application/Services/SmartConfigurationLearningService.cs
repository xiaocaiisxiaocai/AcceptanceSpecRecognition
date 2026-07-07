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

    internal async Task<SmartConfigurationLearningResult> ApplyLearningAsync(
        int customerId,
        string? tableName,
        string? tableKind,
        string? recommendation,
        IReadOnlyList<SmartConfigurationLearnedColumn> learnedColumns,
        CancellationToken cancellationToken)
    {
        var learnedRuleCount = 0;
        var learnedRoutingRuleCount = await UpsertCustomerLearnedRoutingRuleAsync(
            customerId,
            tableName,
            tableKind,
            recommendation,
            cancellationToken)
            ? 1
            : 0;
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

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            if (await PromoteGlobalRuleIfReadyAsync(
                    pattern,
                    learnedColumn.TargetField,
                    cancellationToken))
            {
                promotedGlobalRuleCount++;
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return new SmartConfigurationLearningResult(
            learnedRuleCount,
            learnedRoutingRuleCount,
            promotedGlobalRuleCount);
    }

    private async Task<bool> UpsertCustomerLearnedRoutingRuleAsync(
        int customerId,
        string? tableName,
        string? tableKind,
        string? recommendation,
        CancellationToken cancellationToken)
    {
        var pattern = tableName?.Trim();
        var normalizedKind = tableKind?.Trim();
        var normalizedRecommendation = recommendation?.Trim();
        if (string.IsNullOrWhiteSpace(pattern) ||
            string.IsNullOrWhiteSpace(normalizedKind) ||
            string.IsNullOrWhiteSpace(normalizedRecommendation) ||
            IsGenericTableName(pattern) ||
            string.Equals(normalizedKind, "Unknown", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedRecommendation, "Skip", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var existingManual = await _unitOfWork.SmartStructureRoutingRules.Query()
            .AnyAsync(rule =>
                rule.CustomerId == customerId &&
                rule.MatchScope == SmartStructureRoutingMatchScope.TableName &&
                rule.MatchMode == SmartStructureRoutingMatchMode.Equals &&
                rule.Pattern == pattern &&
                rule.Source == SmartStructureRoutingRuleSource.Manual,
                cancellationToken);
        if (existingManual)
        {
            return false;
        }

        var existing = await _unitOfWork.SmartStructureRoutingRules.Query(asNoTracking: false)
            .FirstOrDefaultAsync(rule =>
                rule.CustomerId == customerId &&
                rule.MatchScope == SmartStructureRoutingMatchScope.TableName &&
                rule.MatchMode == SmartStructureRoutingMatchMode.Equals &&
                rule.Pattern == pattern &&
                rule.Source == SmartStructureRoutingRuleSource.Learned,
                cancellationToken);
        if (existing != null)
        {
            existing.Name = $"学习规则-{pattern}";
            existing.TableKind = normalizedKind;
            existing.Recommendation = normalizedRecommendation;
            existing.Weight = Math.Max(existing.Weight, 1);
            existing.Priority = Math.Max(existing.Priority, 100);
            existing.Enabled = true;
            existing.UpdatedAt = DateTime.UtcNow;
            return false;
        }

        await _unitOfWork.SmartStructureRoutingRules.AddAsync(new SmartStructureRoutingRule
        {
            CustomerId = customerId,
            Name = $"学习规则-{pattern}",
            TableKind = normalizedKind,
            Recommendation = normalizedRecommendation,
            MatchScope = SmartStructureRoutingMatchScope.TableName,
            MatchMode = SmartStructureRoutingMatchMode.Equals,
            Pattern = pattern,
            Weight = 1,
            Priority = 100,
            Enabled = true,
            Source = SmartStructureRoutingRuleSource.Learned,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);
        return true;
    }

    private static bool IsGenericTableName(string tableName)
    {
        var normalized = new string(tableName
            .Where(ch => !char.IsWhiteSpace(ch) && ch != '_' && ch != '-')
            .Select(char.ToLowerInvariant)
            .ToArray());
        return IsPrefixFollowedByOptionalDigits(normalized, "sheet") ||
               IsPrefixFollowedByOptionalDigits(normalized, "worksheet") ||
               IsPrefixFollowedByOptionalDigits(normalized, "工作表") ||
               IsPrefixFollowedByOptionalDigits(normalized, "表");
    }

    private static bool IsPrefixFollowedByOptionalDigits(string value, string prefix)
    {
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return value.Length == prefix.Length ||
               value[prefix.Length..].All(char.IsDigit);
    }

    private async Task<bool> UpsertCustomerLearnedRuleAsync(
        int customerId,
        string pattern,
        ColumnMappingTargetField targetField,
        CancellationToken cancellationToken)
    {
        var existing = await _unitOfWork.ColumnMappingRules.Query(asNoTracking: false)
            .FirstOrDefaultAsync(rule =>
                rule.CustomerId == customerId &&
                rule.TargetField == targetField &&
                rule.Pattern == pattern,
                cancellationToken);

        if (existing != null)
        {
            existing.Source = ColumnMappingRuleSource.Learned;
            existing.MatchMode = ColumnMappingMatchMode.Equals;
            existing.Enabled = true;
            existing.Priority = Math.Max(existing.Priority, 100);
            existing.UpdatedAt = DateTime.UtcNow;
            return false;
        }

        await _unitOfWork.ColumnMappingRules.AddAsync(new ColumnMappingRule
        {
            CustomerId = customerId,
            TargetField = targetField,
            MatchMode = ColumnMappingMatchMode.Equals,
            Pattern = pattern,
            Priority = 100,
            Enabled = true,
            Source = ColumnMappingRuleSource.Learned,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);
        return true;
    }

    private async Task<bool> PromoteGlobalRuleIfReadyAsync(
        string pattern,
        ColumnMappingTargetField targetField,
        CancellationToken cancellationToken)
    {
        var hasGlobal = await _unitOfWork.ColumnMappingRules.Query()
            .AnyAsync(rule =>
                rule.CustomerId == null &&
                rule.TargetField == targetField &&
                rule.Pattern == pattern &&
                rule.Enabled,
                cancellationToken);
        if (hasGlobal)
        {
            return false;
        }

        var learnedCustomerCount = await _unitOfWork.ColumnMappingRules.Query()
            .Where(rule =>
                rule.CustomerId != null &&
                rule.Source == ColumnMappingRuleSource.Learned &&
                rule.TargetField == targetField &&
                rule.Pattern == pattern &&
                rule.Enabled)
            .Select(rule => rule.CustomerId!.Value)
            .Distinct()
            .CountAsync(cancellationToken);
        var promotionThreshold = Math.Max(1, _options.GlobalRulePromotionCustomerThreshold);
        if (learnedCustomerCount < promotionThreshold)
        {
            return false;
        }

        await _unitOfWork.ColumnMappingRules.AddAsync(new ColumnMappingRule
        {
            CustomerId = null,
            TargetField = targetField,
            MatchMode = ColumnMappingMatchMode.Equals,
            Pattern = pattern,
            Priority = 80,
            Enabled = true,
            Source = ColumnMappingRuleSource.Learned,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);
        return true;
    }
}

internal sealed record SmartConfigurationLearningResult(
    int LearnedRuleCount,
    int LearnedRoutingRuleCount,
    int PromotedGlobalRuleCount);
