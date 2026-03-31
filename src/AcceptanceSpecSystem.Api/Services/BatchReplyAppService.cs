using System.IO.Compression;
using System.Security.Claims;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 批量回复应用服务。
/// </summary>
public sealed class BatchReplyAppService
{
    private readonly DocumentTableAccessService _documentTableAccessService;
    private readonly MatchingResultWriteBackService _matchingResultWriteBackService;
    private readonly BatchReplySessionService _batchReplySessionService;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<BatchReplyAppService> _logger;

    public BatchReplyAppService(
        DocumentTableAccessService documentTableAccessService,
        MatchingResultWriteBackService matchingResultWriteBackService,
        BatchReplySessionService batchReplySessionService,
        IFileStorageService fileStorage,
        ILogger<BatchReplyAppService> logger)
    {
        _documentTableAccessService = documentTableAccessService;
        _matchingResultWriteBackService = matchingResultWriteBackService;
        _batchReplySessionService = batchReplySessionService;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<BatchReplySourceUploadResponse> UploadSourceAsync(
        ClaimsPrincipal user,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        var owner = ResolveOwnerForApplication(user);
        var fileType = UploadFileValidation.ValidateOfficeDocument(file, allowExcel: true, allowWord: true);

        byte[] content;
        using (var memoryStream = new MemoryStream())
        {
            await file.CopyToAsync(memoryStream, cancellationToken);
            content = memoryStream.ToArray();
        }

        var tableCount = await _documentTableAccessService.CountTablesAsync(fileType, content);
        var session = await _batchReplySessionService.CreateSourceSessionAsync(
            owner.UserId,
            owner.CompanyId,
            file.FileName,
            fileType,
            content,
            cancellationToken);

        return new BatchReplySourceUploadResponse
        {
            SessionId = session.SessionId,
            SourceFileName = session.SourceFileName,
            SourceFileType = session.SourceFileType,
            TableCount = tableCount
        };
    }

    public async Task<List<TableInfoDto>> GetSourceTablesAsync(ClaimsPrincipal user, string sessionId)
    {
        var session = GetSourceSessionForApplication(user, sessionId);
        var sourceFile = CreateTemporaryWordFile(session.SourceFileName, session.SourceFileType, session.SourceFileRelativePath);
        return await _documentTableAccessService.GetTableInfoDtosAsync(sourceFile);
    }

    public async Task<TableDataDto> GetSourceTablePreviewAsync(
        ClaimsPrincipal user,
        string sessionId,
        int tableIndex,
        int previewRows,
        int headerRowIndex,
        int headerRowCount,
        int dataStartRowIndex)
    {
        var session = GetSourceSessionForApplication(user, sessionId);
        var sourceFile = CreateTemporaryWordFile(session.SourceFileName, session.SourceFileType, session.SourceFileRelativePath);
        return await _documentTableAccessService.GetTablePreviewAsync(
            sourceFile,
            tableIndex,
            previewRows,
            headerRowIndex,
            headerRowCount,
            dataStartRowIndex);
    }

    public async Task<MatchingOperationResult<BatchReplyPreviewResponse>> PreviewAsync(
        ClaimsPrincipal user,
        string sessionId,
        IReadOnlyCollection<BatchTableConfig> tableConfigs,
        IReadOnlyCollection<IFormFile> targetFiles,
        CancellationToken cancellationToken = default)
    {
        var owner = ResolveOwnerForMatching(user);
        if (tableConfigs == null || tableConfigs.Count == 0)
        {
            throw Failure(400, "请至少配置一个来源表格");
        }

        if (targetFiles == null || targetFiles.Count == 0)
        {
            throw Failure(400, "请至少上传一个目标文件");
        }

        var session = GetSourceSessionForMatching(owner, sessionId);
        var normalizedConfigs = NormalizeTableConfigs(tableConfigs);
        var sourceFile = CreateTemporaryWordFile(session.SourceFileName, session.SourceFileType, session.SourceFileRelativePath);
        var sourceTableMetas = await _documentTableAccessService.GetTablesAsync(sourceFile);
        var sourceTables = await BuildSourceTablesAsync(sourceFile, sourceTableMetas, normalizedConfigs);

        var previewFiles = new List<BatchReplyTargetFile>();
        foreach (var targetFile in targetFiles)
        {
            previewFiles.Add(await BuildPreviewTargetAsync(targetFile, session.SourceFileType, sourceTables, cancellationToken));
        }

        await _batchReplySessionService.ReplacePreviewAsync(
            owner.UserId,
            owner.CompanyId,
            session.SessionId,
            sourceTables,
            previewFiles,
            cancellationToken);

        return Result(new BatchReplyPreviewResponse
        {
            SessionId = session.SessionId,
            SourceFileName = session.SourceFileName,
            SourceFileType = session.SourceFileType,
            Files = previewFiles.Select(file => new BatchReplyPreviewFileResult
            {
                TargetId = file.TargetId,
                FileName = file.FileName,
                CanApply = file.CanApply,
                Errors = file.Errors.ToList()
            }).ToList()
        }, previewFiles.Any(file => !file.CanApply)
            ? $"预检完成：可应用{previewFiles.Count(file => file.CanApply)}份，不可应用{previewFiles.Count(file => !file.CanApply)}份"
            : $"预检完成：可应用{previewFiles.Count}份");
    }

    public async Task<MatchingOperationResult<BatchReplyExecuteResponse>> ExecuteAsync(
        ClaimsPrincipal user,
        BatchReplyExecuteRequest request,
        CancellationToken cancellationToken = default)
    {
        var owner = ResolveOwnerForMatching(user);
        var session = GetSourceSessionForMatching(owner, request.SessionId);
        if (session.SourceTables.Count == 0 || session.TargetFiles.Count == 0)
        {
            throw Failure(400, "请先完成预检后再执行批量回复");
        }

        var generatedFiles = new List<StrictReuseGeneratedFile>();
        var executeResults = new List<BatchReplyExecuteFileResult>();
        foreach (var target in session.TargetFiles)
        {
            if (string.IsNullOrWhiteSpace(target.RelativePath) || !target.FileType.HasValue)
            {
                executeResults.Add(new BatchReplyExecuteFileResult
                {
                    TargetId = target.TargetId,
                    FileName = target.FileName,
                    Success = false,
                    Message = target.Errors.Count > 0 ? string.Join("；", target.Errors) : "目标文件不可用"
                });
                continue;
            }

            var targetWordFile = CreateTemporaryWordFile(target.FileName, target.FileType.Value, target.RelativePath);
            var errors = await ValidateTargetFileAsync(targetWordFile, session.SourceTables);
            if (errors.Count > 0)
            {
                executeResults.Add(new BatchReplyExecuteFileResult
                {
                    TargetId = target.TargetId,
                    FileName = target.FileName,
                    Success = false,
                    Message = string.Join("；", errors)
                });
                continue;
            }

            try
            {
                var generated = await _matchingResultWriteBackService.GenerateBatchReplyTargetFileAsync(
                    targetWordFile,
                    session.SourceTables,
                    cancellationToken);
                generatedFiles.Add(generated);
                executeResults.Add(new BatchReplyExecuteFileResult
                {
                    TargetId = target.TargetId,
                    FileName = target.FileName,
                    Success = true,
                    Message = "批量回复成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量回复执行失败: session={SessionId}, target={TargetId}", session.SessionId, target.TargetId);
                executeResults.Add(new BatchReplyExecuteFileResult
                {
                    TargetId = target.TargetId,
                    FileName = target.FileName,
                    Success = false,
                    Message = $"批量回复失败: {ex.Message}"
                });
            }
        }

        if (generatedFiles.Count == 0)
        {
            throw Failure(400, "没有可执行批量回复的目标文件");
        }

        var taskId = Guid.NewGuid().ToString("N");
        var artifact = await SaveDownloadArtifactAsync(taskId, session.SourceFileName, generatedFiles, cancellationToken);
        await _batchReplySessionService.SaveDownloadArtifactAsync(
            owner.UserId,
            owner.CompanyId,
            artifact,
            cancellationToken);

        var response = new BatchReplyExecuteResponse
        {
            TaskId = taskId,
            SuccessCount = executeResults.Count(item => item.Success),
            FailedCount = executeResults.Count(item => !item.Success),
            DownloadUrl = $"/api/batch-reply/download/{taskId}",
            DownloadFileName = artifact.FileName,
            Files = executeResults
        };

        return Result(response, response.FailedCount > 0
            ? $"批量回复完成：成功{response.SuccessCount}份，失败{response.FailedCount}份"
            : $"批量回复完成：成功{response.SuccessCount}份");
    }

    public async Task<MatchingDownloadResult> DownloadAsync(ClaimsPrincipal user, string taskId, CancellationToken cancellationToken = default)
    {
        var owner = ResolveOwnerForMatching(user);
        var artifact = _batchReplySessionService.GetDownloadArtifact(owner.UserId, owner.CompanyId, taskId);
        if (artifact == null)
        {
            throw NotFoundFailure("任务不存在或已过期");
        }

        try
        {
            var fullPath = _fileStorage.GetAbsolutePath(artifact.RelativePath);
            if (!File.Exists(fullPath))
            {
                throw NotFoundFailure("下载文件不存在或已被清理");
            }

            var content = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            return new MatchingDownloadResult(content, artifact.ContentType, artifact.FileName);
        }
        catch (MatchingApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量回复下载失败: {TaskId}", taskId);
            throw Failure(500, $"下载结果失败: {ex.Message}");
        }
    }

    private async Task<List<BatchReplySourceTable>> BuildSourceTablesAsync(
        WordFile sourceFile,
        IReadOnlyList<AcceptanceSpecSystem.Core.Documents.Models.TableInfo> sourceTableMetas,
        IReadOnlyCollection<BatchTableConfig> tableConfigs)
    {
        var sourceTables = new List<BatchReplySourceTable>();
        foreach (var config in tableConfigs.OrderBy(item => item.TableIndex))
        {
            ValidateTableConfig(sourceFile.FileType, sourceTableMetas, config);
            var rows = await _documentTableAccessService.ExtractReplySourceItemsAsync(sourceFile, config);
            if (rows.Count == 0)
            {
                throw Failure(400, $"表格{config.TableIndex + 1}没有可回复的数据");
            }

            sourceTables.Add(new BatchReplySourceTable
            {
                TableIndex = config.TableIndex,
                ProjectColumnIndex = config.ProjectColumnIndex,
                SpecificationColumnIndex = config.SpecificationColumnIndex,
                AcceptanceColumnIndex = config.AcceptanceColumnIndex,
                RemarkColumnIndex = config.RemarkColumnIndex,
                HeaderRowStart = config.HeaderRowStart,
                HeaderRowCount = config.HeaderRowCount,
                DataStartRow = config.DataStartRow,
                FilterEmptySourceRows = config.FilterEmptySourceRows ?? true,
                Rows = rows.Select(row => new BatchReplySourceRow
                {
                    RowIndex = row.RowIndex,
                    Project = row.Project,
                    Specification = row.Specification,
                    Acceptance = row.Acceptance,
                    Remark = row.Remark
                }).ToList()
            });
        }

        return sourceTables;
    }

    private async Task<BatchReplyTargetFile> BuildPreviewTargetAsync(
        IFormFile targetFile,
        UploadedFileType expectedFileType,
        IReadOnlyCollection<BatchReplySourceTable> sourceTables,
        CancellationToken cancellationToken)
    {
        var result = new BatchReplyTargetFile
        {
            TargetId = Guid.NewGuid().ToString("N"),
            FileName = targetFile?.FileName ?? "未命名文件"
        };

        if (targetFile == null || targetFile.Length == 0)
        {
            result.Errors.Add("目标文件为空");
            return result;
        }

        UploadedFileType fileType;
        try
        {
            fileType = UploadFileValidation.ValidateOfficeDocument(targetFile, allowExcel: true, allowWord: true);
        }
        catch (ApplicationServiceException ex)
        {
            result.Errors.Add(ex.Message);
            return result;
        }

        result.FileType = fileType;
        byte[] content;
        using (var memoryStream = new MemoryStream())
        {
            await targetFile.CopyToAsync(memoryStream, cancellationToken);
            content = memoryStream.ToArray();
        }

        var relativePath = await _batchReplySessionService.SaveTargetFileAsync(
            targetFile.FileName,
            fileType,
            content,
            cancellationToken);
        result.RelativePath = relativePath;

        if (fileType != expectedFileType)
        {
            result.Errors.Add("文件类型不一致");
            return result;
        }

        var targetWordFile = CreateTemporaryWordFile(targetFile.FileName, fileType, relativePath!);
        result.Errors = await ValidateTargetFileAsync(targetWordFile, sourceTables);
        result.CanApply = result.Errors.Count == 0;
        return result;
    }

    private async Task<List<string>> ValidateTargetFileAsync(
        WordFile targetFile,
        IReadOnlyCollection<BatchReplySourceTable> sourceTables)
    {
        var errors = new List<string>();
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
            _logger.LogWarning(ex, "读取批量回复目标文件失败: {FileName}", targetFile.FileName);
            errors.Add($"读取目标文件失败: {ex.Message}");
            return errors;
        }

        foreach (var sourceTable in sourceTables.OrderBy(table => table.TableIndex))
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
            if (targetRows.Count != sourceTable.Rows.Count)
            {
                errors.Add($"表格{sourceTable.TableIndex + 1}的数据区行数不一致");
                continue;
            }

            for (var index = 0; index < sourceTable.Rows.Count; index++)
            {
                var expected = sourceTable.Rows[index];
                var actual = targetRows[index];
                if (actual.RowIndex != expected.RowIndex ||
                    !StrictTextEquals(actual.Project, expected.Project) ||
                    !StrictTextEquals(actual.Specification, expected.Specification))
                {
                    errors.Add($"表格{sourceTable.TableIndex + 1}第{index + 1}行的项目/规格顺序不一致");
                    break;
                }
            }
        }

        return errors;
    }

