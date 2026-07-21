using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 匹配执行与流式 LLM 相关接口。
/// </summary>
[Route("api/matching")]
public class MatchingExecutionController : MatchingApiControllerBase
{
    private readonly IMatchingLlmStreamAppService _matchingLlmStreamAppService;
    private readonly IMatchingFillExecutionAppService _matchingFillExecutionAppService;
    private readonly ISmartFillSpecBackfillAppService _smartFillSpecBackfillAppService;

    public MatchingExecutionController(
        IMatchingLlmStreamAppService matchingLlmStreamAppService,
        IMatchingFillExecutionAppService matchingFillExecutionAppService,
        ISmartFillSpecBackfillAppService smartFillSpecBackfillAppService)
    {
        _matchingLlmStreamAppService = matchingLlmStreamAppService;
        _matchingFillExecutionAppService = matchingFillExecutionAppService;
        _smartFillSpecBackfillAppService = smartFillSpecBackfillAppService;
    }

    [HttpPost("llm-stream")]
    [AuditOperation("llm-stream", "matching-fill")]
    [EnableRateLimiting("ai-heavy")]
    public async Task<IActionResult> LlmStream(
        [FromBody] MatchLlmStreamRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _matchingLlmStreamAppService.LlmStreamAsync(
                GetMatchingUserContext(),
                new HttpMatchingEventStream(Response),
                request,
                cancellationToken);
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
    [EnableRateLimiting("ai-heavy")]
    [ProducesResponseType(typeof(ApiResponse<ExecuteFillResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExecuteFillResponse>), StatusCodes.Status400BadRequest)]
    public Task<ActionResult<ApiResponse<ExecuteFillResponse>>> BatchExecuteFill(
        [FromBody] BatchExecuteFillRequest request,
        CancellationToken cancellationToken = default)
    {
        return HandleAsync(() => _matchingFillExecutionAppService.BatchExecuteFillAsync(
            GetMatchingUserContext(), request, cancellationToken));
    }

    [HttpPost("spec-backfill")]
    [AuditOperation("spec-backfill", "matching-fill")]
    [EnableRateLimiting("ai-heavy")]
    [ProducesResponseType(typeof(ApiResponse<SmartFillSpecBackfillResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SmartFillSpecBackfillResponse>), StatusCodes.Status400BadRequest)]
    public Task<ActionResult<ApiResponse<SmartFillSpecBackfillResponse>>> SpecBackfill(
        [FromBody] SmartFillSpecBackfillRequest request,
        CancellationToken cancellationToken = default)
    {
        return HandleAsync(() => _smartFillSpecBackfillAppService.BackfillAsync(
            GetMatchingUserContext(), request, cancellationToken));
    }
}
