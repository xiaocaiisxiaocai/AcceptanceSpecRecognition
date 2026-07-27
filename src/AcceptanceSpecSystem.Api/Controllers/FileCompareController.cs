using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 文件对比 HTTP 适配器。
/// </summary>
[Route("api/file-compare")]
[Authorize]
public class FileCompareController : BaseApiController
{
    private static readonly JsonSerializerOptions PreviewJsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly IFileCompareAppService _appService;
    private readonly IAuthDataScopeService _authDataScopeService;
    private readonly IFileCompareTemporaryStorage _temporaryStorage;
    private readonly IResourceBudgetGovernor _resourceBudgetGovernor;

    public FileCompareController(
        IFileCompareAppService appService,
        IAuthDataScopeService authDataScopeService,
        IFileCompareTemporaryStorage temporaryStorage,
        IResourceBudgetGovernor resourceBudgetGovernor)
    {
        _appService = appService;
        _authDataScopeService = authDataScopeService;
        _temporaryStorage = temporaryStorage;
        _resourceBudgetGovernor = resourceBudgetGovernor;
    }

    [HttpPost("upload")]
    [AuditOperation("upload", "file-compare")]
    [ProducesResponseType(typeof(ApiResponse<FileCompareUploadResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<FileCompareUploadResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<FileCompareUploadResponse>), StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<ApiResponse<FileCompareUploadResponse>>> Upload(
        IFormFile fileA,
        IFormFile fileB,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return Error<FileCompareUploadResponse>(401, "会话缺少用户上下文");
        if (fileA == null || fileB == null)
            return Error<FileCompareUploadResponse>(400, "请上传两份文件");

        try
        {
            await using var uploadStreamA = fileA.OpenReadStream();
            await using var stagedA = await _temporaryStorage.StageUploadAsync(
                uploadStreamA, UploadFileValidation.MaxAllowedFileSizeBytes, cancellationToken);
            using var validationA = stagedA.OpenRead();
            var fileTypeA = UploadFileValidation.ValidateOfficeDocument(
                fileA.FileName, validationA, allowExcel: true, allowWord: true);

            await using var uploadStreamB = fileB.OpenReadStream();
            await using var stagedB = await _temporaryStorage.StageUploadAsync(
                uploadStreamB, UploadFileValidation.MaxAllowedFileSizeBytes, cancellationToken);
            using var validationB = stagedB.OpenRead();
            var fileTypeB = UploadFileValidation.ValidateOfficeDocument(
                fileB.FileName, validationB, allowExcel: true, allowWord: true);

            var uploadA = new FileCompareUploadDocument(
                fileA.FileName, fileTypeA, stagedA.Length, stagedA.Sha256, stagedA);
            var uploadB = new FileCompareUploadDocument(
                fileB.FileName, fileTypeB, stagedB.Length, stagedB.Sha256, stagedB);
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
    [ProducesResponseType(typeof(ApiResponse<FileComparePreviewResponse>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Preview(
        [FromBody] FileComparePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return ErrorResult(401, "会话缺少用户上下文");

        TemporaryFileLease? output = null;
        try
        {
            using var operation = await _appService.PreviewAsync(
                scope.ToAccessContext(), request, cancellationToken);
            output = await _temporaryStorage.CreateOutputAsync(cancellationToken);
            await using (var raw = output.OpenWrite())
            await using (var bounded = new FileCompareResultWriteStream(
                             raw,
                             _resourceBudgetGovernor))
            {
                await JsonSerializer.SerializeAsync(
                    bounded,
                    ApiResponse<FileComparePreviewResponse>.Success(operation.Response),
                    PreviewJsonOptions,
                    cancellationToken);
                await bounded.FlushAsync(cancellationToken);
            }
            var content = new LeaseOwnedReadStream(output.OpenRead(), output);
            output = null;
            return File(content, "application/json", enableRangeProcessing: false);
        }
        catch (ApplicationServiceException ex)
        {
            if (output is not null)
                await output.DisposeAsync();
            return ErrorResult(ex.Code, ex.Message);
        }
        catch
        {
            if (output is not null)
                await output.DisposeAsync();
            throw;
        }
    }

    [HttpPost("download")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Download(
        [FromBody] FileComparePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
            return ErrorResult(401, "会话缺少用户上下文");

        try
        {
            var result = await _appService.DownloadAsync(scope.ToAccessContext(), request, cancellationToken);
            return File(result.Content, result.ContentType, result.FileName, enableRangeProcessing: false);
        }
        catch (ApplicationServiceException ex)
        {
            return ErrorResult(ex.Code, ex.Message);
        }
    }

    private async Task<DataScopeResult?> ResolveSpecScopeAsync()
    {
        return await SpecDataScopeHelper.ResolveScopeAsync(User, _authDataScopeService);
    }

}
