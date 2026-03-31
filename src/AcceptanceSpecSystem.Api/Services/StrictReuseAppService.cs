using System.IO.Compression;
using System.Security.Claims;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 严格复用应用服务。
/// </summary>
public sealed class StrictReuseAppService
{
    private readonly DocumentFileAccessService _documentFileAccessService;
    private readonly DocumentTableAccessService _documentTableAccessService;
    private readonly MatchingResultWriteBackService _matchingResultWriteBackService;
    private readonly IFileStorageService _fileStorage;
    private readonly IAuthDataScopeService _authDataScopeService;
    private readonly MatchingTaskSnapshotService _matchingTaskSnapshotService;
    private readonly ILogger<StrictReuseAppService> _logger;

    public StrictReuseAppService(
        DocumentFileAccessService documentFileAccessService,
        DocumentTableAccessService documentTableAccessService,
        MatchingResultWriteBackService matchingResultWriteBackService,
        IFileStorageService fileStorage,
        IAuthDataScopeService authDataScopeService,
        MatchingTaskSnapshotService matchingTaskSnapshotService,
        ILogger<StrictReuseAppService> logger)
    {
        _documentFileAccessService = documentFileAccessService;
        _documentTableAccessService = documentTableAccessService;
        _matchingResultWriteBackService = matchingResultWriteBackService;
        _fileStorage = fileStorage;
        _authDataScopeService = authDataScopeService;
        _matchingTaskSnapshotService = matchingTaskSnapshotService;
        _logger = logger;
    }

    private static MatchingOperationResult<T> Result<T>(T data, string message = "操作成功")
    {
        return new MatchingOperationResult<T>(data, message);
    }

    private static MatchingApiException Failure(int code, string message)
    {
        return new MatchingApiException(code, message);
    }

    private static MatchingApiException NotFoundFailure(string message)
    {
        return new MatchingApiException(404, message, isNotFound: true);
    }

    public async Task<MatchingOperationResult<StrictReusePreviewResponse>> PreviewStrictReuseAsync(
        ClaimsPrincipal user,
        StrictReusePreviewRequest request)
    {
        if (request.TargetFileIds == null || request.TargetFileIds.Count == 0)
        {
            throw Failure(400, "请至少提供一个目标文件");
        }

        var sourceTask = await _matchingTaskSnapshotService.LoadAsync(user, request.SourceTaskId);
        if (sourceTask?.StrictReuseSession == null || sourceTask.StrictReuseSession.Tables.Count == 0)
        {
            throw Failure(400, "当前填充任务不支持严格复用，请重新执行一次填充后再试");
        }

        var scope = await ResolveSpecScopeAsync(user);
        if (scope == null)
        {
            throw Failure(401, "会话缺少用户上下文");
        }

        var results = new List<StrictReusePreviewFileResult>();
        foreach (var fileId in request.TargetFileIds.Distinct())
        {
            var targetFile = await _documentFileAccessService.GetAccessibleWordFileAsync(fileId, scope);
            if (targetFile == null)
            {
                results.Add(new StrictReusePreviewFileResult
                {
                    FileId = fileId,
                    FileName = $"文件{fileId}",
                    CanApply = false,
                    Errors = ["目标文件不存在"]
                });
                continue;
            }

            var errors = await ValidateStrictReuseTargetFileAsync(targetFile, sourceTask.StrictReuseSession);
            results.Add(new StrictReusePreviewFileResult
            {
                FileId = targetFile.Id,
                FileName = targetFile.FileName,
                CanApply = errors.Count == 0,
                Errors = errors
            });
        }

        return Result(new StrictReusePreviewResponse
        {
            SourceTaskId = sourceTask.TaskId,
            SourceFileName = sourceTask.StrictReuseSession.SourceFileName,
            SourceFileType = sourceTask.StrictReuseSession.SourceFileType,
            Files = results
        });
    }