    private async Task<BatchReplyDownloadArtifact> SaveDownloadArtifactAsync(
        string taskId,
        string sourceFileName,
        IReadOnlyCollection<StrictReuseGeneratedFile> generatedFiles,
        CancellationToken cancellationToken)
    {
        if (generatedFiles.Count == 0)
        {
            throw new InvalidOperationException("没有可保存的批量回复结果");
        }

        if (generatedFiles.Count == 1)
        {
            var file = generatedFiles.First();
            var relativePath = await _fileStorage.SaveFilledWordAsync(file.FileName, file.Content, cancellationToken);
            return new BatchReplyDownloadArtifact
            {
                TaskId = taskId,
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
                await entryStream.WriteAsync(file.Content, cancellationToken);
            }
        }

        var baseName = Path.GetFileNameWithoutExtension(sourceFileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "批量回复结果";
        }

        var downloadFileName = $"{baseName}_批量回复结果.zip";
        var relativePathForZip = await _fileStorage.SaveFilledWordAsync(downloadFileName, zipStream.ToArray(), cancellationToken);
        return new BatchReplyDownloadArtifact
        {
            TaskId = taskId,
            RelativePath = relativePathForZip,
            FileName = downloadFileName,
            ContentType = "application/zip"
        };
    }

    private BatchReplySourceSession GetSourceSessionForApplication(ClaimsPrincipal user, string sessionId)
    {
        var owner = ResolveOwnerForApplication(user);
        var session = _batchReplySessionService.GetSession(owner.UserId, owner.CompanyId, sessionId);
        if (session == null)
        {
            throw new ApplicationServiceException(404, "来源会话不存在或已过期");
        }

        return session;
    }

