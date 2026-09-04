using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Authorization;
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
    private readonly ISmartFillArchiveAppService _smartFillArchiveAppService;

    public ExecutionHistoryController(
        IExecutionHistoryAppService executionHistoryAppService,
        ISmartFillArchiveAppService smartFillArchiveAppService)
    {
        _executionHistoryAppService = executionHistoryAppService;
        _smartFillArchiveAppService = smartFillArchiveAppService;
    }

    [HttpGet("smart-fill-archives")]
    [ProducesResponseType(typeof(ApiResponse<PagedData<SmartFillArchiveListItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedData<SmartFillArchiveListItemDto>>>> GetSmartFillArchives(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int? orgUnitId = null,
        [FromQuery] string? operatorKeyword = null,
        CancellationToken cancellationToken = default)
    {
        var userId = AuthClaimHelper.GetUserId(User);
        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!userId.HasValue || !companyId.HasValue)
            return Error<PagedData<SmartFillArchiveListItemDto>>(401, "会话缺少用户上下文");

        try
        {
            return Success(await _smartFillArchiveAppService.GetListAsync(
                userId.Value,
                companyId.Value,
                User.IsInRole("admin"),
                page,
                pageSize,
                keyword,
                from,
                to,
                orgUnitId,
                operatorKeyword,
                cancellationToken));
        }
        catch (ApplicationServiceException ex)
        {
            return Error<PagedData<SmartFillArchiveListItemDto>>(ex.Code, ex.Message);
        }
    }

    [HttpGet("smart-fill-archives/{id:int}/download")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadSmartFillArchive(
        int id,
        CancellationToken cancellationToken = default)
    {
        var userId = AuthClaimHelper.GetUserId(User);
        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!userId.HasValue || !companyId.HasValue)
            return ErrorResult(401, "会话缺少用户上下文");

        try
        {
            var result = await _smartFillArchiveAppService.DownloadAsync(
                userId.Value,
                companyId.Value,
                User.IsInRole("admin"),
                id,
                cancellationToken);
            return File(result.Content, result.ContentType, result.FileName, enableRangeProcessing: true);
        }
        catch (ApplicationServiceException ex)
        {
            return ErrorResult(ex.Code, ex.Message);
        }
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
        var user = ResolveUserContext();
        if (user == null)
            return Error<PagedData<ExecutionHistoryListItemDto>>(401, "会话缺少用户上下文");
        var result = await _executionHistoryAppService.GetListAsync(user, page, pageSize, keyword, taskType, cancellationToken);
        return Success(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<ExecutionHistoryDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExecutionHistoryDetailDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ExecutionHistoryDetailDto>>> GetDetail(
        int id,
        CancellationToken cancellationToken = default)
    {
        var user = ResolveUserContext();
        if (user == null)
            return Error<ExecutionHistoryDetailDto>(401, "会话缺少用户上下文");
        var result = await _executionHistoryAppService.GetDetailAsync(user, id, cancellationToken);
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
        var user = ResolveUserContext();
        if (user == null)
            return Error<ExecutionHistorySmartFillRowDto>(401, "会话缺少用户上下文");

        var result = await _executionHistoryAppService.GetSmartFillRowAsync(
            user,
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

    private MatchingUserContext? ResolveUserContext()
    {
        var userId = AuthClaimHelper.GetUserId(User);
        var companyId = AuthClaimHelper.GetCompanyId(User);
        return userId.HasValue && companyId.HasValue
            ? new MatchingUserContext(userId.Value, companyId.Value, User.Identity?.Name ?? string.Empty)
            : null;
    }
}
