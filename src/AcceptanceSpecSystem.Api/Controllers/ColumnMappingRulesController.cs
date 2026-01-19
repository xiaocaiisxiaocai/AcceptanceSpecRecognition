using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 导入列映射规则 API（全局）
/// </summary>
[Route("api/column-mapping-rules")]
public class ColumnMappingRulesController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;

    public ColumnMappingRulesController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// 获取规则列表
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<ColumnMappingRuleDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ColumnMappingRuleDto>>>> GetAll([FromQuery] bool? enabled = null)
    {
        var all = await _unitOfWork.ColumnMappingRules.GetAllAsync();
        if (enabled.HasValue)
        {
            all = all.Where(r => r.Enabled == enabled.Value).ToList();
        }

        var items = all
            .OrderBy(r => r.TargetField)
            .ThenByDescending(r => r.Priority)
            .ThenBy(r => r.Id)
            .Select(ToDto)
            .ToList();

        return Success(items);
    }

    /// <summary>
    /// 获取“当前生效规则”（仅全局一套）
    /// </summary>
    [HttpGet("effective")]
    [ProducesResponseType(typeof(ApiResponse<List<ColumnMappingRuleDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<ColumnMappingRuleDto>>>> GetEffective()
    {
        var items = await _unitOfWork.ColumnMappingRules.GetEnabledOrderedAsync();
        return Success(items.Select(ToDto).ToList());
    }

    /// <summary>
    /// 新增规则
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ColumnMappingRuleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ColumnMappingRuleDto>>> Create([FromBody] CreateColumnMappingRuleRequest request)
    {
        var pattern = request.Pattern?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(pattern))
            return Error<ColumnMappingRuleDto>(400, "匹配词不能为空");

        // Regex 基础校验：避免保存后前端/后端应用时报错
        if (request.MatchMode == ColumnMappingMatchMode.Regex)
        {
            try { _ = new Regex(pattern); }
            catch (Exception ex) { return Error<ColumnMappingRuleDto>(400, $"正则表达式无效: {ex.Message}"); }
        }

        var entity = new ColumnMappingRule
        {
            TargetField = request.TargetField,
            MatchMode = request.MatchMode,
            Pattern = pattern,
            Priority = request.Priority,
            Enabled = request.Enabled,
            CreatedAt = DateTime.Now
        };

        await _unitOfWork.ColumnMappingRules.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        return Success(ToDto(entity), "创建成功");
    }

    /// <summary>
    /// 更新规则
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ApiResponse<ColumnMappingRuleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<ColumnMappingRuleDto>>> Update(int id, [FromBody] UpdateColumnMappingRuleRequest request)
    {
        var entity = await _unitOfWork.ColumnMappingRules.GetByIdAsync(id);
        if (entity == null)
            return Error<ColumnMappingRuleDto>(400, "规则不存在");

        var pattern = request.Pattern?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(pattern))
            return Error<ColumnMappingRuleDto>(400, "匹配词不能为空");

        if (request.MatchMode == ColumnMappingMatchMode.Regex)
        {
            try { _ = new Regex(pattern); }
            catch (Exception ex) { return Error<ColumnMappingRuleDto>(400, $"正则表达式无效: {ex.Message}"); }
        }

        entity.TargetField = request.TargetField;
        entity.MatchMode = request.MatchMode;
        entity.Pattern = pattern;
        entity.Priority = request.Priority;
        entity.Enabled = request.Enabled;
        entity.UpdatedAt = DateTime.Now;

        _unitOfWork.ColumnMappingRules.Update(entity);
        await _unitOfWork.SaveChangesAsync();

        return Success(ToDto(entity), "更新成功");
    }

    /// <summary>
    /// 删除规则
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> Delete(int id)
    {
        var entity = await _unitOfWork.ColumnMappingRules.GetByIdAsync(id);
        if (entity == null)
            return Error(400, "规则不存在");

        _unitOfWork.ColumnMappingRules.Remove(entity);
        await _unitOfWork.SaveChangesAsync();
        return Success("删除成功");
    }

    private static ColumnMappingRuleDto ToDto(ColumnMappingRule e) => new()
    {
        Id = e.Id,
        TargetField = e.TargetField,
        MatchMode = e.MatchMode,
        Pattern = e.Pattern,
        Priority = e.Priority,
        Enabled = e.Enabled,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };
}

