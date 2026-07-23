using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AcceptanceSpecSystem.Api.Controllers;

[Route("api/documents")]
public class DocumentsController : BaseApiController
{
    // 文件级范围校验已下沉到应用服务和共享访问组件，底层仍统一复用 WordFileDataScopeHelper。
    private readonly IAuthDataScopeService _authDataScopeService;
    private readonly IDocumentFileAppService _documentFileAppService;
    private readonly IDocumentTableQueryAppService _documentTableQueryAppService;
    private readonly IDocumentImportAppService _documentImportAppService;

    public DocumentsController(
        IAuthDataScopeService authDataScopeService,
        IDocumentFileAppService documentFileAppService,
        IDocumentTableQueryAppService documentTableQueryAppService,
        IDocumentImportAppService documentImportAppService)
    {
        _authDataScopeService = authDataScopeService;
        _documentFileAppService = documentFileAppService;
        _documentTableQueryAppService = documentTableQueryAppService;
        _documentImportAppService = documentImportAppService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedData<WordFileDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedData<WordFileDto>>>> GetFiles(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
        {
            return Error<PagedData<WordFileDto>>(401, "会话缺少用户上下文");
        }

        var result = await _documentFileAppService.GetFilesAsync(
            scope.ToAccessContext(),
            page,
            pageSize,
            keyword,
            cancellationToken);
        return Success(result);
    }

    [HttpPost("upload")]
    [AuditOperation("upload", "document")]
    [EnableRateLimiting("upload")]
    [ProducesResponseType(typeof(ApiResponse<FileUploadResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<FileUploadResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<FileUploadResponse>>> UploadFile(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
        {
            return Error<FileUploadResponse>(401, "会话缺少用户上下文");
        }

        try
        {
            var fileType = UploadFileValidation.ValidateOfficeDocument(file, allowExcel: true, allowWord: true);
            byte[] content;
            using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream, HttpContext.RequestAborted);
                content = memoryStream.ToArray();
            }

            // 使用 HttpContext.RequestAborted 作为取消令牌，确保客户端断开时终止文件上传处理
            var result = await _documentFileAppService.UploadFileAsync(
                scope.ToAccessContext(),
                new DocumentUploadCommand(file.FileName, fileType, content),
                HttpContext.RequestAborted);
            return Success(result, "文件上传成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<FileUploadResponse>(ex.Code, ex.Message);
        }
    }

    [HttpGet("{id}/tables")]
    [ProducesResponseType(typeof(ApiResponse<List<TableInfoDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<List<TableInfoDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<List<TableInfoDto>>>> GetTables(
        int id,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
        {
            return Error<List<TableInfoDto>>(401, "会话缺少用户上下文");
        }

        try
        {
            var result = await _documentTableQueryAppService.GetTablesAsync(
                scope.ToAccessContext(), id, cancellationToken);
            return Success(result);
        }
        catch (ApplicationServiceException ex) when (ex.Code == 404)
        {
            return NotFoundResult<List<TableInfoDto>>(ex.Message);
        }
        catch (ApplicationServiceException ex)
        {
            return Error<List<TableInfoDto>>(ex.Code, ex.Message);
        }
    }

    [HttpGet("{id}/tables/{tableIndex}/preview")]
    [ProducesResponseType(typeof(ApiResponse<TableDataDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<TableDataDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TableDataDto>>> GetTablePreview(
        int id,
        int tableIndex,
        [FromQuery] int previewRows = 100,
        [FromQuery] int headerRowIndex = 0,
        [FromQuery] int headerRowCount = 1,
        [FromQuery] int dataStartRowIndex = 1,
        [FromQuery] int? dataEndRowIndex = null,
        [FromQuery] int? rowOffset = null,
        [FromQuery] int? columnOffset = null,
        [FromQuery] int? previewColumns = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
        {
            return Error<TableDataDto>(401, "会话缺少用户上下文");
        }

        try
        {
            var result = await _documentTableQueryAppService.GetPreviewAsync(
                scope.ToAccessContext(),
                id,
                tableIndex,
                previewRows,
                headerRowIndex,
                headerRowCount,
                dataStartRowIndex,
                dataEndRowIndex,
                rowOffset,
                columnOffset,
                previewColumns,
                cancellationToken);
            return Success(result);
        }
        catch (ApplicationServiceException ex) when (ex.Code == 404)
        {
            return NotFoundResult<TableDataDto>(ex.Message);
        }
        catch (ApplicationServiceException ex)
        {
            return Error<TableDataDto>(ex.Code, ex.Message);
        }
    }

    [HttpPost("import")]
    [AuditOperation("import", "document")]
    [EnableRateLimiting("ai-heavy")]
    [ProducesResponseType(typeof(ApiResponse<ImportResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ImportResult>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ImportResult>>> ImportData(
        [FromBody] ImportDataRequest request,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
        {
            return Error<ImportResult>(401, "会话缺少用户上下文");
        }

        try
        {
            var importResult = await _documentImportAppService.ImportWordAsync(
                scope.ToAccessContext(),
                request,
                cancellationToken);
            return Success(importResult.Result, importResult.Message);
        }
        catch (ApplicationServiceException ex)
        {
            return Error<ImportResult>(ex.Code, ex.Message);
        }
    }

    [HttpPost("excel/import")]
    [AuditOperation("import", "excel-document")]
    [EnableRateLimiting("ai-heavy")]
    [ProducesResponseType(typeof(ApiResponse<ImportResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ImportResult>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ImportResult>>> ImportExcelData(
        [FromBody] ExcelImportDataRequest request,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
        {
            return Error<ImportResult>(401, "会话缺少用户上下文");
        }

        try
        {
            var importResult = await _documentImportAppService.ImportExcelAsync(
                scope.ToAccessContext(),
                request,
                cancellationToken);
            return Success(importResult.Result, importResult.Message);
        }
        catch (ApplicationServiceException ex)
        {
            return Error<ImportResult>(ex.Code, ex.Message);
        }
    }

    [HttpDelete("{id}")]
    [AuditOperation("delete", "document")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteFile(
        int id,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
        {
            return Error(401, "会话缺少用户上下文");
        }

        try
        {
            await _documentFileAppService.DeleteFileAsync(scope.ToAccessContext(), id, cancellationToken);
            return Success("删除成功");
        }
        catch (ApplicationServiceException ex) when (ex.Code == 404)
        {
            return NotFound(ApiResponse.Error(404, ex.Message));
        }
        catch (ApplicationServiceException ex)
        {
            return Error(ex.Code, ex.Message);
        }
    }

    private async Task<DataScopeResult?> ResolveSpecScopeAsync()
    {
        return await SpecDataScopeHelper.ResolveScopeAsync(User, _authDataScopeService);
    }
}
