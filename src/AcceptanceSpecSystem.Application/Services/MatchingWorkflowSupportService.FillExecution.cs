using System.Diagnostics;
using static AcceptanceSpecSystem.Application.Services.MatchingResultHelpers;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class MatchingWorkflowSupportService
{
    private async Task<MatchingOperationResult<ExecuteFillResponse>> BatchExecuteFillUnlockedAsync(MatchingUserContext user, BatchExecuteFillRequest request, CancellationToken cancellationToken)
    {
        if (request.Tables == null || request.Tables.Count == 0)
        {
            throw Failure(400, "请至少提供一个表格的填充映射");
        }

        EnsureDistinctBatchTableIndexes(request.Tables);
        EnsureDistinctPreviewTableIndexes(request.PreviewTables);
        foreach (var table in request.Tables)
        {
            var regionValidationError = MatchingRegionValidator.GetValidationError(
                table.Regions,
                table.TableIndex);
            if (regionValidationError != null)
            {
                throw Failure(400, regionValidationError);
            }
            EnsureDistinctFillMappings(
                table.Mappings,
                $"表格{table.TableIndex + 1}存在重复的行索引，请删除重复映射后重试");
        }

        const int MaxBatchTableCount = 500;
        if (request.Tables.Count > MaxBatchTableCount)
        {
            throw Failure(400, $"批量操作不能超过 {MaxBatchTableCount} 个表格");
        }

        if (request.FileId <= 0)
        {
            throw Failure(400, "文件ID不能为空");
        }

        // 获取源文件
        var scope = await ResolveSpecScopeAsync(user, cancellationToken);
        if (scope == null)
        {
            throw Failure(401, "会话缺少用户上下文");
        }

        var wordFile = await _documentFileAccessService.GetAccessibleWordFileAsync(request.FileId, scope);
        if (wordFile == null)
        {
            throw Failure(400, "源文件不存在");
        }
        var businessScope = await _businessOrgScopeService.ResolveFileScopeAsync(
            scope,
            wordFile,
            cancellationToken);
        var executionRequestId = request.ExecutionRequestId?.Trim();
        var requestFingerprint = BuildFillExecutionRequestFingerprint(request);
        if (!string.IsNullOrEmpty(executionRequestId))
        {
            if (executionRequestId.Length > 80 ||
                executionRequestId.Any(character =>
                    !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
            {
                throw Failure(400, "执行幂等键格式不合法");
            }

            var existingTask = await LoadIdempotentTaskAsync(
                _matchingTaskSnapshotService,
                user,
                executionRequestId);
            if (existingTask != null)
            {
                EnsureIdempotentFillRequestMatches(
                    existingTask,
                    request.FileId,
                    requestFingerprint);

                if (existingTask.FileMutationPending)
                {
                    await _matchingTaskSnapshotService.RecoverPendingFileMutationAsync(
                        user,
                        existingTask,
                        cancellationToken);
                }
                else
                {
                    return Result(
                        new ExecuteFillResponse
                        {
                            TaskId = existingTask.TaskId,
                            FilledCount = existingTask.FilledCount,
                            SkippedCount = existingTask.SkippedCount,
                            DownloadUrl = wordFile.FileType == UploadedFileType.ExcelXlsx
                                ? string.Empty
                                : $"/api/matching/download/{existingTask.TaskId}"
                        },
                        "该填充请求已完成，已返回原任务结果");
                }
            }
        }
        var reviewApprovalBundle = _approvalTokenService.ResolveBundle(
            request.Tables.SelectMany(table =>
                table.Mappings.Select(mapping => (TableIndex: (int?)table.TableIndex, Mapping: mapping))),
            scope.UserId);
        var hasNonTokenSpecMappings = request.Tables.Any(table =>
            table.Mappings.Any(mapping =>
                mapping.SpecId.GetValueOrDefault() > 0 &&
                reviewApprovalBundle?.Tokens.ContainsKey(
                    new MatchingApprovalTokenService.ApprovalLookupKey(table.TableIndex, mapping.RowIndex)) != true));
        var requestedConfig = await ResolveExecutionMatchingConfigAsync(request.Config);
        if (hasNonTokenSpecMappings)
        {
            _approvalTokenService.EnsureRequestContextMatchesBundle(
                reviewApprovalBundle,
                request.CustomerId,
                request.ProcessId,
                request.MachineModelId,
                requestedConfig);
        }
        var executionConfig = reviewApprovalBundle?.Config ?? requestedConfig;
        var effectiveCustomerId = reviewApprovalBundle?.CustomerId ?? request.CustomerId;
        var effectiveProcessId = reviewApprovalBundle?.ProcessId ?? request.ProcessId;
        var effectiveMachineModelId = reviewApprovalBundle?.MachineModelId ?? request.MachineModelId;

        // 收集所有 specId 一次查 DB
        var allSpecIds = request.Tables
            .SelectMany(t => t.Mappings)
            .Select(m => m.SpecId)
            .Where(id => id.HasValue && id.Value > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var specDict = await GetScopedSpecDictionaryAsync(allSpecIds, businessScope);

        var currentMatchLookups = BuildExecutionPreviewSnapshots(request.PreviewTables);
        foreach (var table in request.Tables)
        {
            var currentSnapshot = currentMatchLookups.GetValueOrDefault(table.TableIndex);
            currentSnapshot ??= new ExecutionMatchSnapshot();
            currentMatchLookups[table.TableIndex] = currentSnapshot;
            await CanonicalizeExecutionRegionSourcesAsync(
                wordFile,
                table,
                currentSnapshot,
                // 执行门禁必须能验证被过滤的空源行确实存在，人工填写才可安全写入；
                // 当前匹配仍在后续按用户的 FilterEmptySourceRows 配置过滤。
                filterEmptySourceRows: false,
                cancellationToken);
            if (ExecutionPreviewSnapshotCoversMappings(table, currentSnapshot, reviewApprovalBundle))
            {
                continue;
            }

            var currentMatchRows = GetRowsRequiringCurrentMatch(table, reviewApprovalBundle);
            if (currentMatchRows.Count > 0 || MissingSourceRowsForTokenValidation(table, currentSnapshot, reviewApprovalBundle))
            {
                EnsureExecutionPreviewContext(table.ProjectColumnIndex, table.SpecificationColumnIndex, table.TableIndex);
                currentMatchLookups[table.TableIndex] = await BuildCurrentMatchLookupAsync(
                    wordFile,
                    table.TableIndex,
                    table.ProjectColumnIndex,
                    table.SpecificationColumnIndex,
                    table.HeaderRowStart,
                    table.HeaderRowCount,
                    table.DataStartRow,
                    table.DataEndRow,
                    table.Regions,
                    table.FilterEmptySourceRows ?? executionConfig.FilterEmptySourceRows,
                    effectiveCustomerId,
                    effectiveProcessId,
                    effectiveMachineModelId,
                    executionConfig,
                    businessScope,
                    currentSnapshot,
                    currentMatchRows,
                    cancellationToken);
            }
            else if (!currentMatchLookups.ContainsKey(table.TableIndex))
            {
                currentMatchLookups[table.TableIndex] = new ExecutionMatchSnapshot();
            }
        }

        // 遍历每个表格生成 TableFillEntry
        int totalFilled = 0, totalSkipped = 0;
        var tableEntries = new List<TableFillEntry>();
        var adoptedRowLookup = new Dictionary<int, HashSet<int>>();

        foreach (var tableFill in request.Tables)
        {
            var entry = new TableFillEntry
            {
                TableIndex = tableFill.TableIndex,
                AcceptanceColumnIndex = tableFill.AcceptanceColumnIndex,
                RemarkColumnIndex = tableFill.RemarkColumnIndex
            };
            adoptedRowLookup[tableFill.TableIndex] = new HashSet<int>();
            currentMatchLookups.TryGetValue(tableFill.TableIndex, out var currentMatchSnapshot);
            currentMatchSnapshot ??= new ExecutionMatchSnapshot();
            var currentMatchLookup = currentMatchSnapshot.MatchLookup;
            var currentSourceRowLookup = currentMatchSnapshot.SourceRowLookup;

            foreach (var mapping in tableFill.Mappings)
            {
                var sourceItem = currentSourceRowLookup.GetValueOrDefault(mapping.RowIndex);
                var selectedSpecId = mapping.SpecId ?? 0;
                if (selectedSpecId <= 0 || !specDict.TryGetValue(selectedSpecId, out var spec))
                {
                    if (TryCreateManualFillResult(mapping, out var manualFillResult))
                    {
                        ApplyRegionWriteTarget(
                            manualFillResult,
                            sourceItem,
                            tableFill.AcceptanceColumnIndex,
                            tableFill.RemarkColumnIndex);
                        entry.FillResults.Add(manualFillResult);
                        adoptedRowLookup[tableFill.TableIndex].Add(mapping.RowIndex);
                        totalFilled++;
                    }
                    else
                    {
                        totalSkipped++;
                    }
                }
                else
                {
                    currentMatchLookup.TryGetValue(mapping.RowIndex, out var currentMatch);
                    var reviewApprovalToken = reviewApprovalBundle?.Tokens.GetValueOrDefault(
                        new MatchingApprovalTokenService.ApprovalLookupKey(tableFill.TableIndex, mapping.RowIndex));
                    if (!CanApplyMatchedSpec(
                            _approvalTokenService,
                            mapping,
                            spec,
                            currentMatch,
                            currentSourceRowLookup.GetValueOrDefault(mapping.RowIndex)?.Project,
                            currentSourceRowLookup.GetValueOrDefault(mapping.RowIndex)?.Specification,
                            reviewApprovalToken))
                    {
                        totalSkipped++;
                        continue;
                    }

                    entry.FillResults.Add(new FillResult
                    {
                        RegionId = sourceItem?.RegionId,
                        RegionIndex = sourceItem?.RegionIndex,
                        AcceptanceColumnIndex = sourceItem?.AcceptanceColumnIndex ?? tableFill.AcceptanceColumnIndex,
                        RemarkColumnIndex = sourceItem?.RemarkColumnIndex ?? tableFill.RemarkColumnIndex,
                        RowIndex = mapping.RowIndex,
                        SpecId = spec.Id,
                        Acceptance = mapping.OverrideAcceptance ?? spec.Acceptance ?? "",
                        Remark = mapping.OverrideRemark ?? spec.Remark
                    });
                    adoptedRowLookup[tableFill.TableIndex].Add(mapping.RowIndex);
                    totalFilled++;
                }
            }

            tableEntries.Add(entry);
        }

        // 生成任务ID
        var taskId = string.IsNullOrEmpty(executionRequestId)
            ? Guid.NewGuid().ToString("N")
            : BuildScopedFillTaskId(user, executionRequestId);
        var taskResult = new FillTaskResult
        {
            TaskId = taskId,
            SourceFileId = request.FileId,
            RequestFingerprint = requestFingerprint,
            FilledCount = totalFilled,
            SkippedCount = totalSkipped,
            IsBatchMode = true,
            TableEntries = tableEntries,
            CreatedAt = DateTime.UtcNow
        };

        var isExcelSource = wordFile.FileType == UploadedFileType.ExcelXlsx;
        var persistedTaskResult = isExcelSource
            ? CreatePersistableTaskResult(taskResult, includeFillEntries: false)
            : taskResult;
        if (isExcelSource)
        {
            await PersistExcelFillExecutionAsync(
                user,
                request,
                wordFile,
                taskId,
                taskResult,
                persistedTaskResult,
                executionConfig,
                specDict,
                adoptedRowLookup,
                currentMatchLookups,
                requestFingerprint,
                cancellationToken);
        }
        else
        {
            try
            {
                var renderedFile = await _matchingResultWriteBackService.RenderFillResultToSourceFileAsync(
                    wordFile,
                    taskResult,
                    cancellationToken);
                EnsureWriteBackCompleted(renderedFile.Summary);

                var finalCommitConfirmed = false;
                await _unitOfWork.BeginTransactionAsync(cancellationToken);
                try
                {
                    await _matchingTaskSnapshotService.SaveAsync(
                        user,
                        persistedTaskResult,
                        saveImmediately: false,
                        cancellationToken: cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    await PersistDownloadArtifactAsync(
                        taskId,
                        persistedTaskResult,
                        wordFile,
                        renderedFile.Content,
                        cancellationToken);
                    await SaveExecutionHistoryAsync(
                        user,
                        wordFile,
                        taskId,
                        taskResult.CreatedAt,
                        request.Tables,
                        request.PreviewTables,
                        executionConfig,
                        specDict,
                        adoptedRowLookup,
                        currentMatchLookups,
                        saveImmediately: false,
                        cancellationToken: cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    // 文件产物和数据库写入均已准备完成后进入不可取消提交边界，避免客户端取消造成
                    // “数据库已提交但随后按失败路径删除下载产物”的不一致状态。
                    await _unitOfWork.CommitTransactionAsync(CancellationToken.None);
                    finalCommitConfirmed = true;
                    try
                    {
                        await _matchingTaskSnapshotService.CompleteDeferredExpiredArtifactCleanupAsync(
                            CancellationToken.None);
                    }
                    catch (Exception cleanupException)
                    {
                        _logger.LogWarning(
                            cleanupException,
                            "Word 填充已提交，但清理过期产物失败: 任务{TaskId}",
                            taskId);
                    }
                }
                catch (Exception finalizationException)
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    if (!finalCommitConfirmed)
                    {
                        var committedTask = await LoadTaskFromFreshScopeAsync(user, taskId);
                        finalCommitConfirmed = committedTask != null &&
                                               string.Equals(
                                                   committedTask.RequestFingerprint,
                                                   requestFingerprint,
                                                   StringComparison.Ordinal);
                    }

                    if (!finalCommitConfirmed)
                    {
                        await DeleteFailedDownloadArtifactAsync(persistedTaskResult);
                        _matchingTaskSnapshotService.DiscardDeferredExpiredArtifactCleanup();
                        throw;
                    }

                    _logger.LogWarning(
                        finalizationException,
                        "Word 最终提交返回异常，但已从数据库确认提交成功: 任务{TaskId}",
                        taskId);
                }
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
                _logger.LogError(ex, "批量填充后固化 Word 下载产物失败: 文件{FileId}", wordFile.Id);
                throw Failure(500, $"固化下载产物失败: {ex.Message}");
            }
        }

        await TryLearnColumnMappingsAfterFillAsync(
            wordFile,
            request,
            effectiveCustomerId,
            totalFilled,
            cancellationToken);

        var response = new ExecuteFillResponse
        {
            TaskId = taskId,
            FilledCount = totalFilled,
            SkippedCount = totalSkipped,
            DownloadUrl = isExcelSource ? string.Empty : $"/api/matching/download/{taskId}"
        };

        _logger.LogInformation(
            "批量填充完成: 任务{TaskId}, 文件类型{FileType}, {TableCount}个表格, 填充{Filled}行, 跳过{Skipped}行",
            taskId, wordFile.FileType, request.Tables.Count, totalFilled, totalSkipped);

        return Result(response, isExcelSource
            ? $"批量填充完成：已填充{totalFilled}行，跳过{totalSkipped}行，已写回并可下载 Excel"
            : $"批量填充完成：已填充{totalFilled}行，跳过{totalSkipped}行");
    }

    private static void EnsureDistinctBatchTableIndexes(IReadOnlyCollection<BatchTableFillMapping> tables)
    {
        var uniqueCount = tables
            .Select(table => table.TableIndex)
            .Distinct()
            .Count();
        if (uniqueCount != tables.Count)
        {
            throw Failure(400, "存在重复的表格索引，请删除重复表格后重试");
        }
    }

    private static void EnsureDistinctFillMappings(IReadOnlyCollection<FillMapping> mappings, string message)
    {
        var uniqueCount = mappings
            .Select(mapping => mapping.RowIndex)
            .Distinct()
            .Count();
        if (uniqueCount != mappings.Count)
        {
            throw Failure(400, message);
        }
    }

    private static void EnsureDistinctPreviewTableIndexes(
        IReadOnlyCollection<ExecutionHistoryPreviewTableSnapshot>? previewTables)
    {
        if (previewTables == null || previewTables.Count == 0)
        {
            return;
        }

        var uniqueCount = previewTables
            .Select(table => table.TableIndex)
            .Distinct()
            .Count();
        if (uniqueCount != previewTables.Count)
        {
            throw Failure(400, "执行记录预览快照存在重复的表格索引，请重新预览后再执行");
        }
    }

    private static void EnsureDistinctLlmStreamItems(IReadOnlyCollection<MatchLlmStreamItem> items)
    {
        var uniqueCount = items
            .Select(item => (item.TableIndex, item.RowIndex))
            .Distinct()
            .Count();
        if (uniqueCount != items.Count)
        {
            throw Failure(400, "同一行存在重复的复核请求，请刷新预览后重试");
        }
    }
}
