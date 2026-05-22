using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

[Route("api/embedding-cache-warmup")]
[Authorize]
public sealed class EmbeddingCacheWarmupController : BaseApiController
{
    private readonly EmbeddingCacheWarmupManager _manager;

    public EmbeddingCacheWarmupController(EmbeddingCacheWarmupManager manager)
    {
        _manager = manager;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<EmbeddingCacheWarmupOverviewDto>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<EmbeddingCacheWarmupOverviewDto>> Get()
    {
        return Success(_manager.GetOverview());
    }

    [HttpPut("options")]
    [AuditOperation("update", "embedding-cache-warmup")]
    [ProducesResponseType(typeof(ApiResponse<EmbeddingCacheWarmupOverviewDto>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<EmbeddingCacheWarmupOverviewDto>> UpdateOptions(
        [FromBody] UpdateEmbeddingCacheWarmupOptionsRequest request)
    {
        return Success(_manager.UpdateOptions(request));
    }

    [HttpPost("run")]
    [AuditOperation("execute", "embedding-cache-warmup")]
    [ProducesResponseType(typeof(ApiResponse<EmbeddingCacheWarmupOverviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<EmbeddingCacheWarmupOverviewDto>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<EmbeddingCacheWarmupOverviewDto>), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<EmbeddingCacheWarmupOverviewDto>>> Run(CancellationToken cancellationToken)
    {
        var result = await _manager.RunOnceAsync(cancellationToken);
        if (!result.Started)
            return Error<EmbeddingCacheWarmupOverviewDto>(409, result.Error ?? "向量缓存预热正在执行。");

        if (!result.Succeeded)
            return Error<EmbeddingCacheWarmupOverviewDto>(500, result.Error ?? "向量缓存预热失败。");

        return Success(_manager.GetOverview());
    }
}
