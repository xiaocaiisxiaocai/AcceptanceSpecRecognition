using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 匹配知识配置管理接口。
/// </summary>
[Route("api/matching-knowledge")]
[Authorize]
public class MatchingKnowledgeController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly MatchingKnowledgeBootstrapper _bootstrapper;
    private readonly MatchingKnowledgeOptions _defaultOptions;

    public MatchingKnowledgeController(
        IUnitOfWork unitOfWork,
        MatchingKnowledgeBootstrapper bootstrapper,
        IOptions<MatchingKnowledgeOptions> defaultOptions)
    {
        _unitOfWork = unitOfWork;
        _bootstrapper = bootstrapper;
        _defaultOptions = defaultOptions.Value ?? new MatchingKnowledgeOptions();
    }

    /// <summary>
    /// 获取当前生效的匹配知识配置。
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<MatchingKnowledgeLayerDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<MatchingKnowledgeLayerDto>>> Get()
    {
        await _bootstrapper.EnsureInitializedAsync();
        var entity = await _unitOfWork.MatchingKnowledgeConfigs.GetConfigAsync();
        if (entity == null)
        {
            return Error<MatchingKnowledgeLayerDto>(500, "匹配知识初始化失败");
        }

        return Success(MatchingKnowledgeComposition.ToDto(entity));
    }

    /// <summary>
    /// 保存当前匹配知识配置。
    /// </summary>
    [HttpPut]
    [AuditOperation("update", "matching-knowledge")]
    [ProducesResponseType(typeof(ApiResponse<MatchingKnowledgeLayerDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<MatchingKnowledgeLayerDto>>> Put([FromBody] UpdateMatchingKnowledgeRequest request)
    {
        var entity = MatchingKnowledgeComposition.ToEntity(MatchingKnowledgeComposition.NormalizeRequest(request));

        await _unitOfWork.MatchingKnowledgeConfigs.SaveConfigAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        var saved = await _unitOfWork.MatchingKnowledgeConfigs.GetConfigAsync();
        return Success(MatchingKnowledgeComposition.ToDto(saved), "保存成功");
    }

    /// <summary>
    /// 清空当前匹配知识配置。
    /// </summary>
    [HttpPost("clear")]
    [AuditOperation("clear", "matching-knowledge")]
    [ProducesResponseType(typeof(ApiResponse<MatchingKnowledgeLayerDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<MatchingKnowledgeLayerDto>>> Clear()
    {
        var entity = MatchingKnowledgeComposition.CreateEmptyEntity();
        await _unitOfWork.MatchingKnowledgeConfigs.SaveConfigAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        var saved = await _unitOfWork.MatchingKnowledgeConfigs.GetConfigAsync();
        return Success(MatchingKnowledgeComposition.ToDto(saved), "已清空当前配置");
    }

    /// <summary>
    /// 恢复默认种子匹配知识配置。
    /// </summary>
    [HttpPost("restore-defaults")]
    [AuditOperation("reset", "matching-knowledge")]
    [ProducesResponseType(typeof(ApiResponse<MatchingKnowledgeLayerDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<MatchingKnowledgeLayerDto>>> RestoreDefaults()
    {
        var entity = MatchingKnowledgeComposition.CreateSeedEntity(_defaultOptions);
        await _unitOfWork.MatchingKnowledgeConfigs.SaveConfigAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        var saved = await _unitOfWork.MatchingKnowledgeConfigs.GetConfigAsync();
        return Success(MatchingKnowledgeComposition.ToDto(saved), "已恢复默认配置");
    }
}