    private BatchReplySourceSession GetSourceSessionForMatching((int UserId, int CompanyId) owner, string sessionId)
    {
        var session = _batchReplySessionService.GetSession(owner.UserId, owner.CompanyId, sessionId);
        if (session == null)
        {
            throw NotFoundFailure("来源会话不存在或已过期");
        }

        return session;
    }

    private static IReadOnlyCollection<BatchTableConfig> NormalizeTableConfigs(IReadOnlyCollection<BatchTableConfig> tableConfigs)
    {
        if (tableConfigs.Select(item => item.TableIndex).Distinct().Count() != tableConfigs.Count)
        {
            throw Failure(400, "表格配置存在重复");
        }

        return tableConfigs;
    }

    private static void ValidateTableConfig(
        UploadedFileType fileType,
        IReadOnlyList<AcceptanceSpecSystem.Core.Documents.Models.TableInfo> tableMetas,
        BatchTableConfig config)
    {
        if (config.TableIndex < 0 || config.TableIndex >= tableMetas.Count)
        {
            throw Failure(400, $"表格{config.TableIndex + 1}不存在");
        }

        var requiredIndexes = new[]
        {
            config.ProjectColumnIndex,
            config.SpecificationColumnIndex,
            config.AcceptanceColumnIndex,
            config.RemarkColumnIndex ?? -1
        };
        if (requiredIndexes.Any(index => index < 0))
        {
            throw Failure(400, $"表格{config.TableIndex + 1}列配置不合法");
        }

        var tableMeta = tableMetas[config.TableIndex];
        if (requiredIndexes.Max() >= tableMeta.ColumnCount)
        {
            throw Failure(400, $"表格{config.TableIndex + 1}列配置超出来源文件范围");
        }

        if (fileType == UploadedFileType.ExcelXlsx)
        {
            if (config.HeaderRowCount.HasValue && config.HeaderRowCount.Value < 0)
            {
                throw Failure(400, $"表格{config.TableIndex + 1}表头行数不合法");
            }

            if (config.HeaderRowStart.HasValue && config.HeaderRowStart.Value <= 0)
            {
                throw Failure(400, $"表格{config.TableIndex + 1}表头起始行不合法");
            }

            if (config.DataStartRow.HasValue && config.DataStartRow.Value <= 0)
            {
                throw Failure(400, $"表格{config.TableIndex + 1}数据起始行不合法");
            }
        }
    }

