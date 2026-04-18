using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// Word 列映射规则配置接口。
/// </summary>
[Route("api/column-mapping-rules")]
[Authorize]
public class ColumnMappingRulesController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;

    public ColumnMappingRulesController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<ColumnMappingRuleDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ColumnMappingRuleDto>>>> GetAll([FromQuery] bool? enabled = null)
    {
        var rules = await _unitOfWork.ColumnMappingRules.GetAllAsync();
        if (enabled.HasValue)
        {
            rules = rules.Where(rule => rule.Enabled == enabled.Value).ToList();
        }

        var items = rules
            .OrderBy(rule => rule.TargetField)
            .ThenByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.Id)
            .Select(ToDto)
            .ToList();

        return Success(items);
    }

    [HttpGet("effective")]
    [ProducesResponseType(typeof(ApiResponse<List<ColumnMappingRuleDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ColumnMappingRuleDto>>>> GetEffective()
    {
        var rules = await _unitOfWork.ColumnMappingRules.GetEnabledOrderedAsync();
        return Success(rules.Select(ToDto).ToList());
    }

    [HttpPost]
    [AuditOperation("create", "column-mapping-rule")]
    [ProducesResponseType(typeof(ApiResponse<ColumnMappingRuleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ColumnMappingRuleDto>>> Create([FromBody] CreateColumnMappingRuleRequest request)
    {
        var pattern = request.Pattern?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return Error<ColumnMappingRuleDto>(400, "匹配词不能为空");
        }

        var validationError = ValidateRegexRule(request.MatchMode, pattern);
        if (validationError != null)
        {
            return Error<ColumnMappingRuleDto>(400, validationError);
        }

        var entity = new ColumnMappingRule
        {
            TargetField = request.TargetField,
            MatchMode = request.MatchMode,
            Pattern = pattern,
            Priority = request.Priority,
            Enabled = request.Enabled,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.ColumnMappingRules.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        return Success(ToDto(entity), "创建成功");
    }

    [HttpPut("{id:int}")]
    [AuditOperation("update", "column-mapping-rule")]
    [ProducesResponseType(typeof(ApiResponse<ColumnMappingRuleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ColumnMappingRuleDto>>> Update(
        int id,
        [FromBody] UpdateColumnMappingRuleRequest request)
    {
        var entity = await _unitOfWork.ColumnMappingRules.GetByIdAsync(id);
        if (entity == null)
        {
            return Error<ColumnMappingRuleDto>(400, "规则不存在");
        }

        var pattern = request.Pattern?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return Error<ColumnMappingRuleDto>(400, "匹配词不能为空");
        }

        var validationError = ValidateRegexRule(request.MatchMode, pattern);
        if (validationError != null)
        {
            return Error<ColumnMappingRuleDto>(400, validationError);
        }

        entity.TargetField = request.TargetField;
        entity.MatchMode = request.MatchMode;
        entity.Pattern = pattern;
        entity.Priority = request.Priority;
        entity.Enabled = request.Enabled;
        entity.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.ColumnMappingRules.Update(entity);
        await _unitOfWork.SaveChangesAsync();

        return Success(ToDto(entity), "更新成功");
    }

    [HttpDelete("{id:int}")]
    [AuditOperation("delete", "column-mapping-rule")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> Delete(int id)
    {
        var entity = await _unitOfWork.ColumnMappingRules.GetByIdAsync(id);
        if (entity == null)
        {
            return Error(400, "规则不存在");
        }

        _unitOfWork.ColumnMappingRules.Remove(entity);
        await _unitOfWork.SaveChangesAsync();
        return Success("删除成功");
    }

    private static string? ValidateRegexRule(ColumnMappingMatchMode matchMode, string pattern)
    {
        if (matchMode != ColumnMappingMatchMode.Regex)
        {
            return null;
        }

        try
        {
            _ = new Regex(pattern);
            return null;
        }
        catch (Exception ex)
        {
            return $"正则表达式无效: {ex.Message}";
        }
    }

    private static ColumnMappingRuleDto ToDto(ColumnMappingRule entity) => new()
    {
        Id = entity.Id,
        TargetField = entity.TargetField,
        MatchMode = entity.MatchMode,
        Pattern = entity.Pattern,
        Priority = entity.Priority,
        Enabled = entity.Enabled,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt
    };
}
