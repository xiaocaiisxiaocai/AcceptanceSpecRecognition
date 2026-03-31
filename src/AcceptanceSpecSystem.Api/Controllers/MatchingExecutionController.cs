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
    private readonly MatchingExecutionAppService _matchingExecutionAppService;

    public MatchingExecutionController(MatchingExecutionAppService matchingExecutionAppService)
    {
        _matchingExecutionAppService = matchingExecutionAppService;
    }

    [HttpPost("llm-stream")]
    [AuditOperation("llm-stream", "matching-fill")]
    public Task LlmStream([FromBody] MatchLlmStreamRequest request)
    {
        return _matchingExecutionAppService.LlmStreamAsync(User, Response, request, HttpContext.RequestAborted);
    }

    [HttpPost("execute")]
    [AuditOperation("execute", "matching-fill")]
    [ProducesResponseType(typeof(ApiResponse<ExecuteFillResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExecuteFillResponse>), StatusCodes.Status400BadRequest)]
    public Task<ActionResult<ApiResponse<ExecuteFillResponse>>> ExecuteFill([FromBody] ExecuteFillRequest request)
    {
        return HandleAsync(() => _matchingExecutionAppService.ExecuteFillAsync(User, request));
    }

    [HttpPost("batch-execute")]
    [AuditOperation("execute-batch", "matching-fill")]
    [ProducesResponseType(typeof(ApiResponse<ExecuteFillResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExecuteFillResponse>), StatusCodes.Status400BadRequest)]
    public Task<ActionResult<ApiResponse<ExecuteFillResponse>>> BatchExecuteFill([FromBody] BatchExecuteFillRequest request)
    {
        return HandleAsync(() => _matchingExecutionAppService.BatchExecuteFillAsync(User, request));
    }
}
