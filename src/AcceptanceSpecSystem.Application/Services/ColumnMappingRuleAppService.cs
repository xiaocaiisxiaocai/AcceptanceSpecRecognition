using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Core.Documents.Intelligence;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Application.Services;

public interface IColumnMappingRuleAppService
{
    Task<List<ColumnMappingRuleDto>> GetAllAsync(bool? enabled, CancellationToken cancellationToken = default);
    Task<List<ColumnMappingRuleDto>> GetEffectiveAsync(int? customerId, CancellationToken cancellationToken = default);
    Task<int> RestoreDefaultsAsync(ColumnMappingTargetField? targetField, CancellationToken cancellationToken = default);
    Task<ColumnMappingRuleDto> CreateAsync(CreateColumnMappingRuleRequest request, CancellationToken cancellationToken = default);
    Task<ColumnMappingRuleDto> UpdateAsync(int id, UpdateColumnMappingRuleRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
public sealed class ColumnMappingRuleAppService : IColumnMappingRuleAppService
{
    private readonly IUnitOfWork _unitOfWork;

    public ColumnMappingRuleAppService(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<List<ColumnMappingRuleDto>> GetAllAsync(bool? enabled, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.ColumnMappingRules.Query();
        if (enabled.HasValue)
            query = query.Where(rule => rule.Enabled == enabled.Value);

        var rules = await query.OrderBy(rule => rule.TargetField)
            .ThenByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.Id)
            .ToListAsync(cancellationToken);
        return ColumnMappingRuleDeduplicator.ForConfigurationList(rules).Select(ToDto).ToList();
    }

    public async Task<List<ColumnMappingRuleDto>> GetEffectiveAsync(int? customerId, CancellationToken cancellationToken = default)
    {
        var rules = await _unitOfWork.ColumnMappingRules.GetEffectiveForCustomerAsync(customerId);
        cancellationToken.ThrowIfCancellationRequested();
        return rules.Select(ToDto).ToList();
    }

    public async Task<int> RestoreDefaultsAsync(ColumnMappingTargetField? targetField, CancellationToken cancellationToken = default)
    {
        await RepairNormalizedIdentitiesAsync(cancellationToken);
        var added = 0;
        foreach (var (columnType, words) in ColumnMappingRuleDefaults.GetAll())
        {
            var field = ToTargetField(columnType);
            if (!field.HasValue || (targetField.HasValue && field.Value != targetField.Value))
                continue;

            var scopeKey = ColumnMappingRule.BuildScopeKey(customerId: null);
            var patterns = await _unitOfWork.ColumnMappingRules.Query()
                .Where(rule => rule.ScopeKey == scopeKey && rule.TargetField == field.Value)
                .Select(rule => rule.NormalizedPattern)
                .ToListAsync(cancellationToken);
            var existing = patterns.ToHashSet(StringComparer.Ordinal);
            foreach (var word in words)
            {
                var normalizedPattern = ColumnMappingRule.NormalizePattern(word);
                if (existing.Contains(normalizedPattern))
                    continue;

                var belongsToAnotherGlobalField = await _unitOfWork.ColumnMappingRules.Query()
                    .AnyAsync(rule =>
                        rule.ScopeKey == ColumnMappingRule.GlobalScopeKey &&
                        rule.NormalizedPattern == normalizedPattern &&
                        rule.TargetField != field.Value,
                        cancellationToken);
                if (belongsToAnotherGlobalField)
                    continue;

                var entity = new ColumnMappingRule
                {
                    TargetField = field.Value,
                    MatchMode = ColumnMappingMatchMode.Contains,
                    Pattern = word,
                    Priority = 0,
                    Enabled = true,
                    Source = ColumnMappingRuleSource.Builtin,
                    CreatedAt = DateTime.UtcNow
                };
                entity.RefreshUniqueIdentity();
                await _unitOfWork.ColumnMappingRules.AddAsync(entity, cancellationToken);
                try
                {
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    existing.Add(normalizedPattern);
                    added++;
                }
                catch (DbUpdateException)
                {
                    _unitOfWork.ColumnMappingRules.Remove(entity);
                    var concurrentWinnerExists = await RuleIdentityQuery(
                            scopeKey,
                            field.Value,
                            normalizedPattern)
                        .AnyAsync(cancellationToken);
                    var conflictingGlobalExists = concurrentWinnerExists ||
                        await _unitOfWork.ColumnMappingRules.Query()
                            .AnyAsync(rule =>
                                rule.ScopeKey == ColumnMappingRule.GlobalScopeKey &&
                                rule.NormalizedPattern == normalizedPattern,
                                cancellationToken);
                    if (!conflictingGlobalExists)
                        throw;

                    existing.Add(normalizedPattern);
                }
            }
        }
        return added;
    }

    private async Task RepairNormalizedIdentitiesAsync(CancellationToken cancellationToken)
    {
        var rules = await _unitOfWork.ColumnMappingRules.Query(asNoTracking: false)
            .OrderBy(rule => rule.Id)
            .ToListAsync(cancellationToken);
        if (rules.Count == 0)
        {
            return;
        }

        static int SourceRank(ColumnMappingRuleSource source) => source switch
        {
            ColumnMappingRuleSource.Manual => 3,
            ColumnMappingRuleSource.Learned => 2,
            ColumnMappingRuleSource.Builtin => 1,
            _ => 0
        };

        static ColumnMappingRule PickWinner(IEnumerable<ColumnMappingRule> candidates) => candidates
            .OrderByDescending(rule => rule.Enabled)
            .ThenByDescending(rule => SourceRank(rule.Source))
            .ThenByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.Id)
            .First();

        var losers = new HashSet<ColumnMappingRule>();
        foreach (var group in rules.GroupBy(rule => (
                     Scope: ColumnMappingRule.BuildScopeKey(rule.CustomerId),
                     rule.TargetField,
                     Pattern: ColumnMappingRule.NormalizePattern(rule.Pattern))))
        {
            var winner = PickWinner(group);
            foreach (var duplicate in group.Where(rule => !ReferenceEquals(rule, winner)))
            {
                losers.Add(duplicate);
            }
        }

        foreach (var group in rules
                     .Where(rule => rule.CustomerId is null && !losers.Contains(rule))
                     .GroupBy(rule => ColumnMappingRule.NormalizePattern(rule.Pattern)))
        {
            var winner = PickWinner(group);
            foreach (var conflict in group.Where(rule => !ReferenceEquals(rule, winner)))
            {
                losers.Add(conflict);
            }
        }

        if (losers.Count > 0)
        {
            _unitOfWork.ColumnMappingRules.RemoveRange(losers);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var changed = false;
        foreach (var rule in rules.Where(rule => !losers.Contains(rule)))
        {
            var expectedScope = ColumnMappingRule.BuildScopeKey(rule.CustomerId);
            var expectedPattern = ColumnMappingRule.NormalizePattern(rule.Pattern);
            var expectedGlobal = rule.CustomerId.HasValue ? null : expectedPattern;
            if (rule.ScopeKey == expectedScope &&
                rule.NormalizedPattern == expectedPattern &&
                rule.GlobalNormalizedPatternKey == expectedGlobal)
            {
                continue;
            }

            rule.RefreshUniqueIdentity();
            _unitOfWork.ColumnMappingRules.Update(rule);
            changed = true;
        }

        if (changed)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<ColumnMappingRuleDto> CreateAsync(CreateColumnMappingRuleRequest request, CancellationToken cancellationToken = default)
    {
        var pattern = ValidateAndNormalize(request.MatchMode, request.Pattern);
        await EnsureUniqueAsync(request.TargetField, request.CustomerId, pattern, null, cancellationToken);
        var entity = new ColumnMappingRule
        {
            TargetField = request.TargetField,
            MatchMode = request.MatchMode,
            Pattern = pattern,
            Priority = request.Priority,
            Enabled = request.Enabled,
            Source = request.Source,
            CustomerId = request.CustomerId,
            CreatedAt = DateTime.UtcNow
        };
        entity.RefreshUniqueIdentity();
        await _unitOfWork.ColumnMappingRules.AddAsync(entity, cancellationToken);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(entity);
        }
        catch (DbUpdateException)
        {
            _unitOfWork.ColumnMappingRules.Remove(entity);
            var concurrentWinner = await RuleIdentityQuery(
                    entity.ScopeKey,
                    entity.TargetField,
                    entity.NormalizedPattern)
                .FirstOrDefaultAsync(cancellationToken);
            if (concurrentWinner is null && entity.CustomerId is null)
            {
                var conflictingGlobal = await FindGlobalPatternAsync(
                    entity.NormalizedPattern,
                    cancellationToken);
                if (conflictingGlobal is not null)
                    throw new ApplicationServiceException(409, "该全局匹配词已映射到其他目标字段");
            }
            if (concurrentWinner is null)
                throw;

            return ToDto(concurrentWinner);
        }
    }

    public async Task<ColumnMappingRuleDto> UpdateAsync(int id, UpdateColumnMappingRuleRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.ColumnMappingRules.GetByIdAsync(id, cancellationToken)
            ?? throw new ApplicationServiceException(400, "规则不存在");
        var pattern = ValidateAndNormalize(request.MatchMode, request.Pattern);
        await EnsureUniqueAsync(request.TargetField, request.CustomerId, pattern, id, cancellationToken);
        entity.TargetField = request.TargetField;
        entity.MatchMode = request.MatchMode;
        entity.Pattern = pattern;
        entity.Priority = request.Priority;
        entity.Enabled = request.Enabled;
        entity.Source = request.Source;
        entity.CustomerId = request.CustomerId;
        entity.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.ColumnMappingRules.Update(entity);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // 预检查与写入之间可能有其他实例抢先提交，数据库唯一键是最终裁决者。
            var identityConflict = await RuleIdentityQuery(
                    entity.ScopeKey,
                    entity.TargetField,
                    entity.NormalizedPattern)
                .AnyAsync(rule => rule.Id != entity.Id, cancellationToken);
            var globalConflict = entity.CustomerId is null &&
                await _unitOfWork.ColumnMappingRules.Query()
                    .AnyAsync(rule =>
                        rule.ScopeKey == ColumnMappingRule.GlobalScopeKey &&
                        rule.NormalizedPattern == entity.NormalizedPattern &&
                        rule.Id != entity.Id,
                        cancellationToken);
            if (!identityConflict && !globalConflict)
                throw;

            throw new ApplicationServiceException(409, "列映射规则已被其他请求更新为冲突配置，请刷新后重试");
        }
        return ToDto(entity);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.ColumnMappingRules.GetByIdAsync(id, cancellationToken)
            ?? throw new ApplicationServiceException(400, "规则不存在");
        _unitOfWork.ColumnMappingRules.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureUniqueAsync(ColumnMappingTargetField targetField, int? customerId, string pattern, int? exceptId, CancellationToken cancellationToken)
    {
        var scopeKey = ColumnMappingRule.BuildScopeKey(customerId);
        var normalizedPattern = ColumnMappingRule.NormalizePattern(pattern);
        if (!customerId.HasValue)
        {
            var conflictingGlobal = await _unitOfWork.ColumnMappingRules.Query()
                .AnyAsync(rule =>
                    rule.ScopeKey == ColumnMappingRule.GlobalScopeKey &&
                    rule.NormalizedPattern == normalizedPattern &&
                    rule.TargetField != targetField &&
                    (!exceptId.HasValue || rule.Id != exceptId.Value),
                    cancellationToken);
            if (conflictingGlobal)
                throw new ApplicationServiceException(409, "该全局匹配词已映射到其他目标字段");
        }

        var exists = await RuleIdentityQuery(scopeKey, targetField, normalizedPattern)
            .AnyAsync(rule => !exceptId.HasValue || rule.Id != exceptId.Value, cancellationToken);
        if (exists)
            throw new ApplicationServiceException(400, "同一范围下已存在相同字段和匹配词的列映射规则");
    }

    private Task<ColumnMappingRule?> FindGlobalPatternAsync(
        string normalizedPattern,
        CancellationToken cancellationToken) =>
        _unitOfWork.ColumnMappingRules.Query()
            .FirstOrDefaultAsync(rule =>
                rule.ScopeKey == ColumnMappingRule.GlobalScopeKey &&
                rule.NormalizedPattern == normalizedPattern,
                cancellationToken);

    private IQueryable<ColumnMappingRule> RuleIdentityQuery(
        string scopeKey,
        ColumnMappingTargetField targetField,
        string normalizedPattern) =>
        _unitOfWork.ColumnMappingRules.Query().Where(rule =>
            rule.ScopeKey == scopeKey &&
            rule.TargetField == targetField &&
            rule.NormalizedPattern == normalizedPattern);

    private static string ValidateAndNormalize(ColumnMappingMatchMode matchMode, string? value)
    {
        var pattern = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(pattern))
            throw new ApplicationServiceException(400, "匹配词不能为空");
        if (matchMode == ColumnMappingMatchMode.Regex)
        {
            try { _ = new Regex(pattern); }
            catch (Exception ex) { throw new ApplicationServiceException(400, $"正则表达式无效: {ex.Message}"); }
        }
        return pattern;
    }

    private static ColumnMappingRuleDto ToDto(ColumnMappingRule entity) => new()
    {
        Id = entity.Id, TargetField = entity.TargetField, MatchMode = entity.MatchMode,
        Pattern = entity.Pattern, Priority = entity.Priority, Enabled = entity.Enabled,
        Source = entity.Source, CustomerId = entity.CustomerId, CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt
    };

    private static ColumnMappingTargetField? ToTargetField(ColumnType columnType) => columnType switch
    {
        ColumnType.Project => ColumnMappingTargetField.Project,
        ColumnType.Specification => ColumnMappingTargetField.Specification,
        ColumnType.Acceptance => ColumnMappingTargetField.Acceptance,
        ColumnType.Remark => ColumnMappingTargetField.Remark,
        _ => null
    };
}
