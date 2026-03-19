using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 审计日志控制器
/// </summary>
[Route("api/audit-logs")]
[Authorize]
public class AuditLogsController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;

    public AuditLogsController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// 分页查询审计日志（仅管理员）
    /// </summary>
    [HttpGet]
    [Authorize]
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
        [FromQuery] int? maxStatusCode = null)
    {
        var (items, total) = await _unitOfWork.AuditLogs.GetPagedAsync(
            page,
            pageSize,
            source,
            level,
            username,
            requestMethod,
            keyword,
            from,
            to,
            minStatusCode,
            maxStatusCode);

        return Success(new PagedData<AuditLogListItemDto>
        {
            Items = items.Select(ToListDto).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 查询审计日志详情（仅管理员）
    /// </summary>
    [HttpGet("{id:int}")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<AuditLogDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuditLogDetailDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AuditLogDetailDto>>> GetDetail(int id)
    {
        var entity = await _unitOfWork.AuditLogs.GetByIdAsync(id);
        if (entity == null)
        {
            return NotFoundResult<AuditLogDetailDto>("审计日志不存在");
        }

        return Success(ToDetailDto(entity));
    }

    /// <summary>
    /// 按时间范围删除审计日志（仅管理员）
    /// </summary>
    [HttpDelete("range")]
    [AuditOperation("delete-range", "audit-log")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteByRange(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        if (!from.HasValue && !to.HasValue)
        {
            return Error<object>(400, "请至少提供 from 或 to");
        }

        if (from.HasValue && to.HasValue && from > to)
        {
            return Error<object>(400, "from 不能晚于 to");
        }

        var deletedCount = await _unitOfWork.AuditLogs.DeleteByRangeAsync(from, to);
        return Success<object>(new
        {
            deletedCount,
            from,
            to
        }, $"删除成功，共删除 {deletedCount} 条审计日志");
    }

    private static AuditLogListItemDto ToListDto(AuditLog entity)
    {
        return new AuditLogListItemDto
        {
            Id = entity.Id,
            Source = entity.Source,
            Level = entity.Level,
            EventType = entity.EventType,
            Username = entity.Username,
            RequestMethod = entity.RequestMethod,
            RequestPath = entity.RequestPath,
            QueryString = entity.QueryString,
            StatusCode = entity.StatusCode,
            DurationMs = entity.DurationMs,
            ClientIp = entity.ClientIp,
            UserAgent = entity.UserAgent,
            ClientTraceId = entity.ClientTraceId,
            ClientId = entity.ClientId,
            FrontendRoute = entity.FrontendRoute,
            CreatedAt = entity.CreatedAt
        };
    }

    private static AuditLogDetailDto ToDetailDto(AuditLog entity)
    {
        return new AuditLogDetailDto
        {
            Id = entity.Id,
            Source = entity.Source,
            Level = entity.Level,
            EventType = entity.EventType,
            Username = entity.Username,
            RequestMethod = entity.RequestMethod,
            RequestPath = entity.RequestPath,
            QueryString = entity.QueryString,
            StatusCode = entity.StatusCode,
            DurationMs = entity.DurationMs,
            ClientIp = entity.ClientIp,
            UserAgent = entity.UserAgent,
            ClientTraceId = entity.ClientTraceId,
            ClientId = entity.ClientId,
            FrontendRoute = entity.FrontendRoute,
            Details = entity.Details,
            CreatedAt = entity.CreatedAt
        };
    }
}
