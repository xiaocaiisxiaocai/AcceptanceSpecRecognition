using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 审计日志控制器。
/// </summary>
[Route("api/audit-logs")]
[Authorize]
public class AuditLogsController : BaseApiController
{
    private readonly IAuditTrailAppService _auditTrail;

    public AuditLogsController(IAuditTrailAppService auditTrail)
    {
        _auditTrail = auditTrail;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedData<AuditLogListItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedData<AuditLogListItemDto>>>> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] AuditLogSource? source = null,
        [FromQuery] AuditLogLevel? level = null,
        [FromQuery] string? username = null,
        [FromQuery] string? requestMethod = null,
        [FromQuery] string? keyword = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int? minStatusCode = null,
        [FromQuery] int? maxStatusCode = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _auditTrail.GetPagedAsync(
            page, pageSize, source, level, username, requestMethod, keyword, from, to,
            minStatusCode, maxStatusCode, cancellationToken);

        return Success(new PagedData<AuditLogListItemDto>
        {
            Items = result.Items,
            Total = result.Total,
            Page = result.Page,
            PageSize = result.PageSize
        });
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<AuditLogDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuditLogDetailDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AuditLogDetailDto>>> GetDetail(
        int id,
        CancellationToken cancellationToken = default)
    {
        var item = await _auditTrail.GetByIdAsync(id, cancellationToken);
        return item == null
            ? NotFoundResult<AuditLogDetailDto>("审计日志不存在")
            : Success(item);
    }

    [HttpDelete("range")]
    [AuditOperation("delete-range", "audit-log")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteByRange(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var deletedCount = await _auditTrail.DeleteByRangeAsync(from, to, cancellationToken);
            return Success<object>(new { deletedCount, from, to }, $"删除成功，共删除 {deletedCount} 条审计日志");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<object>(ex.Code, ex.Message);
        }
    }
}