    private static WordFile CreateTemporaryWordFile(string fileName, UploadedFileType fileType, string relativePath)
    {
        return new WordFile
        {
            FileName = fileName,
            FileType = fileType,
            FilePath = relativePath,
            FileContent = Array.Empty<byte>(),
            UploadedAt = DateTime.UtcNow
        };
    }

    private static (int UserId, int CompanyId) ResolveOwnerForApplication(ClaimsPrincipal user)
    {
        var userId = AuthClaimHelper.GetUserId(user);
        var companyId = AuthClaimHelper.GetCompanyId(user);
        if (!userId.HasValue || !companyId.HasValue)
        {
            throw new ApplicationServiceException(401, "会话缺少用户上下文");
        }

        return (userId.Value, companyId.Value);
    }

    private static (int UserId, int CompanyId) ResolveOwnerForMatching(ClaimsPrincipal user)
    {
        var userId = AuthClaimHelper.GetUserId(user);
        var companyId = AuthClaimHelper.GetCompanyId(user);
        if (!userId.HasValue || !companyId.HasValue)
        {
            throw Failure(401, "会话缺少用户上下文");
        }

        return (userId.Value, companyId.Value);
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

    private static bool StrictTextEquals(string? left, string? right)
    {
        return string.Equals(NormalizeStrictText(left), NormalizeStrictText(right), StringComparison.Ordinal);
    }

    private static string NormalizeStrictText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(" ", value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string BuildUniqueArchiveEntryName(string fileName, HashSet<string> usedEntryNames)
    {
        var normalizedFileName = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "batch-reply.docx" : fileName.Trim());
        if (string.IsNullOrWhiteSpace(normalizedFileName))
        {
            normalizedFileName = "batch-reply.docx";
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
}
