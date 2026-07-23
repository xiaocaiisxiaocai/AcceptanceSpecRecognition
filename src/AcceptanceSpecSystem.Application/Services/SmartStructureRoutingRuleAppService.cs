using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Application.Services;

public interface ISmartStructureRoutingRuleAppService
{
    Task<List<SmartStructureRoutingRuleDto>> GetAllAsync(bool? enabled, CancellationToken cancellationToken = default);
    Task<List<SmartStructureRoutingRuleDto>> GetEffectiveAsync(int? customerId, CancellationToken cancellationToken = default);
    Task<SmartStructureRoutingRuleDto> CreateAsync(CreateSmartStructureRoutingRuleRequest request, CancellationToken cancellationToken = default);
    Task<SmartStructureRoutingRuleDto> UpdateAsync(int id, UpdateSmartStructureRoutingRuleRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
public sealed class SmartStructureRoutingRuleAppService : ISmartStructureRoutingRuleAppService
{
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(200);
    private readonly IUnitOfWork _unitOfWork;

    public SmartStructureRoutingRuleAppService(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<List<SmartStructureRoutingRuleDto>> GetAllAsync(bool? enabled, CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.SmartStructureRoutingRules.Query()
            .Where(rule => rule.Source != SmartStructureRoutingRuleSource.Learned);
        if (enabled.HasValue)
            query = query.Where(rule => rule.Enabled == enabled.Value);
        return (await query.OrderByDescending(rule => rule.Priority)
                .ThenBy(rule => rule.TableKind).ThenBy(rule => rule.Id)
                .ToListAsync(cancellationToken))
            .Select(ToDto).ToList();
    }

    public async Task<List<SmartStructureRoutingRuleDto>> GetEffectiveAsync(int? customerId, CancellationToken cancellationToken = default)
    {
        var rules = await _unitOfWork.SmartStructureRoutingRules.GetEffectiveForCustomerAsync(customerId, cancellationToken);
        return rules.Select(ToDto).ToList();
    }

    public async Task<SmartStructureRoutingRuleDto> CreateAsync(CreateSmartStructureRoutingRuleRequest request, CancellationToken cancellationToken = default)
    {
        var value = Normalize(request);
        var entity = new SmartStructureRoutingRule
        {
            Name = value.Name, TableKind = value.TableKind, Recommendation = value.Recommendation,
            MatchScope = value.MatchScope, MatchMode = value.MatchMode, Pattern = value.Pattern,
            Weight = value.Weight, Priority = value.Priority, Enabled = value.Enabled,
            Source = value.Source, CustomerId = value.CustomerId, CreatedAt = DateTime.UtcNow
        };
        await _unitOfWork.SmartStructureRoutingRules.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task<SmartStructureRoutingRuleDto> UpdateAsync(int id, UpdateSmartStructureRoutingRuleRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.SmartStructureRoutingRules.GetByIdAsync(id, cancellationToken)
            ?? throw new ApplicationServiceException(400, "规则不存在");
        var value = Normalize(request);
        entity.Name = value.Name;
        entity.TableKind = value.TableKind;
        entity.Recommendation = value.Recommendation;
        entity.MatchScope = value.MatchScope;
        entity.MatchMode = value.MatchMode;
        entity.Pattern = value.Pattern;
        entity.Weight = value.Weight;
        entity.Priority = value.Priority;
        entity.Enabled = value.Enabled;
        entity.Source = value.Source;
        entity.CustomerId = value.CustomerId;
        entity.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.SmartStructureRoutingRules.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.SmartStructureRoutingRules.GetByIdAsync(id, cancellationToken)
            ?? throw new ApplicationServiceException(400, "规则不存在");
        _unitOfWork.SmartStructureRoutingRules.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static NormalizedRoutingRule Normalize(CreateSmartStructureRoutingRuleRequest request)
    {
        var name = Require(request.Name, "规则名称不能为空");
        var tableKind = Require(request.TableKind, "表格类型不能为空");
        var recommendation = Require(request.Recommendation, "推荐结果不能为空");
        var pattern = Require(request.Pattern, "匹配词不能为空");
        if (!Enum.TryParse<SmartStructureRoutingMatchScope>(request.MatchScope, true, out var scope))
            throw new ApplicationServiceException(400, $"匹配范围无效：{request.MatchScope}");
        if (!Enum.TryParse<SmartStructureRoutingMatchMode>(request.MatchMode, true, out var mode))
            throw new ApplicationServiceException(400, $"匹配方式无效：{request.MatchMode}");
        if (!Enum.TryParse<SmartStructureRoutingRuleSource>(request.Source, true, out var source))
            throw new ApplicationServiceException(400, $"规则来源无效：{request.Source}");
        if (source == SmartStructureRoutingRuleSource.Learned)
            throw new ApplicationServiceException(400, "学习型路由规则已停用，请使用手动或 AI 建议来源");
        if (mode == SmartStructureRoutingMatchMode.Regex)
        {
            try { _ = new Regex(pattern, RegexOptions.None, RegexMatchTimeout); }
            catch (Exception ex) { throw new ApplicationServiceException(400, $"正则表达式无效: {ex.Message}"); }
        }
        return new NormalizedRoutingRule(name, tableKind, recommendation, scope, mode, pattern,
            request.Weight, request.Priority, request.Enabled, source, request.CustomerId);
    }

    private static string Require(string? value, string message)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? throw new ApplicationServiceException(400, message)
            : normalized;
    }

    private static SmartStructureRoutingRuleDto ToDto(SmartStructureRoutingRule entity) => new()
    {
        Id = entity.Id, Name = entity.Name, TableKind = entity.TableKind,
        Recommendation = entity.Recommendation, MatchScope = entity.MatchScope.ToString(),
        MatchMode = entity.MatchMode.ToString(), Pattern = entity.Pattern, Weight = entity.Weight,
        Priority = entity.Priority, Enabled = entity.Enabled, Source = entity.Source.ToString(),
        CustomerId = entity.CustomerId, CreatedAt = entity.CreatedAt, UpdatedAt = entity.UpdatedAt
    };

    private sealed record NormalizedRoutingRule(
        string Name, string TableKind, string Recommendation,
        SmartStructureRoutingMatchScope MatchScope, SmartStructureRoutingMatchMode MatchMode,
        string Pattern, double Weight, int Priority, bool Enabled,
        SmartStructureRoutingRuleSource Source, int? CustomerId);
}
