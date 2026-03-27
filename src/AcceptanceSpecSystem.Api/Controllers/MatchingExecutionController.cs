using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 匹配执行与流式 LLM 相关接口。
/// </summary>
[Route("api/matching")]
public class MatchingExecutionController : MatchingApiControllerBase
{
    public MatchingExecutionController(MatchingWorkflowService workflow)
        : base(workflow)
    {
    }

    [HttpPost("llm-stream")]
    [AuditOperation("llm-stream", "matching-fill")]
    public Task LlmStream([FromBody] MatchLlmStreamRequest request)
    {
        return Workflow.LlmStreamAsync(User, Response, request, HttpContext.RequestAborted);
    }

    [HttpPost("execute")]
    [AuditOperation("execute", "matching-fill")]
    [ProducesResponseType(typeof(ApiResponse<ExecuteFillResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExecuteFillResponse>), StatusCodes.Status400BadRequest)]
    public Task<ActionResult<ApiResponse<ExecuteFillResponse>>> ExecuteFill([FromBody] ExecuteFillRequest request)
    {
        return HandleAsync(() => Workflow.ExecuteFillAsync(User, request));
    }

    [HttpPost("batch-execute")]
    [AuditOperation("execute-batch", "matching-fill")]
    [ProducesResponseType(typeof(ApiResponse<ExecuteFillResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExecuteFillResponse>), StatusCodes.Status400BadRequest)]
    public Task<ActionResult<ApiResponse<ExecuteFillResponse>>> BatchExecuteFill([FromBody] BatchExecuteFillRequest request)
    {
        return HandleAsync(() => Workflow.BatchExecuteFillAsync(User, request));
    }
}
