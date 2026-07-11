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
        var added = 0;
        foreach (var (columnType, words) in ColumnMappingRuleDefaults.GetAll())
        {
            var field = ToTargetField(columnType);
            if (!field.HasValue || (targetField.HasValue && field.Value != targetField.Value))
                continue;

            var patterns = await _unitOfWork.ColumnMappingRules.Query()
                .Where(rule => rule.CustomerId == null && rule.TargetField == field.Value)
                .Select(rule => rule.Pattern)
                .ToListAsync(cancellationToken);
            var existing = patterns.Select(pattern => pattern.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var word in words.Where(word => !existing.Contains(word)))
            {
                await _unitOfWork.ColumnMappingRules.AddAsync(new ColumnMappingRule
                {
                    TargetField = field.Value,
                    MatchMode = ColumnMappingMatchMode.Contains,
                    Pattern = word,
                    Priority = 0,
                    Enabled = true,
                    Source = ColumnMappingRuleSource.Builtin,
                    CreatedAt = DateTime.UtcNow
                }, cancellationToken);
                added++;
            }
        }

        if (added > 0)
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        return added;
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
        await _unitOfWork.ColumnMappingRules.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
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
        await _unitOfWork.SaveChangesAsync(cancellationToken);
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
        var normalized = pattern.ToLower();
        var exists = await _unitOfWork.ColumnMappingRules.Query().AnyAsync(rule =>
            (!exceptId.HasValue || rule.Id != exceptId.Value) &&
            rule.TargetField == targetField && rule.CustomerId == customerId &&
            rule.Pattern.Trim().ToLower() == normalized, cancellationToken);
        if (exists)
            throw new ApplicationServiceException(400, "同一范围下已存在相同字段和匹配词的列映射规则");
    }

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
