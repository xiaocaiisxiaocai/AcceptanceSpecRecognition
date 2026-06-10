using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 智能填充匹配预览接口。
/// </summary>
[Route("api/matching")]
public class MatchingPreviewController : MatchingApiControllerBase
{
    private readonly IMatchingPreviewAppService _matchingPreviewAppService;

    public MatchingPreviewController(IMatchingPreviewAppService matchingPreviewAppService)
    {
        _matchingPreviewAppService = matchingPreviewAppService;
    }

    [HttpPost("batch-preview")]
    [EnableRateLimiting("ai-heavy")]
    [ProducesResponseType(typeof(ApiResponse<BatchPreviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BatchPreviewResponse>), StatusCodes.Status400BadRequest)]
    public Task<ActionResult<ApiResponse<BatchPreviewResponse>>> BatchPreview([FromBody] BatchPreviewRequest request)
    {
        return HandleAsync(() => _matchingPreviewAppService.BatchPreviewAsync(User, request, HttpContext.RequestAborted));
    }

    [HttpGet("batch-preview-progress/{requestId}")]
    [ProducesResponseType(typeof(ApiResponse<BatchPreviewProgressResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BatchPreviewProgressResponse>), StatusCodes.Status404NotFound)]
    public Task<ActionResult<ApiResponse<BatchPreviewProgressResponse>>> GetBatchPreviewProgress(string requestId)
    {
        return HandleAsync(() => Task.FromResult(_matchingPreviewAppService.GetBatchPreviewProgress(requestId)));
    }
}
