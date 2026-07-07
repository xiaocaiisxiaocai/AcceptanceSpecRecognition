using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 智能结构识别表格路由规则配置接口。
/// </summary>
[Route("api/smart-structure-routing-rules")]
[Authorize]
public sealed class SmartStructureRoutingRulesController : BaseApiController
{
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(200);

    private readonly IUnitOfWork _unitOfWork;

    public SmartStructureRoutingRulesController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<SmartStructureRoutingRuleDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<SmartStructureRoutingRuleDto>>>> GetAll(
        [FromQuery] bool? enabled = null,
        CancellationToken cancellationToken = default)
    {
        var query = _unitOfWork.SmartStructureRoutingRules.Query()
            .Where(rule => rule.Source != SmartStructureRoutingRuleSource.Learned);
        if (enabled.HasValue)
        {
            query = query.Where(rule => rule.Enabled == enabled.Value);
        }

        var rules = await query
            .OrderByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.TableKind)
            .ThenBy(rule => rule.Id)
            .ToListAsync(cancellationToken);
        return Success(rules.Select(ToDto).ToList());
    }

    [HttpGet("effective")]
    [ProducesResponseType(typeof(ApiResponse<List<SmartStructureRoutingRuleDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<SmartStructureRoutingRuleDto>>>> GetEffective(
        [FromQuery] int? customerId = null,
        CancellationToken cancellationToken = default)
    {
        var rules = await _unitOfWork.SmartStructureRoutingRules.GetEffectiveForCustomerAsync(
            customerId,
            cancellationToken);
        return Success(rules.Select(ToDto).ToList());
    }

    [HttpPost]
    [AuditOperation("create", "smart-structure-routing-rule")]
    [ProducesResponseType(typeof(ApiResponse<SmartStructureRoutingRuleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SmartStructureRoutingRuleDto>>> Create(
        [FromBody] CreateSmartStructureRoutingRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeRequest(request);
        if (normalized.Error != null)
        {
            return Error<SmartStructureRoutingRuleDto>(400, normalized.Error);
        }

        var entity = new SmartStructureRoutingRule
        {
            Name = normalized.Name,
            TableKind = normalized.TableKind,
            Recommendation = normalized.Recommendation,
            MatchScope = normalized.MatchScope,
            MatchMode = normalized.MatchMode,
            Pattern = normalized.Pattern,
            Weight = normalized.Weight,
            Priority = normalized.Priority,
            Enabled = normalized.Enabled,
            Source = normalized.Source,
            CustomerId = normalized.CustomerId,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.SmartStructureRoutingRules.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Success(ToDto(entity), "创建成功");
    }

    [HttpPut("{id:int}")]
    [AuditOperation("update", "smart-structure-routing-rule")]
    [ProducesResponseType(typeof(ApiResponse<SmartStructureRoutingRuleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SmartStructureRoutingRuleDto>>> Update(
        int id,
        [FromBody] UpdateSmartStructureRoutingRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.SmartStructureRoutingRules.GetByIdAsync(id, cancellationToken);
        if (entity == null)
        {
            return Error<SmartStructureRoutingRuleDto>(400, "规则不存在");
        }

        var normalized = NormalizeRequest(request);
        if (normalized.Error != null)
        {
            return Error<SmartStructureRoutingRuleDto>(400, normalized.Error);
        }

        entity.Name = normalized.Name;
        entity.TableKind = normalized.TableKind;
        entity.Recommendation = normalized.Recommendation;
        entity.MatchScope = normalized.MatchScope;
        entity.MatchMode = normalized.MatchMode;
        entity.Pattern = normalized.Pattern;
        entity.Weight = normalized.Weight;
        entity.Priority = normalized.Priority;
        entity.Enabled = normalized.Enabled;
        entity.Source = normalized.Source;
        entity.CustomerId = normalized.CustomerId;
        entity.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.SmartStructureRoutingRules.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Success(ToDto(entity), "更新成功");
    }

    [HttpDelete("{id:int}")]
    [AuditOperation("delete", "smart-structure-routing-rule")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> Delete(
        int id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.SmartStructureRoutingRules.GetByIdAsync(id, cancellationToken);
        if (entity == null)
        {
            return Error(400, "规则不存在");
        }

        _unitOfWork.SmartStructureRoutingRules.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Success("删除成功");
    }

    private static NormalizedRoutingRuleRequest NormalizeRequest(CreateSmartStructureRoutingRuleRequest request)
    {
        var name = request.Name.Trim();
        var tableKind = request.TableKind.Trim();
        var recommendation = request.Recommendation.Trim();
        var pattern = request.Pattern.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return NormalizedRoutingRuleRequest.Fail("规则名称不能为空");
        }

        if (string.IsNullOrWhiteSpace(tableKind))
        {
            return NormalizedRoutingRuleRequest.Fail("表格类型不能为空");
        }

        if (string.IsNullOrWhiteSpace(recommendation))
        {
            return NormalizedRoutingRuleRequest.Fail("推荐结果不能为空");
        }

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return NormalizedRoutingRuleRequest.Fail("匹配词不能为空");
        }

        if (!Enum.TryParse<SmartStructureRoutingMatchScope>(request.MatchScope, ignoreCase: true, out var matchScope))
        {
            return NormalizedRoutingRuleRequest.Fail($"匹配范围无效：{request.MatchScope}");
        }

        if (!Enum.TryParse<SmartStructureRoutingMatchMode>(request.MatchMode, ignoreCase: true, out var matchMode))
        {
            return NormalizedRoutingRuleRequest.Fail($"匹配方式无效：{request.MatchMode}");
        }

        if (!Enum.TryParse<SmartStructureRoutingRuleSource>(request.Source, ignoreCase: true, out var source))
        {
            return NormalizedRoutingRuleRequest.Fail($"规则来源无效：{request.Source}");
        }

        if (matchMode == SmartStructureRoutingMatchMode.Regex)
        {
            try
            {
                _ = new Regex(pattern, RegexOptions.None, RegexMatchTimeout);
            }
            catch (Exception ex)
            {
                return NormalizedRoutingRuleRequest.Fail($"正则表达式无效: {ex.Message}");
            }
        }

        return new NormalizedRoutingRuleRequest
        {
            Name = name,
            TableKind = tableKind,
            Recommendation = recommendation,
            MatchScope = matchScope,
            MatchMode = matchMode,
            Pattern = pattern,
            Weight = request.Weight,
            Priority = request.Priority,
            Enabled = request.Enabled,
            Source = source,
            CustomerId = request.CustomerId
        };
    }

    private static SmartStructureRoutingRuleDto ToDto(SmartStructureRoutingRule entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        TableKind = entity.TableKind,
        Recommendation = entity.Recommendation,
        MatchScope = entity.MatchScope.ToString(),
        MatchMode = entity.MatchMode.ToString(),
        Pattern = entity.Pattern,
        Weight = entity.Weight,
        Priority = entity.Priority,
        Enabled = entity.Enabled,
        Source = entity.Source.ToString(),
        CustomerId = entity.CustomerId,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt
    };

    private sealed class NormalizedRoutingRuleRequest
    {
        public string? Error { get; init; }

        public string Name { get; init; } = string.Empty;

        public string TableKind { get; init; } = string.Empty;

        public string Recommendation { get; init; } = string.Empty;

        public SmartStructureRoutingMatchScope MatchScope { get; init; }

        public SmartStructureRoutingMatchMode MatchMode { get; init; }

        public string Pattern { get; init; } = string.Empty;

        public double Weight { get; init; }

        public int Priority { get; init; }

        public bool Enabled { get; init; }

        public SmartStructureRoutingRuleSource Source { get; init; }

        public int? CustomerId { get; init; }

        public static NormalizedRoutingRuleRequest Fail(string error) => new()
        {
            Error = error
        };
    }
}
