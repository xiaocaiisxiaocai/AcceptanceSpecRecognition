using Microsoft.AspNetCore.Mvc;
using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuditController : ControllerBase
{
    private readonly IAuditLogger _auditLogger;

    public AuditController(IAuditLogger auditLogger)
    {
        _auditLogger = auditLogger;
    }

    /// <summary>
    /// 查询审计日志
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AuditQueryResult>> Query([FromQuery] AuditQueryParams queryParams)
    {
        var filter = new AuditLogFilter
        {
            StartTime = queryParams.StartTime,
            EndTime = queryParams.EndTime,
            ActionType = queryParams.ActionType,
            Page = queryParams.Page,
            PageSize = queryParams.PageSize
        };

        var result = await _auditLogger.QueryLogsAsync(filter);
        return Ok(result);
    }

    /// <summary>
    /// 获取审计日志统计
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<AuditStats>> GetStats([FromQuery] DateTime? startTime, [FromQuery] DateTime? endTime)
    {
        var filter = new AuditLogFilter
        {
            StartTime = startTime,
            EndTime = endTime
        };

        var result = await _auditLogger.QueryLogsAsync(filter);

        var stats = new AuditStats
        {
            TotalQueries = result.Entries.Count(e => e.ActionType == "query"),
            TotalConfirms = result.Entries.Count(e => e.ActionType == "confirm_match"),
            TotalRejects = result.Entries.Count(e => e.ActionType == "reject_match"),
            TotalConfigChanges = result.Entries.Count(e => e.ActionType == "config_change"),
            TotalHistoryCreates = result.Entries.Count(e => e.ActionType == "create_history"),
            TotalHistoryUpdates = result.Entries.Count(e => e.ActionType == "update_history")
        };

        return Ok(stats);
    }

    /// <summary>
    /// 清除所有审计日志
    /// </summary>
    [HttpDelete]
    public async Task<ActionResult> Clear()
    {
        await _auditLogger.ClearLogsAsync();
        return NoContent();
    }
}

public class AuditQueryParams
{
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? ActionType { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class AuditStats
{
    public int TotalQueries { get; set; }
    public int TotalConfirms { get; set; }
    public int TotalRejects { get; set; }
    public int TotalConfigChanges { get; set; }
    public int TotalHistoryCreates { get; set; }
    public int TotalHistoryUpdates { get; set; }
}
