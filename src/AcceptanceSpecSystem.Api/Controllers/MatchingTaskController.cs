using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 匹配任务下载接口。
/// </summary>
[Route("api/matching")]
public class MatchingTaskController : MatchingApiControllerBase
{
    private readonly IMatchingTaskAppService _matchingTaskAppService;

    public MatchingTaskController(IMatchingTaskAppService matchingTaskAppService)
    {
        _matchingTaskAppService = matchingTaskAppService;
    }

    [HttpGet("tasks/{taskId:regex(^[[a-f0-9]]{{32}}$)}/status")]
    [ProducesResponseType(typeof(ApiResponse<MatchingTaskStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public Task<ActionResult<ApiResponse<MatchingTaskStatusDto>>> GetStatus(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        return HandleAsync(() => _matchingTaskAppService.GetStatusAsync(
            GetMatchingUserContext(), taskId, cancellationToken));
    }

    [HttpGet("download/{taskId:regex(^[[a-f0-9]]{{32}}$)}")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Download(string taskId, CancellationToken cancellationToken = default)
    {
        return HandleFileAsync(() => _matchingTaskAppService.DownloadAsync(
            GetMatchingUserContext(), taskId, cancellationToken));
    }
}
