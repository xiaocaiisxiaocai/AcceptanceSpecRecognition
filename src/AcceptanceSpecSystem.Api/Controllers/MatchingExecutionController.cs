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
    public async Task<IActionResult> LlmStream([FromBody] MatchLlmStreamRequest request)
    {
        try
        {
            await _matchingExecutionAppService.LlmStreamAsync(User, Response, request, HttpContext.RequestAborted);
            return new EmptyResult();
        }
        catch (MatchingApiException ex) when (ex.IsNotFound)
        {
            return NotFound(ApiResponse.Error(404, ex.Message));
        }
        catch (MatchingApiException ex)
        {
            return BadRequest(ApiResponse.Error(ex.Code, ex.Message));
        }
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
