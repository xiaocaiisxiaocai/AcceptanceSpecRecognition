using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 智能填充匹配预览接口。
/// </summary>
[Route("api/matching")]
public class MatchingPreviewController : MatchingApiControllerBase
{
    private readonly MatchingPreviewAppService _matchingPreviewAppService;

    public MatchingPreviewController(MatchingPreviewAppService matchingPreviewAppService)
    {
        _matchingPreviewAppService = matchingPreviewAppService;
    }

    [HttpPost("batch-preview")]
    [ProducesResponseType(typeof(ApiResponse<BatchPreviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BatchPreviewResponse>), StatusCodes.Status400BadRequest)]
    public Task<ActionResult<ApiResponse<BatchPreviewResponse>>> BatchPreview([FromBody] BatchPreviewRequest request)
    {
        return HandleAsync(() => _matchingPreviewAppService.BatchPreviewAsync(User, request));
    }

    [HttpGet("batch-preview-progress/{requestId}")]
    [ProducesResponseType(typeof(ApiResponse<BatchPreviewProgressResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BatchPreviewProgressResponse>), StatusCodes.Status404NotFound)]
    public Task<ActionResult<ApiResponse<BatchPreviewProgressResponse>>> GetBatchPreviewProgress(string requestId)
    {
        return HandleAsync(() => Task.FromResult(_matchingPreviewAppService.GetBatchPreviewProgress(requestId)));
    }
}
