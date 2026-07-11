using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
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
    internal async Task<MatchingOperationResult<ExecuteFillResponse>> BatchExecuteFillCoreAsync(MatchingUserContext user, BatchExecuteFillRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Tables == null || request.Tables.Count == 0)
        {
            throw Failure(400, "请至少提供一个表格的填充映射");
        }

        EnsureDistinctBatchTableIndexes(request.Tables);
        EnsureDistinctPreviewTableIndexes(request.PreviewTables);
        foreach (var table in request.Tables)
        {
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
        var scope = await ResolveSpecScopeAsync(user);
        if (scope == null)
        {
            throw Failure(401, "会话缺少用户上下文");
        }

        var wordFile = await _documentFileAccessService.GetAccessibleWordFileAsync(request.FileId, scope);
        if (wordFile == null)
        {
            throw Failure(400, "源文件不存在");
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

        var specDict = await GetScopedSpecDictionaryAsync(allSpecIds, scope);

        var currentMatchLookups = BuildExecutionPreviewSnapshots(request.PreviewTables);
        foreach (var table in request.Tables)
        {
            var currentSnapshot = currentMatchLookups.GetValueOrDefault(table.TableIndex);
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
                    table.FilterEmptySourceRows ?? executionConfig.FilterEmptySourceRows,
                    effectiveCustomerId,
                    effectiveProcessId,
                    effectiveMachineModelId,
                    executionConfig,
                    scope,
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
                var selectedSpecId = mapping.SpecId ?? 0;
                if (selectedSpecId <= 0 || !specDict.TryGetValue(selectedSpecId, out var spec))
                {
                    if (TryCreateManualFillResult(mapping, out var manualFillResult))
                    {
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
        var taskId = Guid.NewGuid().ToString("N");
        var taskResult = new FillTaskResult
        {
            TaskId = taskId,
            SourceFileId = request.FileId,
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
            try
            {
                var renderedFile = await _matchingResultWriteBackService.RenderFillResultToSourceFileAsync(
                    wordFile,
                    taskResult,
                    cancellationToken);
                EnsureWriteBackCompleted(renderedFile.Summary);

                await _unitOfWork.BeginTransactionAsync();
                try
                {
                    await _matchingTaskSnapshotService.SaveAsync(user, persistedTaskResult, saveImmediately: false);
                    await _unitOfWork.SaveChangesAsync();

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
                    await _unitOfWork.SaveChangesAsync();

                    await PersistExcelExecutionAsync(wordFile, renderedFile.Content, cancellationToken);
                    await PersistDownloadArtifactAsync(
                        taskId,
                        persistedTaskResult,
                        wordFile,
                        renderedFile.Content,
                        cancellationToken);
                    await _unitOfWork.CommitTransactionAsync();
                }
                catch
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    throw;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量填充后写回 Excel 失败: 文件{FileId}", wordFile.Id);
                throw Failure(500, $"写回 Excel 失败: {ex.Message}");
            }
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

                await _matchingTaskSnapshotService.SaveAsync(user, persistedTaskResult);
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
                    cancellationToken: cancellationToken);
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

    private async Task TryLearnColumnMappingsAfterFillAsync(
        WordFile wordFile,
        BatchExecuteFillRequest request,
        int? customerId,
        int totalFilled,
        CancellationToken cancellationToken)
    {
        if (!customerId.HasValue || customerId.Value <= 0 || totalFilled <= 0)
        {
            return;
        }

        foreach (var table in request.Tables)
        {
            try
            {
                await _columnMappingLearningService.LearnFromDocumentTableAsync(
                    customerId,
                    wordFile,
                    table.TableIndex,
                    table.ProjectColumnIndex,
                    table.SpecificationColumnIndex,
                    table.AcceptanceColumnIndex,
                    table.RemarkColumnIndex,
                    table.HeaderRowStart,
                    table.HeaderRowCount,
                    table.DataStartRow,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "智能填充成功后学习列映射失败: 文件{FileId}, 表{TableIndex}, 客户{CustomerId}",
                    wordFile.Id,
                    table.TableIndex,
                    customerId);
            }
        }
    }


    private void EnsureExecutionPreviewContext(int? projectColumnIndex, int? specificationColumnIndex, int? tableIndex = null)
    {
        if (projectColumnIndex.HasValue && specificationColumnIndex.HasValue)
        {
            return;
        }

        var prefix = tableIndex.HasValue
            ? $"表格{tableIndex.Value}执行填充"
            : "执行填充";
        throw Failure(400, $"{prefix}必须提供项目列索引和规格列索引，请重新预览后再执行");
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
