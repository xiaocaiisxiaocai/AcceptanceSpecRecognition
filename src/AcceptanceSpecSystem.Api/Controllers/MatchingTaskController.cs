using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
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

    [HttpGet("download/{taskId:regex(^[[a-f0-9]]{{32}}$)}")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Download(string taskId)
    {
        return HandleFileAsync(() => _matchingTaskAppService.DownloadAsync(User, taskId));
    }
}
