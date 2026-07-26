using System.IO.Compression;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.Extensions.Logging;
using static AcceptanceSpecSystem.Application.Services.MatchingResultHelpers;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class BatchReplyAppService
{
    public async Task<MatchingOperationResult<BatchReplyExecuteResponse>> ExecuteAsync(
        BatchReplyUserContext user,
        BatchReplyExecuteRequest request,
        CancellationToken cancellationToken = default)
    {
        // 兼容旧版仅传 SessionId 的执行请求，同时保留新式显式表格配置入口。
        if (request.SourceTables.Count > 0 || request.Targets.Count > 0)
        {
            return await ExecuteConfiguredAsync(user, request, cancellationToken);
        }

        return await ExecuteLegacyAsync(user, request, cancellationToken);
    }

    private async Task<MatchingOperationResult<BatchReplyExecuteResponse>> ExecuteLegacyAsync(
        BatchReplyUserContext user,
        BatchReplyExecuteRequest request,
        CancellationToken cancellationToken)
    {
        var owner = ResolveOwnerForMatching(user);
        var session = GetSourceSessionForMatching(owner, request.SessionId);
        if (session.SourceTables.Count == 0 || session.TargetFiles.Count == 0)
        {
            throw Failure(400, "请先完成预检后再执行批量回复");
        }

        var generatedFiles = new List<GeneratedArtifactFile>();
        var executeResults = new List<BatchReplyExecuteFileResult>();
        var executionHistoryRows = new Dictionary<string, IReadOnlyCollection<BatchReplyWriteTable>>(StringComparer.Ordinal);
        // 旧模式按会话内已预检过的目标文件逐个执行，失败项不影响其它文件。
        foreach (var target in session.TargetFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            var validation = await ValidateTargetFileAsync(
                targetWordFile,
                session.SourceTables,
                cancellationToken);
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
                var generated = await _matchingResultWriteBackService.GenerateTargetFileAsync(
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
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
        await _executionHistoryAppService.SaveAsync(user, taskId, session, session.TargetFiles, executeResults, executionHistoryRows, cancellationToken);

        var response = CreateExecuteResponse(taskId, artifact, executeResults);
        return Result(response, BuildExecuteMessage(response));
    }

    private async Task<MatchingOperationResult<BatchReplyExecuteResponse>> ExecuteConfiguredAsync(
        BatchReplyUserContext user,
        BatchReplyExecuteRequest request,
        CancellationToken cancellationToken)
    {
        var owner = ResolveOwnerForMatching(user);
        if (request.SourceTables == null || request.SourceTables.Count == 0)
        {
            throw Failure(400, "请至少配置一个来源表格");
        }

        if (request.Targets == null || request.Targets.Count == 0)
        {
            throw Failure(400, "请至少选择一个目标文件");
        }

        var session = GetSourceSessionForMatching(owner, request.SessionId);
        var sourceFile = CreateTemporaryWordFile(session.SourceFileName, session.SourceFileType, session.SourceFileRelativePath);
        var normalizedSourceConfigs = NormalizeTableConfigs(request.SourceTables);
        var sourceTableMetas = await _documentTableAccessService.GetTablesAsync(sourceFile, cancellationToken);
        var sourceTables = await BuildSourceTablesAsync(
            sourceFile,
            sourceTableMetas,
            normalizedSourceConfigs,
            cancellationToken);
        var sourceLookup = sourceTables.ToDictionary(table => table.TableIndex);

        var generatedFiles = new List<GeneratedArtifactFile>();
        var executeResults = new List<BatchReplyExecuteFileResult>();
        var executionHistoryRows = new Dictionary<string, IReadOnlyCollection<BatchReplyWriteTable>>(StringComparer.Ordinal);
        var selectedTargetFiles = new List<BatchReplyTargetFile>();

        // 新模式以请求内显式指定的目标文件为准，便于前端按文件粒度控制表格参与范围。
        foreach (var targetRequest in request.Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = session.TargetFiles.FirstOrDefault(file => string.Equals(file.TargetId, targetRequest.TargetId, StringComparison.Ordinal));
            if (target == null)
            {
                executeResults.Add(new BatchReplyExecuteFileResult
                {
                    TargetId = targetRequest.TargetId,
                    FileName = "未知目标文件",
                    Success = false,
                    Message = "目标文件不存在或已过期"
                });
                continue;
            }

            selectedTargetFiles.Add(target);
            if (string.IsNullOrWhiteSpace(target.RelativePath) || !target.FileType.HasValue)
            {
                executeResults.Add(new BatchReplyExecuteFileResult
                {
                    TargetId = target.TargetId,
                    FileName = target.FileName,
                    Success = false,
                    Message = "目标文件不可用"
                });
                continue;
            }

            if (targetRequest.Tables == null || targetRequest.Tables.Count == 0)
            {
                executeResults.Add(new BatchReplyExecuteFileResult
                {
                    TargetId = target.TargetId,
                    FileName = target.FileName,
                    Success = false,
                    Message = "该目标文件未配置任何参与表"
                });
                continue;
            }

            IReadOnlyCollection<BatchTableConfig> normalizedTargetConfigs;
            try
            {
                normalizedTargetConfigs = NormalizeTableConfigs(targetRequest.Tables);
            }
            catch (MatchingApiException ex)
            {
                executeResults.Add(new BatchReplyExecuteFileResult
                {
                    TargetId = target.TargetId,
                    FileName = target.FileName,
                    Success = false,
                    Message = ex.Message
                });
                continue;
            }

            var targetWordFile = CreateTemporaryWordFile(target.FileName, target.FileType.Value, target.RelativePath);
            IReadOnlyList<AcceptanceSpecSystem.Core.Documents.Models.TableInfo> targetTables;
            try
            {
                targetTables = await _documentTableAccessService.GetTablesAsync(targetWordFile, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ApplicationServiceException ex)
            {
                executeResults.Add(new BatchReplyExecuteFileResult
                {
                    TargetId = target.TargetId,
                    FileName = target.FileName,
                    Success = false,
                    Message = ex.Message
                });
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取批量回复目标文件失败: {FileName}", target.FileName);
                executeResults.Add(new BatchReplyExecuteFileResult
                {
                    TargetId = target.TargetId,
                    FileName = target.FileName,
                    Success = false,
                    Message = $"读取目标文件失败: {ex.Message}"
                });
                continue;
            }

            var validation = new BatchReplyTargetValidationResult();
            foreach (var targetTableConfig in normalizedTargetConfigs.OrderBy(item => item.TableIndex))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceTableIndex = ResolveSourceTableIndex(targetTableConfig);
                if (!sourceLookup.TryGetValue(sourceTableIndex, out var sourceTable))
                {
                    validation.Errors.Add($"来源表格{sourceTableIndex + 1}不存在或未配置");
                    continue;
                }

                try
                {
                    var tableValidation = await ValidateTargetTableAsync(
                        targetWordFile,
                        targetTables,
                        sourceTable,
                        targetTableConfig,
                        cancellationToken);
                    validation.Errors.AddRange(tableValidation.Errors);
                    if (tableValidation.WriteTable != null)
                    {
                        validation.WriteTables.Add(tableValidation.WriteTable);
                    }
                }
                catch (MatchingApiException ex)
                {
                    validation.Errors.Add(ex.Message);
                }
            }

            if (validation.Errors.Count > 0 || validation.WriteTables.Count == 0)
            {
                executeResults.Add(new BatchReplyExecuteFileResult
                {
                    TargetId = target.TargetId,
                    FileName = target.FileName,
                    Success = false,
                    Message = validation.Errors.Count > 0
                        ? string.Join("；", validation.Errors)
                        : "该目标文件没有可执行的表格"
                });
                continue;
            }

            try
            {
                var generated = await _matchingResultWriteBackService.GenerateTargetFileAsync(
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
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
            throw Failure(400, "没有配置完整且可执行的目标文件");
        }

        var taskId = Guid.NewGuid().ToString("N");
        var artifact = await SaveDownloadArtifactAsync(taskId, session.SourceFileName, generatedFiles, cancellationToken);
        await _batchReplySessionService.SaveDownloadArtifactAsync(
            owner.UserId,
            owner.CompanyId,
            artifact,
            cancellationToken);
        await _executionHistoryAppService.SaveAsync(user, taskId, session, selectedTargetFiles, executeResults, executionHistoryRows, cancellationToken);

        var response = CreateExecuteResponse(taskId, artifact, executeResults);
        return Result(response, BuildExecuteMessage(response));
    }


    private static BatchReplyExecuteResponse CreateExecuteResponse(
        string taskId,
        BatchReplyDownloadArtifact artifact,
        IReadOnlyCollection<BatchReplyExecuteFileResult> executeResults)
    {
        return new BatchReplyExecuteResponse
        {
            TaskId = taskId,
            SuccessCount = executeResults.Count(item => item.Success),
            FailedCount = executeResults.Count(item => !item.Success),
            DownloadUrl = $"/api/batch-reply/download/{taskId}",
            DownloadFileName = artifact.FileName,
            Files = executeResults.ToList()
        };
    }

    private static string BuildExecuteMessage(BatchReplyExecuteResponse response)
    {
        return response.FailedCount > 0
            ? $"批量回复完成：成功{response.SuccessCount}份，失败{response.FailedCount}份"
            : $"批量回复完成：成功{response.SuccessCount}份";
    }
}
