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
    private readonly ExecutionHistoryAppService _executionHistoryAppService;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<BatchReplyAppService> _logger;

    public BatchReplyAppService(
        DocumentTableAccessService documentTableAccessService,
        MatchingResultWriteBackService matchingResultWriteBackService,
        BatchReplySessionService batchReplySessionService,
        ExecutionHistoryAppService executionHistoryAppService,
        IFileStorageService fileStorage,
        ILogger<BatchReplyAppService> logger)
    {
        _documentTableAccessService = documentTableAccessService;
        _matchingResultWriteBackService = matchingResultWriteBackService;
        _batchReplySessionService = batchReplySessionService;
        _executionHistoryAppService = executionHistoryAppService;
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
        var executionHistoryRows = new Dictionary<string, IReadOnlyCollection<BatchReplyWriteTable>>(StringComparer.Ordinal);
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
            var validation = await ValidateTargetFileAsync(targetWordFile, session.SourceTables);
            if (validation.Errors.Count > 0)
            {
                executeResults.Add(new BatchReplyExecuteFileResult
                {
                    TargetId = target.TargetId,
                    FileName = target.FileName,
                    Success = false,
                    Message = string.Join("；", validation.Errors)
                });
                continue;
            }

            try
            {
                var generated = await _matchingResultWriteBackService.GenerateBatchReplyTargetFileAsync(
                    targetWordFile,
                    validation.WriteTables,
                    cancellationToken);
                generatedFiles.Add(generated);
                executionHistoryRows[target.TargetId] = validation.WriteTables
                    .Select(table => new BatchReplyWriteTable
                    {
                        TableIndex = table.TableIndex,
                        AcceptanceColumnIndex = table.AcceptanceColumnIndex,
                        RemarkColumnIndex = table.RemarkColumnIndex,
                        Rows = table.Rows
                            .Select(row => new BatchReplyWriteRow
                            {
                                RowIndex = row.RowIndex,
                                Project = row.Project,
                                Specification = row.Specification,
                                Acceptance = row.Acceptance,
                                Remark = row.Remark
                            })
                            .ToList()
                    })
                    .ToList();
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
        await SaveExecutionHistoryAsync(user, taskId, session, executeResults, executionHistoryRows, cancellationToken);

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

    private async Task SaveExecutionHistoryAsync(
        ClaimsPrincipal user,
        string taskId,
        BatchReplySourceSession session,
        IReadOnlyCollection<BatchReplyExecuteFileResult> executeResults,
        IReadOnlyDictionary<string, IReadOnlyCollection<BatchReplyWriteTable>> executionHistoryRows,
        CancellationToken cancellationToken)
    {
        var resultLookup = executeResults.ToDictionary(item => item.TargetId, item => item);
        var files = session.TargetFiles
            .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(file =>
            {
                resultLookup.TryGetValue(file.TargetId, out var result);
                var success = result?.Success == true;
                executionHistoryRows.TryGetValue(file.TargetId, out var targetTables);
                var historyTables = targetTables?.Count > 0
                    ? targetTables
                    : BuildSourceFallbackHistoryTables(session.SourceTables);

                return new ExecutionHistoryFileDto
                {
                    FileName = file.FileName,
                    FileType = file.FileType,
                    Sheets = historyTables
                        .OrderBy(table => table.TableIndex)
                        .Select(table => new ExecutionHistorySheetDto
                        {
                            SheetIndex = table.TableIndex,
                            SheetName = $"表格 {table.TableIndex + 1}",
                            Rows = table.Rows
                                .OrderBy(row => row.RowIndex)
                                .Select(row => new ExecutionHistoryRowDto
                                {
                                    RowIndex = row.RowIndex,
                                    Project = row.Project,
                                    Specification = row.Specification,
                                    Acceptance = row.Acceptance,
                                    Remark = row.Remark,
                                    ConfidencePercent = success ? 100 : 0,
                                    Status = success ? ExecutionHistoryStatuses.Adopted : ExecutionHistoryStatuses.Skipped,
                                    IsManualSelected = false,
                                    AcceptanceColumnIndex = table.AcceptanceColumnIndex,
                                    RemarkColumnIndex = table.RemarkColumnIndex
                                })
                                .ToList()
                        })
                        .ToList()
                };
            })
            .ToList();

        await _executionHistoryAppService.SaveAsync(user, new ExecutionHistoryDraft
        {
            TaskId = taskId,
            TaskType = ExecutionHistoryTaskTypes.BatchReply,
            SourceFileId = null,
            SourceFileName = session.SourceFileName,
            SourceFileType = session.SourceFileType,
            CreatedAt = DateTime.UtcNow,
            Files = files
        }, cancellationToken);
    }

    private static IReadOnlyCollection<BatchReplyWriteTable> BuildSourceFallbackHistoryTables(
        IReadOnlyCollection<BatchReplySourceTable> sourceTables)
    {
        return sourceTables
            .Select(table => new BatchReplyWriteTable
            {
                TableIndex = table.TableIndex,
                AcceptanceColumnIndex = table.AcceptanceColumnIndex,
                RemarkColumnIndex = table.RemarkColumnIndex,
                Rows = table.Rows
                    .Select(row => new BatchReplyWriteRow
                    {
                        RowIndex = row.RowIndex,
                        Project = row.Project,
                        Specification = row.Specification,
                        Acceptance = row.Acceptance,
                        Remark = row.Remark
                    })
                    .ToList()
            })
            .ToList();
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
        result.Errors = (await ValidateTargetFileAsync(targetWordFile, sourceTables)).Errors;
        result.CanApply = result.Errors.Count == 0;
        return result;
    }

    private async Task<BatchReplyTargetValidationResult> ValidateTargetFileAsync(
        WordFile targetFile,
        IReadOnlyCollection<BatchReplySourceTable> sourceTables)
    {
        var result = new BatchReplyTargetValidationResult();
        IReadOnlyList<AcceptanceSpecSystem.Core.Documents.Models.TableInfo> targetTables;
        try
        {
            targetTables = await _documentTableAccessService.GetTablesAsync(targetFile);
        }
        catch (ApplicationServiceException ex)
        {
            result.Errors.Add(ex.Message);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取批量回复目标文件失败: {FileName}", targetFile.FileName);
            result.Errors.Add($"读取目标文件失败: {ex.Message}");
            return result;
        }

        foreach (var sourceTable in sourceTables.OrderBy(table => table.TableIndex))
        {
            if (sourceTable.TableIndex < 0 || sourceTable.TableIndex >= targetTables.Count)
            {
                result.Errors.Add($"表格{sourceTable.TableIndex + 1}不存在");
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
                result.Errors.Add($"表格{sourceTable.TableIndex + 1}列配置超出目标文件范围");
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
                result.Errors.Add($"表格{sourceTable.TableIndex + 1}的数据区行数不一致");
                continue;
            }

            var sourceLookup = BuildSourceRowLookup(sourceTable, result.Errors);
            if (sourceLookup == null)
            {
                continue;
            }

            var targetLookup = BuildTargetRowLookup(sourceTable.TableIndex, targetRows, result.Errors);
            if (targetLookup == null)
            {
                continue;
            }

            if (sourceLookup.Count != targetLookup.Count ||
                sourceLookup.Keys.Except(targetLookup.Keys).Any() ||
                targetLookup.Keys.Except(sourceLookup.Keys).Any())
            {
                result.Errors.Add($"表格{sourceTable.TableIndex + 1}的项目/规格不一致");
                continue;
            }

            result.WriteTables.Add(new BatchReplyWriteTable
            {
                TableIndex = sourceTable.TableIndex,
                AcceptanceColumnIndex = sourceTable.AcceptanceColumnIndex,
                RemarkColumnIndex = sourceTable.RemarkColumnIndex,
                Rows = targetRows.Select(targetRow =>
                {
                    var sourceRow = sourceLookup[BuildRowKey(targetRow.Project, targetRow.Specification)];
                    return new BatchReplyWriteRow
                    {
                        RowIndex = targetRow.RowIndex,
                        Project = targetRow.Project,
                        Specification = targetRow.Specification,
                        Acceptance = sourceRow.Acceptance,
                        Remark = sourceRow.Remark
                    };
                }).ToList()
            });
        }

        return result;
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

    private static Dictionary<(string Project, string Specification), BatchReplySourceRow>? BuildSourceRowLookup(
        BatchReplySourceTable sourceTable,
        List<string> errors)
    {
        var lookup = new Dictionary<(string Project, string Specification), BatchReplySourceRow>();
        foreach (var row in sourceTable.Rows)
        {
            var key = BuildRowKey(row.Project, row.Specification);
            if (lookup.TryAdd(key, row))
            {
                continue;
            }

            errors.Add($"表格{sourceTable.TableIndex + 1}存在重复的项目/规格组合，请手动处理");
            return null;
        }

        return lookup;
    }

    private static Dictionary<(string Project, string Specification), MatchSourceItem>? BuildTargetRowLookup(
        int tableIndex,
        IReadOnlyCollection<MatchSourceItem> targetRows,
        List<string> errors)
    {
        var lookup = new Dictionary<(string Project, string Specification), MatchSourceItem>();
        foreach (var row in targetRows)
        {
            var key = BuildRowKey(row.Project, row.Specification);
            if (lookup.TryAdd(key, row))
            {
                continue;
            }

            errors.Add($"表格{tableIndex + 1}存在重复的项目/规格组合，请手动处理");
            return null;
        }

        return lookup;
    }

    private static (string Project, string Specification) BuildRowKey(string? project, string? specification)
    {
        return (NormalizeStrictText(project), NormalizeStrictText(specification));
    }

    private static string NormalizeStrictText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(" ", value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed class BatchReplyTargetValidationResult
    {
        public List<string> Errors { get; } = [];

        public List<BatchReplyWriteTable> WriteTables { get; } = [];
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