    public async Task<MatchingOperationResult<StrictReuseExecuteResponse>> ExecuteStrictReuseAsync(
        ClaimsPrincipal user,
        StrictReuseExecuteRequest request)
    {
        if (request.TargetFileIds == null || request.TargetFileIds.Count == 0)
        {
            throw Failure(400, "请至少提供一个目标文件");
        }

        var sourceTask = await _matchingTaskSnapshotService.LoadAsync(user, request.SourceTaskId);
        if (sourceTask?.StrictReuseSession == null || sourceTask.StrictReuseSession.Tables.Count == 0)
        {
            throw Failure(400, "当前填充任务不支持严格复用，请重新执行一次填充后再试");
        }

        var scope = await ResolveSpecScopeAsync(user);
        if (scope == null)
        {
            throw Failure(401, "会话缺少用户上下文");
        }

        var executableTargets = new List<StrictReuseGeneratedFile>();
        var fileResults = new List<StrictReuseExecuteFileResult>();

        foreach (var fileId in request.TargetFileIds.Distinct())
        {
            var targetFile = await _documentFileAccessService.GetAccessibleWordFileAsync(fileId, scope);
            if (targetFile == null)
            {
                fileResults.Add(new StrictReuseExecuteFileResult
                {
                    FileId = fileId,
                    FileName = $"文件{fileId}",
                    Success = false,
                    Message = "目标文件不存在"
                });
                continue;
            }

            var errors = await ValidateStrictReuseTargetFileAsync(targetFile, sourceTask.StrictReuseSession);
            if (errors.Count > 0)
            {
                fileResults.Add(new StrictReuseExecuteFileResult
                {
                    FileId = targetFile.Id,
                    FileName = targetFile.FileName,
                    Success = false,
                    Message = string.Join("；", errors)
                });
                continue;
            }

            try
            {
                var generated = await _matchingResultWriteBackService.GenerateStrictReuseTargetFileAsync(
                    targetFile,
                    sourceTask.StrictReuseSession);
                executableTargets.Add(generated);
                fileResults.Add(new StrictReuseExecuteFileResult
                {
                    FileId = targetFile.Id,
                    FileName = targetFile.FileName,
                    Success = true,
                    Message = "复用成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "严格复用执行失败: sourceTask={SourceTaskId}, targetFile={FileId}", request.SourceTaskId, targetFile.Id);
                fileResults.Add(new StrictReuseExecuteFileResult
                {
                    FileId = targetFile.Id,
                    FileName = targetFile.FileName,
                    Success = false,
                    Message = $"复用失败: {ex.Message}"
                });
            }
        }

        if (executableTargets.Count == 0)
        {
            throw Failure(400, "没有可执行严格复用的目标文件");
        }

        var artifact = await SaveStrictReuseArtifactAsync(sourceTask.StrictReuseSession, executableTargets);
        var taskId = Guid.NewGuid().ToString("N");
        var taskResult = new FillTaskResult
        {
            TaskId = taskId,
            SourceFileId = sourceTask.SourceFileId,
            CreatedAt = DateTime.UtcNow,
            DownloadArtifactRelativePath = artifact.RelativePath,
            DownloadArtifactFileName = artifact.FileName,
            DownloadArtifactContentType = artifact.ContentType
        };

        await _matchingTaskSnapshotService.SaveAsync(user, taskResult);

        var response = new StrictReuseExecuteResponse
        {
            TaskId = taskId,
            SuccessCount = fileResults.Count(item => item.Success),
            FailedCount = fileResults.Count(item => !item.Success),
            DownloadUrl = $"/api/matching/download/{taskId}",
            DownloadFileName = artifact.FileName,
            Files = fileResults
        };

        return Result(response, response.FailedCount > 0
            ? $"严格复用完成：成功{response.SuccessCount}份，失败{response.FailedCount}份"
            : $"严格复用完成：成功{response.SuccessCount}份");
    }

    private async Task<List<string>> ValidateStrictReuseTargetFileAsync(
        WordFile targetFile,
        StrictReuseSession session)
    {
        var errors = new List<string>();
        if (targetFile.FileType != session.SourceFileType)
        {
            errors.Add("文件类型不一致");
            return errors;
        }

        IReadOnlyList<AcceptanceSpecSystem.Core.Documents.Models.TableInfo> targetTables;
        try
        {
            targetTables = await _documentTableAccessService.GetTablesAsync(targetFile);
        }
        catch (ApplicationServiceException ex)
        {
            errors.Add(ex.Message);
            return errors;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "严格复用预检读取目标文件失败: fileId={FileId}", targetFile.Id);
            errors.Add($"读取目标文件失败: {ex.Message}");
            return errors;
        }

        foreach (var sourceTable in session.Tables.OrderBy(table => table.TableIndex))
        {
            if (sourceTable.TableIndex < 0 || sourceTable.TableIndex >= targetTables.Count)
            {
                errors.Add($"表格{sourceTable.TableIndex + 1}不存在");
                continue;
            }

            var targetTable = targetTables[sourceTable.TableIndex];
            var requiredMaxColumnIndex = new[]
            {
                sourceTable.ProjectColumnIndex,
                sourceTable.SpecificationColumnIndex,
                sourceTable.AcceptanceColumnIndex,
                sourceTable.RemarkColumnIndex ?? -1
            }.Max();

            if (requiredMaxColumnIndex >= targetTable.ColumnCount)
            {
                errors.Add($"表格{sourceTable.TableIndex + 1}列配置超出目标文件范围");
                continue;
            }

            var targetRows = await _documentTableAccessService.ExtractMatchSourceItemsAsync(
                targetFile,
                sourceTable.TableIndex,
                sourceTable.ProjectColumnIndex,
                sourceTable.SpecificationColumnIndex,
                sourceTable.HeaderRowStart,
                sourceTable.HeaderRowCount,
                sourceTable.DataStartRow,
                sourceTable.FilterEmptySourceRows);

            if (targetRows.Count != sourceTable.RowSignatures.Count)
            {
                errors.Add($"表格{sourceTable.TableIndex + 1}的数据区行数不一致");
                continue;
            }

            for (var index = 0; index < sourceTable.RowSignatures.Count; index++)
            {
                var expected = sourceTable.RowSignatures[index];
                var actual = targetRows[index];
                if (actual.RowIndex != expected.RowIndex ||
                    !StrictReuseTextEquals(actual.Project, expected.Project) ||
                    !StrictReuseTextEquals(actual.Specification, expected.Specification))
                {
                    errors.Add($"表格{sourceTable.TableIndex + 1}第{index + 1}行的项目/规格顺序不一致");
                    break;
                }
            }
        }

        return errors;
    }

    private async Task<SavedDownloadArtifact> SaveStrictReuseArtifactAsync(
        StrictReuseSession session,
        List<StrictReuseGeneratedFile> generatedFiles)
    {
        if (generatedFiles.Count == 0)
        {
            throw new InvalidOperationException("没有可保存的严格复用结果");
        }

        if (generatedFiles.Count == 1)
        {
            var file = generatedFiles[0];
            var relativePath = await _fileStorage.SaveFilledWordAsync(file.FileName, file.Content);
            return new SavedDownloadArtifact
            {
                RelativePath = relativePath,
                FileName = file.FileName,
                ContentType = file.ContentType
            };
        }

        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var usedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in generatedFiles.OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase))
            {
                var entryName = BuildUniqueArchiveEntryName(file.FileName, usedEntryNames);
                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(file.Content);
            }
        }

        var baseName = Path.GetFileNameWithoutExtension(session.SourceFileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "严格复用结果";
        }

        var downloadFileName = $"{baseName}_严格复用结果.zip";
        var relativePathForZip = await _fileStorage.SaveFilledWordAsync(downloadFileName, zipStream.ToArray());
        return new SavedDownloadArtifact
        {
            RelativePath = relativePathForZip,
            FileName = downloadFileName,
            ContentType = "application/zip"
        };
    }

    private static string BuildUniqueArchiveEntryName(string fileName, HashSet<string> usedEntryNames)
    {
        var normalizedFileName = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "filled.docx" : fileName.Trim());
        if (string.IsNullOrWhiteSpace(normalizedFileName))
        {
            normalizedFileName = "filled.docx";
        }

        if (usedEntryNames.Add(normalizedFileName))
        {
            return normalizedFileName;
        }

        var baseName = Path.GetFileNameWithoutExtension(normalizedFileName);
        var extension = Path.GetExtension(normalizedFileName);
        var counter = 2;
        while (true)
        {
            var candidate = $"{baseName} ({counter}){extension}";
            if (usedEntryNames.Add(candidate))
            {
                return candidate;
            }

            counter++;
        }
    }

    private static bool StrictReuseTextEquals(string? left, string? right)
    {
        return string.Equals(
            NormalizeForDedup(left),
            NormalizeForDedup(right),
            StringComparison.Ordinal);
    }

    private static string NormalizeForDedup(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(" ", value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private async Task<DataScopeResult?> ResolveSpecScopeAsync(ClaimsPrincipal user)
    {
        return await SpecDataScopeHelper.ResolveScopeAsync(user, _authDataScopeService);
    }
}
