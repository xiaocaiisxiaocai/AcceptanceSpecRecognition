using System.Text.Json;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 批量回复接口。
/// </summary>
[Route("api/batch-reply")]
public class BatchReplyController : MatchingApiControllerBase
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IBatchReplyAppService _batchReplyAppService;

    public BatchReplyController(IBatchReplyAppService batchReplyAppService)
    {
        _batchReplyAppService = batchReplyAppService;
    }

    [HttpPost("source/upload")]
    [AuditOperation("upload-source", "batch-reply")]
    [EnableRateLimiting("upload")]
    [ProducesResponseType(typeof(ApiResponse<BatchReplySourceUploadResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BatchReplySourceUploadResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<BatchReplySourceUploadResponse>>> UploadSource(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateBatchUploadFiles([file]);
            var upload = ToUploadDocument(file);
            var result = await _batchReplyAppService.UploadSourceAsync(RequireBatchReplyUser(), upload, cancellationToken);
            return Success(result, "来源文件上传成功");
        }
        catch (ApplicationServiceException ex)
        {
            return ex.Code == 404
                ? NotFoundResult<BatchReplySourceUploadResponse>(ex.Message)
                : Error<BatchReplySourceUploadResponse>(ex.Code, ex.Message);
        }
        catch (MatchingApiException ex)
        {
            return Error<BatchReplySourceUploadResponse>(ex.Code, ex.Message);
        }
    }

    [HttpPost("targets/upload")]
    [AuditOperation("upload", "batch-reply")]
    [EnableRateLimiting("upload")]
    [ProducesResponseType(typeof(ApiResponse<BatchReplyTargetUploadResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BatchReplyTargetUploadResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<BatchReplyTargetUploadResponse>>> UploadTargets(
        [FromForm] string sessionId,
        [FromForm] List<IFormFile> targetFiles,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateBatchUploadFiles(targetFiles);
            var uploads = targetFiles.Select(ToUploadDocument).ToArray();
            var result = await _batchReplyAppService.UploadTargetsAsync(
                RequireBatchReplyUser(),
                sessionId,
                uploads,
                cancellationToken);
            return Success(result, "目标文件上传成功");
        }
        catch (ApplicationServiceException ex)
        {
            return ex.Code == 404
                ? NotFoundResult<BatchReplyTargetUploadResponse>(ex.Message)
                : Error<BatchReplyTargetUploadResponse>(ex.Code, ex.Message);
        }
        catch (MatchingApiException ex)
        {
            return Error<BatchReplyTargetUploadResponse>(ex.Code, ex.Message);
        }
    }

    [HttpGet("sessions/{sessionId}/tables")]
    [ProducesResponseType(typeof(ApiResponse<List<TableInfoDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<List<TableInfoDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<List<TableInfoDto>>>> GetTables(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _batchReplyAppService.GetSourceTablesAsync(
                RequireBatchReplyUser(),
                sessionId,
                cancellationToken);
            return Success(result);
        }
        catch (ApplicationServiceException ex)
        {
            return ex.Code == 404
                ? NotFoundResult<List<TableInfoDto>>(ex.Message)
                : Error<List<TableInfoDto>>(ex.Code, ex.Message);
        }
    }

    [HttpGet("sessions/{sessionId}/tables/{tableIndex}/preview")]
    [ProducesResponseType(typeof(ApiResponse<TableDataDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<TableDataDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TableDataDto>>> GetTablePreview(
        string sessionId,
        int tableIndex,
        [FromQuery] int previewRows = 100,
        [FromQuery] int headerRowIndex = 0,
        [FromQuery] int headerRowCount = 1,
        [FromQuery] int dataStartRowIndex = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _batchReplyAppService.GetSourceTablePreviewAsync(
                RequireBatchReplyUser(),
                sessionId,
                tableIndex,
                previewRows,
                headerRowIndex,
                headerRowCount,
                dataStartRowIndex,
                cancellationToken);
            return Success(result);
        }
        catch (ApplicationServiceException ex)
        {
            return ex.Code == 404
                ? NotFoundResult<TableDataDto>(ex.Message)
                : Error<TableDataDto>(ex.Code, ex.Message);
        }
    }

    [HttpGet("sessions/{sessionId}/targets/{targetId}/tables")]
    [ProducesResponseType(typeof(ApiResponse<List<TableInfoDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<List<TableInfoDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<List<TableInfoDto>>>> GetTargetTables(
        string sessionId,
        string targetId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _batchReplyAppService.GetTargetTablesAsync(
                RequireBatchReplyUser(),
                sessionId,
                targetId,
                cancellationToken);
            return Success(result);
        }
        catch (ApplicationServiceException ex)
        {
            return ex.Code == 404
                ? NotFoundResult<List<TableInfoDto>>(ex.Message)
                : Error<List<TableInfoDto>>(ex.Code, ex.Message);
        }
    }

    [HttpGet("sessions/{sessionId}/targets/{targetId}/tables/{tableIndex}/preview")]
    [ProducesResponseType(typeof(ApiResponse<TableDataDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<TableDataDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TableDataDto>>> GetTargetTablePreview(
        string sessionId,
        string targetId,
        int tableIndex,
        [FromQuery] int previewRows = 100,
        [FromQuery] int headerRowIndex = 0,
        [FromQuery] int headerRowCount = 1,
        [FromQuery] int dataStartRowIndex = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _batchReplyAppService.GetTargetTablePreviewAsync(
                RequireBatchReplyUser(),
                sessionId,
                targetId,
                tableIndex,
                previewRows,
                headerRowIndex,
                headerRowCount,
                dataStartRowIndex,
                cancellationToken);
            return Success(result);
        }
        catch (ApplicationServiceException ex)
        {
            return ex.Code == 404
                ? NotFoundResult<TableDataDto>(ex.Message)
                : Error<TableDataDto>(ex.Code, ex.Message);
        }
    }

    [HttpPost("table-preview")]
    [ProducesResponseType(typeof(ApiResponse<BatchReplyTablePreviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BatchReplyTablePreviewResponse>), StatusCodes.Status400BadRequest)]
    public Task<ActionResult<ApiResponse<BatchReplyTablePreviewResponse>>> TablePreview(
        [FromBody] BatchReplyTablePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        return HandleAsync(() => _batchReplyAppService.TablePreviewAsync(RequireBatchReplyUser(), request, cancellationToken));
    }

    [HttpPost("preview")]
    [EnableRateLimiting("ai-heavy")]
    [ProducesResponseType(typeof(ApiResponse<BatchReplyPreviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BatchReplyPreviewResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<BatchReplyPreviewResponse>>> Preview(
        [FromForm] string sessionId,
        [FromForm] string tableConfigsJson,
        [FromForm] List<IFormFile> targetFiles,
        CancellationToken cancellationToken = default)
    {
        var tableConfigs = ParseTableConfigs(tableConfigsJson);
        ValidateBatchUploadFiles(targetFiles);
        var uploads = targetFiles.Select(ToUploadDocument).ToArray();
        return await HandleAsync(() => _batchReplyAppService.PreviewAsync(
            RequireBatchReplyUser(),
            sessionId,
            tableConfigs,
            uploads,
            cancellationToken));
    }

    [HttpPost("execute")]
    [AuditOperation("execute", "batch-reply")]
    [EnableRateLimiting("ai-heavy")]
    [ProducesResponseType(typeof(ApiResponse<BatchReplyExecuteResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BatchReplyExecuteResponse>), StatusCodes.Status400BadRequest)]
    public Task<ActionResult<ApiResponse<BatchReplyExecuteResponse>>> Execute(
        [FromBody] BatchReplyExecuteRequest request,
        CancellationToken cancellationToken = default)
    {
        return HandleAsync(() => _batchReplyAppService.ExecuteAsync(RequireBatchReplyUser(), request, cancellationToken));
    }

    [HttpGet("download/{taskId}")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Download(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        return HandleFileAsync(() => _batchReplyAppService.DownloadAsync(RequireBatchReplyUser(), taskId, cancellationToken));
    }

    private static List<BatchTableConfig> ParseTableConfigs(string tableConfigsJson)
    {
        if (string.IsNullOrWhiteSpace(tableConfigsJson))
        {
            throw new MatchingApiException(400, "表格配置不能为空");
        }

        try
        {
            return JsonSerializer.Deserialize<List<BatchTableConfig>>(tableConfigsJson, WebJsonOptions) ?? [];
        }
        catch (JsonException)
        {
            throw new MatchingApiException(400, "表格配置格式不正确");
        }
    }

    private BatchReplyUserContext RequireBatchReplyUser()
    {
        var userId = AuthClaimHelper.GetUserId(User);
        var companyId = AuthClaimHelper.GetCompanyId(User);
        if (!userId.HasValue || !companyId.HasValue)
            throw new MatchingApiException(401, "会话缺少用户上下文");
        return new BatchReplyUserContext(userId.Value, companyId.Value);
    }

    private static BatchReplyUploadDocument ToUploadDocument(IFormFile file)
    {
        if (file == null || file.Length == 0)
            throw new ApplicationServiceException(400, "文件不能为空");
        var fileType = UploadFileValidation.ValidateOfficeDocument(file, allowExcel: true, allowWord: true);
        return new BatchReplyUploadDocument(file.FileName, fileType, file.Length, file.OpenReadStream);
    }

    private static void ValidateBatchUploadFiles(IReadOnlyCollection<IFormFile> files)
    {
        if (files.Count == 0)
            throw new ApplicationServiceException(400, "请至少上传一个文件");
        if (files.Count > BatchReplyUploadLimits.MaxFileCount)
            throw new ApplicationServiceException(400, $"单次最多上传 {BatchReplyUploadLimits.MaxFileCount} 个文件");

        long totalBytes = 0;
        foreach (var file in files)
        {
            if (file == null || file.Length <= 0)
                throw new ApplicationServiceException(400, "文件不能为空");
            if (file.Length > BatchReplyUploadLimits.MaxFileSizeBytes)
                throw new ApplicationServiceException(400, "单个文件大小不能超过 50MB");
            totalBytes = checked(totalBytes + file.Length);
        }

        if (totalBytes > BatchReplyUploadLimits.MaxBatchSizeBytes)
            throw new ApplicationServiceException(400, "单次上传文件总大小不能超过 100MB");
    }
}
