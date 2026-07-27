using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using static AcceptanceSpecSystem.Application.Services.MatchingResultHelpers;

namespace AcceptanceSpecSystem.Application.Services;

public interface IMatchingTaskAppService
{
    Task<MatchingOperationResult<MatchingTaskStatusDto>> GetStatusAsync(
        MatchingUserContext user,
        string taskId,
        CancellationToken cancellationToken = default);

    Task<MatchingDownloadResult> DownloadAsync(
        MatchingUserContext user,
        string taskId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 匹配任务应用服务。
/// </summary>
public sealed class MatchingTaskAppService : IMatchingTaskAppService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMatchingResultWriteBackPort _matchingResultWriteBackService;
    private readonly IFileStorageService _fileStorage;
    private readonly MatchingTaskSnapshotService _matchingTaskSnapshotService;
    private readonly ILogger<MatchingTaskAppService> _logger;

    public MatchingTaskAppService(
        IUnitOfWork unitOfWork,
        IMatchingResultWriteBackPort matchingResultWriteBackService,
        IFileStorageService fileStorage,
        MatchingTaskSnapshotService matchingTaskSnapshotService,
        ILogger<MatchingTaskAppService> logger)
    {
        _unitOfWork = unitOfWork;
        _matchingResultWriteBackService = matchingResultWriteBackService;
        _fileStorage = fileStorage;
        _matchingTaskSnapshotService = matchingTaskSnapshotService;
        _logger = logger;
    }

    public async Task<MatchingOperationResult<MatchingTaskStatusDto>> GetStatusAsync(
        MatchingUserContext user,
        string taskId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = await _matchingTaskSnapshotService.LoadStatusAsync(
            user,
            taskId,
            cancellationToken);
        if (status == null)
        {
            throw NotFoundFailure("任务不存在或已过期");
        }

        return Result(status);
    }

    public async Task<MatchingDownloadResult> DownloadAsync(
        MatchingUserContext user,
        string taskId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var taskResult = await _matchingTaskSnapshotService.LoadAsync(user, taskId, cancellationToken);
        if (taskResult == null)
        {
            throw NotFoundFailure("任务不存在或已过期");
        }

        if (!string.IsNullOrWhiteSpace(taskResult.DownloadArtifactRelativePath))
        {
            try
            {
                var fullPath = _fileStorage.GetAbsolutePath(taskResult.DownloadArtifactRelativePath);
                if (!System.IO.File.Exists(fullPath))
                {
                    throw NotFoundFailure("下载文件不存在或已被清理");
                }

                cancellationToken.ThrowIfCancellationRequested();
                var content = _fileStorage.OpenReadStream(taskResult.DownloadArtifactRelativePath);
                var artifactContentType = string.IsNullOrWhiteSpace(taskResult.DownloadArtifactContentType)
                    ? "application/octet-stream"
                    : taskResult.DownloadArtifactContentType;
                var artifactDownloadFileName = string.IsNullOrWhiteSpace(taskResult.DownloadArtifactFileName)
                    ? Path.GetFileName(fullPath)
                    : taskResult.DownloadArtifactFileName;

                _logger.LogInformation("下载填充结果产物: 任务{TaskId}, 文件{FileName}", taskId, artifactDownloadFileName);
                return new MatchingDownloadResult(content, artifactContentType, artifactDownloadFileName);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (MatchingApiException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "下载填充结果产物失败: {TaskId}", taskId);
                throw Failure(500, $"下载结果失败: {ex.Message}");
            }
        }

        var wordFile = await _unitOfWork.WordFiles.GetByIdAsync(taskResult.SourceFileId);
        if (wordFile == null ||
            wordFile.CreatedByUserId != user.UserId ||
            wordFile.CompanyId != user.CompanyId)
        {
            throw NotFoundFailure("源文件不存在或无权访问");
        }

        byte[] resultContent;
        try
        {
            resultContent = await _matchingResultWriteBackService.RenderFilledContentAsync(
                wordFile,
                taskResult,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "填充文档失败: {TaskId}", taskId);
            throw Failure(500, $"填充文档失败: {ex.Message}");
        }

        var fileExtension = GetDownloadFileExtension(wordFile.FileType);
        var contentType = GetDownloadContentType(wordFile.FileType);
        var downloadFileName = Path.GetFileName(wordFile.FileName);
        if (string.IsNullOrWhiteSpace(downloadFileName))
        {
            downloadFileName = $"filled{fileExtension}";
        }

        Stream resultStream = new MemoryStream(resultContent, writable: false);
        try
        {
            await _matchingTaskSnapshotService.PersistDownloadArtifactAsync(
                taskId,
                taskResult,
                downloadFileName,
                contentType,
                resultContent,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(taskResult.DownloadArtifactRelativePath))
            {
                var persistedStream = _fileStorage.OpenReadStream(taskResult.DownloadArtifactRelativePath);
                resultStream.Dispose();
                resultStream = persistedStream;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            resultStream.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "缓存填充下载产物失败，将继续返回内存结果: {TaskId}", taskId);
        }

        _logger.LogInformation("下载填充结果: 任务{TaskId}, 文件{FileName}", taskId, downloadFileName);
        return new MatchingDownloadResult(resultStream, contentType, downloadFileName);
    }

    private static string GetDownloadFileExtension(UploadedFileType fileType)
    {
        return fileType == UploadedFileType.ExcelXlsx ? ".xlsx" : ".docx";
    }

    private static string GetDownloadContentType(UploadedFileType fileType)
    {
        return fileType == UploadedFileType.ExcelXlsx
            ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            : "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    }
}
