using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

[Route("api/database-backup")]
[Authorize]
public sealed class DatabaseBackupController : BaseApiController
{
    private readonly DatabaseBackupManager _manager;

    public DatabaseBackupController(DatabaseBackupManager manager)
    {
        _manager = manager;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<DatabaseBackupOverviewDto>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<DatabaseBackupOverviewDto>> Get()
    {
        return Success(_manager.GetOverview());
    }

    [HttpPut("options")]
    [AuditOperation("update", "database-backup")]
    [ProducesResponseType(typeof(ApiResponse<DatabaseBackupOverviewDto>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<DatabaseBackupOverviewDto>> UpdateOptions(
        [FromBody] UpdateDatabaseBackupOptionsRequest request)
    {
        return Success(_manager.UpdateOptions(request));
    }

    [HttpPost("run")]
    [AuditOperation("execute", "database-backup")]
    [ProducesResponseType(typeof(ApiResponse<DatabaseBackupOverviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<DatabaseBackupOverviewDto>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<DatabaseBackupOverviewDto>), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<DatabaseBackupOverviewDto>>> Run(CancellationToken cancellationToken)
    {
        var result = await _manager.RunOnceAsync(cancellationToken);
        if (!result.Started)
            return Error<DatabaseBackupOverviewDto>(409, result.Error ?? "数据库备份正在执行。");

        if (!result.Succeeded)
            return Error<DatabaseBackupOverviewDto>(500, result.Error ?? "数据库备份失败。");

        return Success(_manager.GetOverview());
    }
}
