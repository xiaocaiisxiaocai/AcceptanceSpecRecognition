using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 执行记录控制器
/// </summary>
[Route("api/execution-history")]
[Authorize]
public class ExecutionHistoryController : BaseApiController
{
    private readonly IExecutionHistoryAppService _executionHistoryAppService;

    public ExecutionHistoryController(IExecutionHistoryAppService executionHistoryAppService)
    {
        _executionHistoryAppService = executionHistoryAppService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedData<ExecutionHistoryListItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedData<ExecutionHistoryListItemDto>>>> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] string? taskType = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _executionHistoryAppService.GetListAsync(User, page, pageSize, keyword, taskType, cancellationToken);
        return Success(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<ExecutionHistoryDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExecutionHistoryDetailDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ExecutionHistoryDetailDto>>> GetDetail(
        int id,
        CancellationToken cancellationToken = default)
    {
        var result = await _executionHistoryAppService.GetDetailAsync(User, id, cancellationToken);
        if (result == null)
        {
            return NotFoundResult<ExecutionHistoryDetailDto>("执行记录不存在");
        }

        return Success(result);
    }

    [HttpGet("{id:int}/smart-fill/rows")]
    [ProducesResponseType(typeof(ApiResponse<ExecutionHistorySmartFillRowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExecutionHistorySmartFillRowDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ExecutionHistorySmartFillRowDto>>> GetSmartFillRow(
        int id,
        [FromQuery] int fileIndex,
        [FromQuery] int sheetIndex,
        [FromQuery] int rowIndex,
        CancellationToken cancellationToken = default)
    {
        var result = await _executionHistoryAppService.GetSmartFillRowAsync(
            User,
            id,
            fileIndex,
            sheetIndex,
            rowIndex,
            cancellationToken);
        if (result == null)
        {
            return NotFoundResult<ExecutionHistorySmartFillRowDto>("完整回放归档不存在");
        }

        return Success(result);
    }
}
