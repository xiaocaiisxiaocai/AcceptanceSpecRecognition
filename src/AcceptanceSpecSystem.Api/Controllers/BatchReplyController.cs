using System.Text.Json;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 批量回复接口。
/// </summary>
[Route("api/batch-reply")]
public class BatchReplyController : MatchingApiControllerBase
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly BatchReplyAppService _batchReplyAppService;

    public BatchReplyController(BatchReplyAppService batchReplyAppService)
    {
        _batchReplyAppService = batchReplyAppService;
    }

    [HttpPost("source/upload")]
    [AuditOperation("upload-source", "batch-reply")]
    [ProducesResponseType(typeof(ApiResponse<BatchReplySourceUploadResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BatchReplySourceUploadResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<BatchReplySourceUploadResponse>>> UploadSource(IFormFile file)
    {
        try
        {
            var result = await _batchReplyAppService.UploadSourceAsync(User, file, HttpContext.RequestAborted);
            return Success(result, "来源文件上传成功");
        }
        catch (ApplicationServiceException ex)
        {
            return ex.Code == 404
                ? NotFoundResult<BatchReplySourceUploadResponse>(ex.Message)
                : Error<BatchReplySourceUploadResponse>(ex.Code, ex.Message);
        }
    }

    [HttpGet("sessions/{sessionId}/tables")]
    [ProducesResponseType(typeof(ApiResponse<List<TableInfoDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<List<TableInfoDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<List<TableInfoDto>>>> GetTables(string sessionId)
    {
        try
        {
            var result = await _batchReplyAppService.GetSourceTablesAsync(User, sessionId);
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
        [FromQuery] int previewRows = 0,
        [FromQuery] int headerRowIndex = 0,
        [FromQuery] int headerRowCount = 1,
        [FromQuery] int dataStartRowIndex = 1)
    {
        try
        {
            var result = await _batchReplyAppService.GetSourceTablePreviewAsync(
                User,
                sessionId,
                tableIndex,
                previewRows,
                headerRowIndex,
                headerRowCount,
                dataStartRowIndex);
            return Success(result);
        }
        catch (ApplicationServiceException ex)
        {
            return ex.Code == 404
                ? NotFoundResult<TableDataDto>(ex.Message)
                : Error<TableDataDto>(ex.Code, ex.Message);
        }
    }

    [HttpPost("preview")]
    [ProducesResponseType(typeof(ApiResponse<BatchReplyPreviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BatchReplyPreviewResponse>), StatusCodes.Status400BadRequest)]
    public Task<ActionResult<ApiResponse<BatchReplyPreviewResponse>>> Preview(
        [FromForm] string sessionId,
        [FromForm] string tableConfigsJson,
        [FromForm] List<IFormFile> targetFiles)
    {
        var tableConfigs = ParseTableConfigs(tableConfigsJson);
        return HandleAsync(() => _batchReplyAppService.PreviewAsync(
            User,
            sessionId,
            tableConfigs,
            targetFiles,
            HttpContext.RequestAborted));
    }

    [HttpPost("execute")]
    [AuditOperation("execute", "batch-reply")]
    [ProducesResponseType(typeof(ApiResponse<BatchReplyExecuteResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BatchReplyExecuteResponse>), StatusCodes.Status400BadRequest)]
    public Task<ActionResult<ApiResponse<BatchReplyExecuteResponse>>> Execute([FromBody] BatchReplyExecuteRequest request)
    {
        return HandleAsync(() => _batchReplyAppService.ExecuteAsync(User, request, HttpContext.RequestAborted));
    }

    [HttpGet("download/{taskId}")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Download(string taskId)
    {
        return HandleFileAsync(() => _batchReplyAppService.DownloadAsync(User, taskId, HttpContext.RequestAborted));
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
}
