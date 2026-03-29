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
    [ProducesResponseType(typeof(ApiResponse<MatchingKnowledgeViewDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<MatchingKnowledgeViewDto>>> Get()
    {
        await _bootstrapper.EnsureInitializedAsync();
        var entity = await _unitOfWork.MatchingKnowledgeConfigs.GetConfigAsync();
        if (entity == null)
        {
            return Error<MatchingKnowledgeViewDto>(500, "匹配知识初始化失败");
        }

        return Success(MatchingKnowledgeComposition.BuildView(entity, _defaultOptions));
    }

    /// <summary>
    /// 保存当前匹配知识配置。
    /// </summary>
    [HttpPut]
    [AuditOperation("update", "matching-knowledge")]
    [ProducesResponseType(typeof(ApiResponse<MatchingKnowledgeViewDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<MatchingKnowledgeViewDto>>> Put([FromBody] UpdateMatchingKnowledgeRequest request)
    {
        var builtIn = MatchingKnowledgeComposition.CreateBuiltInLayer(_defaultOptions);
        var custom = MatchingKnowledgeComposition.FilterBuiltInDuplicates(
            MatchingKnowledgeComposition.NormalizeRequest(request),
            builtIn);
        var entity = MatchingKnowledgeComposition.ToEntity(custom);

        await _unitOfWork.MatchingKnowledgeConfigs.SaveConfigAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        var saved = await _unitOfWork.MatchingKnowledgeConfigs.GetConfigAsync();
        return Success(MatchingKnowledgeComposition.BuildView(saved, _defaultOptions), "保存成功");
    }

    /// <summary>
    /// 重置为系统默认匹配知识配置。
    /// </summary>
    [HttpPost("reset")]
    [AuditOperation("reset", "matching-knowledge")]
    [ProducesResponseType(typeof(ApiResponse<MatchingKnowledgeViewDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<MatchingKnowledgeViewDto>>> Reset()
    {
        var entity = MatchingKnowledgeComposition.CreateEmptyEntity();
        await _unitOfWork.MatchingKnowledgeConfigs.SaveConfigAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        var saved = await _unitOfWork.MatchingKnowledgeConfigs.GetConfigAsync();
        return Success(MatchingKnowledgeComposition.BuildView(saved, _defaultOptions), "已清空自定义扩展");
    }
}
