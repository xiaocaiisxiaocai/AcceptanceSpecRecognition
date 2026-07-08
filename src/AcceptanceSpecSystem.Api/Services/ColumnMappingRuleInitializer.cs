using AcceptanceSpecSystem.Core.Documents.Intelligence;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 启动期补齐内置表头字段识别词，避免空库无规则。
/// </summary>
public sealed class ColumnMappingRuleInitializer
{
    private readonly IUnitOfWork _unitOfWork;

    public ColumnMappingRuleInitializer(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<int> EnsureAsync(
        ColumnMappingTargetField? targetField = null,
        CancellationToken cancellationToken = default)
    {
        return await SeedMissingAsync(targetField, cancellationToken);
    }

    public async Task<int> RestoreMissingAsync(
        ColumnMappingTargetField? targetField = null,
        CancellationToken cancellationToken = default)
    {
        return await SeedMissingAsync(targetField, cancellationToken);
    }

    private async Task<int> SeedMissingAsync(
        ColumnMappingTargetField? targetField,
        CancellationToken cancellationToken)
    {
        var added = 0;
        foreach (var (columnType, words) in ColumnMappingRuleDefaults.GetAll())
        {
            var field = ToTargetField(columnType);
            if (!field.HasValue || (targetField.HasValue && field.Value != targetField.Value))
            {
                continue;
            }

            var existingPatterns = await _unitOfWork.ColumnMappingRules.Query()
                .Where(rule =>
                    rule.CustomerId == null &&
                    rule.TargetField == field.Value)
                .Select(rule => rule.Pattern)
                .ToListAsync(cancellationToken);
            var existing = existingPatterns
                .Select(pattern => pattern.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

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
                    CustomerId = null,
                    CreatedAt = DateTime.UtcNow
                }, cancellationToken);
                added++;
            }
        }

        if (added > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return added;
    }

    private static ColumnMappingTargetField? ToTargetField(ColumnType columnType) => columnType switch
    {
        ColumnType.Project => ColumnMappingTargetField.Project,
        ColumnType.Specification => ColumnMappingTargetField.Specification,
        ColumnType.Acceptance => ColumnMappingTargetField.Acceptance,
        ColumnType.Remark => ColumnMappingTargetField.Remark,
        _ => null
    };
}
