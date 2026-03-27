using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 严格复用相关接口。
/// </summary>
[Route("api/matching")]
public class MatchingReuseController : MatchingApiControllerBase
{
    public MatchingReuseController(MatchingWorkflowService workflow)
        : base(workflow)
    {
    }

    [HttpPost("reuse/strict/preview")]
    [ProducesResponseType(typeof(ApiResponse<StrictReusePreviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<StrictReusePreviewResponse>), StatusCodes.Status400BadRequest)]
    public Task<ActionResult<ApiResponse<StrictReusePreviewResponse>>> PreviewStrictReuse([FromBody] StrictReusePreviewRequest request)
    {
        return HandleAsync(() => Workflow.PreviewStrictReuseAsync(User, request));
    }

    [HttpPost("reuse/strict/execute")]
    [AuditOperation("execute", "matching-fill")]
    [ProducesResponseType(typeof(ApiResponse<StrictReuseExecuteResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<StrictReuseExecuteResponse>), StatusCodes.Status400BadRequest)]
    public Task<ActionResult<ApiResponse<StrictReuseExecuteResponse>>> ExecuteStrictReuse([FromBody] StrictReuseExecuteRequest request)
    {
        return HandleAsync(() => Workflow.ExecuteStrictReuseAsync(User, request));
    }
}
