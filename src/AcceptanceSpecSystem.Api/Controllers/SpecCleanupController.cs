using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application.Models;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

[Route("api/spec-cleanup")]
[Authorize]
public sealed class SpecCleanupController : BaseApiController
{
    private readonly IAcceptanceSpecCleanupAppService _service;
    private readonly IAuthDataScopeService _authDataScopeService;

    public SpecCleanupController(
        IAcceptanceSpecCleanupAppService service,
        IAuthDataScopeService authDataScopeService)
    {
        _service = service;
        _authDataScopeService = authDataScopeService;
    }

    [HttpPost("scans")]
    [AuditOperation("scan", "spec-cleanup")]
    public async Task<ActionResult<ApiResponse<SpecCleanupScanStatusModel>>> StartScan(
        [FromBody] StartSpecCleanupScanRequest request,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(cancellationToken);
        if (scope is null) return Error<SpecCleanupScanStatusModel>(401, "会话缺少用户上下文");
        try
        {
            return Success(await _service.StartScanAsync(scope.ToAccessContext(), request, cancellationToken));
        }
        catch (ApplicationServiceException ex)
        {
            return Error<SpecCleanupScanStatusModel>(ex.Code, ex.Message);
        }
    }

    [HttpGet("scans/{scanId}")]
    public async Task<ActionResult<ApiResponse<SpecCleanupScanStatusModel>>> GetScanStatus(
        string scanId,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(cancellationToken);
        if (scope is null) return Error<SpecCleanupScanStatusModel>(401, "会话缺少用户上下文");
        var result = await _service.GetScanStatusAsync(scope.ToAccessContext(), scanId, cancellationToken);
        return result is null ? NotFoundResult<SpecCleanupScanStatusModel>("扫描任务不存在") : Success(result);
    }

    [HttpGet("scans/{scanId}/items")]
    public async Task<ActionResult<ApiResponse<PagedResult<SpecCleanupScanItemModel>>>> GetScanItems(
        string scanId,
        [FromQuery] AcceptanceSpecCleanupCategory category,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(cancellationToken);
        if (scope is null) return Error<PagedResult<SpecCleanupScanItemModel>>(401, "会话缺少用户上下文");
        try
        {
            return Success(await _service.GetScanItemsAsync(
                scope.ToAccessContext(), scanId, category, page, pageSize, cancellationToken));
        }
        catch (ApplicationServiceException ex)
        {
            return Error<PagedResult<SpecCleanupScanItemModel>>(ex.Code, ex.Message);
        }
    }

    [HttpPost("scans/{scanId}/cancel")]
    [AuditOperation("cancel", "spec-cleanup")]
    public async Task<ActionResult<ApiResponse>> CancelScan(
        string scanId,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWriteAsync(
            scope => _service.CancelScanAsync(scope, scanId, cancellationToken),
            "已请求取消扫描", cancellationToken);
    }

    [HttpPost("items/keep")]
    [AuditOperation("keep", "spec-cleanup")]
    public Task<ActionResult<ApiResponse<SpecCleanupBatchResult>>> Keep(
        [FromBody] List<SpecCleanupActionItem> items,
        CancellationToken cancellationToken = default) =>
        ExecuteBatchAsync(scope => _service.KeepAsync(scope, items ?? [], cancellationToken), cancellationToken);

    [HttpPost("items/ignore")]
    [AuditOperation("ignore", "spec-cleanup")]
    public Task<ActionResult<ApiResponse<SpecCleanupBatchResult>>> Ignore(
        [FromBody] List<SpecCleanupActionItem> items,
        CancellationToken cancellationToken = default) =>
        ExecuteBatchAsync(scope => _service.IgnoreAsync(scope, items ?? [], cancellationToken), cancellationToken);

    [HttpPost("items/quarantine")]
    [AuditOperation("quarantine", "spec-cleanup")]
    public Task<ActionResult<ApiResponse<SpecCleanupBatchResult>>> Quarantine(
        [FromBody] List<SpecCleanupActionItem> items,
        CancellationToken cancellationToken = default) =>
        ExecuteBatchAsync(scope => _service.QuarantineAsync(scope, items ?? [], cancellationToken), cancellationToken);

    [HttpGet("quarantine")]
    public async Task<ActionResult<ApiResponse<PagedResult<QuarantinedAcceptanceSpecModel>>>> GetQuarantine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(cancellationToken);
        if (scope is null) return Error<PagedResult<QuarantinedAcceptanceSpecModel>>(401, "会话缺少用户上下文");
        return Success(await _service.GetQuarantinedAsync(scope.ToAccessContext(), page, pageSize, cancellationToken));
    }

    [HttpGet("ignored")]
    public async Task<ActionResult<ApiResponse<PagedResult<IgnoredAcceptanceSpecModel>>>> GetIgnored(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(cancellationToken);
        if (scope is null) return Error<PagedResult<IgnoredAcceptanceSpecModel>>(401, "会话缺少用户上下文");
        return Success(await _service.GetIgnoredAsync(scope.ToAccessContext(), page, pageSize, cancellationToken));
    }

    [HttpPost("ignored/restore")]
    [AuditOperation("unignore", "spec-cleanup")]
    public Task<ActionResult<ApiResponse<SpecCleanupBatchResult>>> Unignore(
        [FromBody] RestoreSpecCleanupRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteBatchAsync(scope => _service.UnignoreAsync(scope, request.SpecIds ?? [], cancellationToken), cancellationToken);

    [HttpPost("quarantine/restore")]
    [AuditOperation("restore", "spec-cleanup")]
    public Task<ActionResult<ApiResponse<SpecCleanupBatchResult>>> Restore(
        [FromBody] RestoreSpecCleanupRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteBatchAsync(scope => _service.RestoreAsync(scope, request.SpecIds ?? [], cancellationToken), cancellationToken);

    [HttpPost("quarantine/permanent-delete")]
    [AuditOperation("permanent-delete", "spec-cleanup")]
    public Task<ActionResult<ApiResponse<SpecCleanupBatchResult>>> PermanentlyDelete(
        [FromBody] PermanentlyDeleteSpecCleanupRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteBatchAsync(scope => _service.PermanentlyDeleteAsync(scope, request, cancellationToken), cancellationToken);

    private async Task<ActionResult<ApiResponse<SpecCleanupBatchResult>>> ExecuteBatchAsync(
        Func<SpecAccessContext, Task<SpecCleanupBatchResult>> action,
        CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(cancellationToken);
        if (scope is null) return Error<SpecCleanupBatchResult>(401, "会话缺少用户上下文");
        try
        {
            return Success(await action(scope.ToAccessContext()));
        }
        catch (ApplicationServiceException ex)
        {
            return Error<SpecCleanupBatchResult>(ex.Code, ex.Message);
        }
    }

    private async Task<ActionResult<ApiResponse>> ExecuteWriteAsync(
        Func<SpecAccessContext, Task> action,
        string message,
        CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(cancellationToken);
        if (scope is null) return Error(401, "会话缺少用户上下文");
        try
        {
            await action(scope.ToAccessContext());
            return Success(message);
        }
        catch (ApplicationServiceException ex)
        {
            return Error(ex.Code, ex.Message);
        }
    }

    private Task<DataScopeResult?> ResolveScopeAsync(CancellationToken cancellationToken) =>
        SpecDataScopeHelper.ResolveScopeAsync(User, _authDataScopeService, cancellationToken);
}
