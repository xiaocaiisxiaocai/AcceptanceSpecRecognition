using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 匹配预览与相似度相关接口。
/// </summary>
[Route("api/matching")]
public class MatchingPreviewController : MatchingApiControllerBase
{
    public MatchingPreviewController(MatchingWorkflowService workflow)
        : base(workflow)
    {
    }

    [HttpPost("preview")]
    [ProducesResponseType(typeof(ApiResponse<MatchPreviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MatchPreviewResponse>), StatusCodes.Status400BadRequest)]
    public Task<ActionResult<ApiResponse<MatchPreviewResponse>>> Preview([FromBody] MatchPreviewRequest request)
    {
        return HandleAsync(() => Workflow.PreviewAsync(User, request));
    }

    [HttpPost("batch-preview")]
    [ProducesResponseType(typeof(ApiResponse<BatchPreviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BatchPreviewResponse>), StatusCodes.Status400BadRequest)]
    public Task<ActionResult<ApiResponse<BatchPreviewResponse>>> BatchPreview([FromBody] BatchPreviewRequest request)
    {
        return HandleAsync(() => Workflow.BatchPreviewAsync(User, request));
    }

    [HttpPost("similarity")]
    [ProducesResponseType(typeof(ApiResponse<SimilarityResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SimilarityResponse>), StatusCodes.Status400BadRequest)]
    public Task<ActionResult<ApiResponse<SimilarityResponse>>> ComputeSimilarity([FromBody] SimilarityRequest request)
    {
        return HandleAsync(() => Workflow.ComputeSimilarityAsync(request));
    }
}
