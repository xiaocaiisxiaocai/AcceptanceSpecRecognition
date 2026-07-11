using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 文件对比 HTTP 适配器。
/// </summary>
[Route("api/file-compare")]
[Authorize]
public class FileCompareController : BaseApiController
{
    private readonly IFileCompareAppService _appService;
    private readonly IAuthDataScopeService _authDataScopeService;

    public FileCompareController(
        IFileCompareAppService appService,
        IAuthDataScopeService authDataScopeService)
    {
        _appService = appService;
        _authDataScopeService = authDataScopeService;
    }

    [HttpPost("upload")]
    [AuditOperation("upload", "file-compare")]
    [ProducesResponseType(typeof(ApiResponse<FileCompareUploadResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<FileCompareUploadResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<FileCompareUploadResponse>>> Upload(
        IFormFile fileA,
        IFormFile fileB,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<FileCompareUploadResponse>(401, "会话缺少用户上下文");
        if (fileA == null || fileA.Length == 0 || fileB == null || fileB.Length == 0)
            return Error<FileCompareUploadResponse>(400, "请上传两份文件");

        try
        {
            var fileTypeA = UploadFileValidation.ValidateOfficeDocument(fileA, allowExcel: true, allowWord: true);
            var fileTypeB = UploadFileValidation.ValidateOfficeDocument(fileB, allowExcel: true, allowWord: true);
            var uploadA = new FileCompareUploadDocument(fileA.FileName, fileTypeA, await ReadContentAsync(fileA, cancellationToken));
            var uploadB = new FileCompareUploadDocument(fileB.FileName, fileTypeB, await ReadContentAsync(fileB, cancellationToken));
            var result = await _appService.UploadAsync(scope.ToAccessContext(), uploadA, uploadB, cancellationToken);
            return Success(result, "上传成功");
        }
        catch (ApplicationServiceException ex)
        {
            return Error<FileCompareUploadResponse>(ex.Code, ex.Message);
        }
    }

    [HttpPost("preview")]
    [ProducesResponseType(typeof(ApiResponse<FileComparePreviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<FileComparePreviewResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<FileComparePreviewResponse>>> Preview(
        [FromBody] FileComparePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<FileComparePreviewResponse>(401, "会话缺少用户上下文");

        try
        {
            var result = await _appService.PreviewAsync(scope.ToAccessContext(), request, cancellationToken);
            return Success(result);
        }
        catch (ApplicationServiceException ex)
        {
            return Error<FileComparePreviewResponse>(ex.Code, ex.Message);
        }
    }

    [HttpPost("download")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Download(
        [FromBody] FileComparePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return BadRequest(ApiResponse.Error(401, "会话缺少用户上下文"));

        try
        {
            var result = await _appService.DownloadAsync(scope.ToAccessContext(), request, cancellationToken);
            return File(result.Content, result.ContentType, result.FileName, enableRangeProcessing: false);
        }
        catch (ApplicationServiceException ex)
        {
            return BadRequest(ApiResponse.Error(ex.Code, ex.Message));
        }
    }

    private async Task<DataScopeResult?> ResolveSpecScopeAsync()
    {
        return await SpecDataScopeHelper.ResolveScopeAsync(User, _authDataScopeService);
    }

    private static async Task<byte[]> ReadContentAsync(IFormFile file, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);
        return stream.ToArray();
    }
}
