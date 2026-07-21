using System.Text;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class DocumentImportAppService
{
    private static readonly SemaphoreSlim SourceCleanupMemoryLease = new(1, 1);
    private async Task TryLearnColumnMappingsAfterImportAsync(
        int customerId,
        IReadOnlyList<string> headers,
        int? projectColumnIndex,
        int? specificationColumnIndex,
        int? acceptanceColumnIndex,
        int? remarkColumnIndex,
        string? tableName,
        DocumentImportAppResult importResult,
        CancellationToken cancellationToken)
    {
        if (importResult.Result.PendingCount > 0 || importResult.Result.SuccessCount <= 0)
        {
            return;
        }

        try
        {
            await _columnMappingLearningService.LearnFromHeadersAsync(
                customerId,
                headers,
                projectColumnIndex,
                specificationColumnIndex,
                acceptanceColumnIndex,
                remarkColumnIndex,
                tableName,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "导入成功后学习列映射失败: 客户{CustomerId}, 表{TableName}", customerId, tableName);
        }
    }

    private async Task ValidateImportTargetAsync(
        int customerId,
        int? processId,
        int? machineModelId,
        CancellationToken cancellationToken)
    {
        var customer = await _unitOfWork.Customers.GetByIdAsync(customerId, cancellationToken);
        if (customer == null)
        {
            throw new ApplicationServiceException(400, "客户不存在");
        }

        if (processId.HasValue)
        {
            var process = await _unitOfWork.Processes.GetByIdAsync(processId.Value, cancellationToken);
            if (process == null)
            {
                throw new ApplicationServiceException(400, "制程不存在");
            }
        }

        if (machineModelId.HasValue)
        {
            var machineModel = await _unitOfWork.MachineModels.GetByIdAsync(machineModelId.Value, cancellationToken);
            if (machineModel == null)
            {
                throw new ApplicationServiceException(400, "机型不存在");
            }
        }
    }

    private async Task<WordFile> AuthorizeImportReplayAsync(
        SpecAccessContext scope,
        int fileId,
        UploadedFileType expectedFileType,
        int customerId,
        int? processId,
        int? machineModelId,
        CancellationToken cancellationToken)
    {
        var file = await _documentFileAccessService.GetAccessibleWordFileAsync(
            fileId,
            scope,
            includeScopedSpecs: true,
            cancellationToken);
        if (file == null)
        {
            throw new ApplicationServiceException(400, "文件不存在或当前无权访问");
        }

        if (file.FileType != expectedFileType)
        {
            throw new ApplicationServiceException(
                400,
                expectedFileType == UploadedFileType.ExcelXlsx
                    ? "该文件不是 Excel 文件"
                    : "该文件为 Excel，请使用 Excel 导入接口");
        }

        await ValidateImportTargetAsync(
            customerId,
            processId,
            machineModelId,
            cancellationToken);
        return file;
    }

    private async Task<DocumentImportAppResult> ExecuteImportAsync(
        SpecAccessContext scope,
        WordFile wordFile,
        int sourceIndex,
        int customerId,
        int? processId,
        int? machineModelId,
        IEnumerable<string>? confirmedDifferenceKeys,
        IEnumerable<string>? partiallyConfirmedDifferenceKeys,
        IEnumerable<string>? skippedDifferenceKeys,
        IEnumerable<int>? excludedRowIndexes,
        ImportDuplicateCheckOptions? duplicateCheckOptions,
        bool previewSkippedRows,
        bool cleanupSourceFile,
        TableData tableData,
        Func<RowData, ImportRowPayload> rowPayloadFactory,
        string sourceLabel,
        ImportIdempotencyContext? idempotency,
        CancellationToken cancellationToken,
        string? completedMessageOverride = null)
    {
        var importScopeKey = $"document-import:{scope.CompanyId}:{customerId}:{processId?.ToString() ?? "none"}:{machineModelId?.ToString() ?? "none"}";
        await using var importScopeLease = await _unitOfWork.AcquireOperationLockAsync(
            importScopeKey,
            cancellationToken);
        try
        {
        var result = new ImportResult
        {
            TotalCount = tableData.Rows.Count
        };

        var excludedSet = (excludedRowIndexes ?? [])
            .Where(index => index >= 0 && index < tableData.Rows.Count)
            .ToHashSet();
        if (excludedSet.Count > 0)
        {
            result.TotalCount = Math.Max(0, tableData.Rows.Count - excludedSet.Count);
        }

        var existingSpecsInScope = await LoadExistingSpecsForImportAsync(
            customerId,
            processId,
            machineModelId,
            scope,
            cancellationToken);
        // 先按当前数据范围建立重复检测会话；后续逐行只消费同一套确认/跳过规则，
        // 避免同批导入中重复判断口径前后不一致。
        var pendingDecisionMap = BuildPendingDecisionMap(
            scope,
            wordFile.Id,
            sourceIndex,
            customerId,
            processId,
            machineModelId,
            confirmedDifferenceKeys,
            partiallyConfirmedDifferenceKeys,
            skippedDifferenceKeys);
        var duplicateSession = await CreateDuplicateDetectionSessionAsync(
            existingSpecsInScope,
            pendingDecisionMap.Count > 0,
            duplicateCheckOptions,
            cancellationToken);
        var executionContext = CreateImportExecutionContext(
            result,
            existingSpecsInScope,
            confirmedDifferenceKeys,
            partiallyConfirmedDifferenceKeys,
            skippedDifferenceKeys,
            duplicateSession,
            pendingDecisionMap,
            customerId,
            processId,
            machineModelId,
            wordFile.Id,
            scope.UserId,
            scope.CompanyId,
            scope.OrgUnitId,
            previewSkippedRows);

        for (var relativeRowIndex = 0; relativeRowIndex < tableData.Rows.Count; relativeRowIndex++)
        {
            if (excludedSet.Contains(relativeRowIndex))
            {
                continue;
            }

            var row = tableData.Rows[relativeRowIndex];
            var payload = rowPayloadFactory(row);
            try
            {
                await ProcessImportRowAsync(executionContext, sourceIndex, payload, cancellationToken);
            }
            catch (AiServiceUnavailableException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.FailedCount++;
                result.Errors.Add(new ImportError
                {
                    RowIndex = payload.RowIndex,
                    Message = ex.Message
                });
            }
        }

        if (result.FailedCount > 0)
        {
            result.SuccessCount = 0;
            return new DocumentImportAppResult(
                result,
                $"导入失败：{result.FailedCount}条数据处理失败，本区域未写入任何数据");
        }

        if (result.PendingCount > 0)
        {
            // 已明确确认的覆盖允许在本轮落库；尚未确认的新增行仍留待下一轮，
            // 避免把用户已经确认的结果反复弹出。
            var pendingMessage = $"检测到{result.PendingCount}条重复或疑似重复数据，请逐条确认后再导入";
            if (executionContext.OverwriteCount > 0 || idempotency != null)
            {
                await _unitOfWork.BeginTransactionAsync(cancellationToken);
                await AddImportExecutionSnapshotAsync(
                    idempotency,
                    scope,
                    wordFile.Id,
                    result,
                    pendingMessage,
                    cleanupRequested: false);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
            }
            return new DocumentImportAppResult(
                result,
                pendingMessage);
        }

        var hasSpecificationChanges =
            executionContext.SpecsToInsert.Count > 0 || executionContext.OverwriteCount > 0;
        if (hasSpecificationChanges || idempotency != null)
        {
            // 解析、Embedding 与 AI 重复判断均已在事务外完成。这里只用短事务提交
            // 最终规格变更和幂等快照，避免慢外部调用长期占用数据库事务。
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            if (executionContext.SpecsToInsert.Count > 0)
            {
                await _unitOfWork.AcceptanceSpecs.AddRangeAsync(
                    executionContext.SpecsToInsert,
                    cancellationToken);
            }

            result.SuccessCount = executionContext.SpecsToInsert.Count + executionContext.OverwriteCount;
            var completedMessage = completedMessageOverride ?? BuildCompletedImportMessage(result);
            await AddImportExecutionSnapshotAsync(
                idempotency,
                scope,
                wordFile.Id,
                result,
                completedMessage,
                cleanupSourceFile);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }

        if (cleanupSourceFile && result.FailedCount == 0)
        {
            await TryCompleteSourceCleanupAsync(idempotency, wordFile, sourceLabel, cancellationToken);
        }

        _logger.LogInformation(
            "{SourceLabel}导入完成: 文件{FileId}, 索引{SourceIndex}, 客户{CustomerId}, 制程{ProcessId}, 机型{MachineModelId}, 成功{Success}, 失败{Failed}, 跳过{Skipped}",
            sourceLabel,
            wordFile.Id,
            sourceIndex,
            customerId,
            processId,
            machineModelId,
            result.SuccessCount,
            result.FailedCount,
            result.SkippedCount);

        // 只向宿主管理的后台服务提交合并信号；不创建脱离宿主生命周期的任务。
        if (result.SuccessCount > 0)
        {
            var accepted = _embeddingCacheWarmupTrigger.Request();
            _logger.LogDebug(
                accepted
                    ? "导入后已提交 Embedding 预热信号"
                    : "导入后 Embedding 预热信号已存在，本次触发已合并");
        }

        return new DocumentImportAppResult(
            result,
            completedMessageOverride ?? BuildCompletedImportMessage(result));
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }

    private static string BuildCompletedImportMessage(ImportResult result) =>
        $"导入完成：成功{result.SuccessCount}条，失败{result.FailedCount}条，跳过{result.SkippedCount}条";

    private async Task TryCompleteSourceCleanupAsync(
        ImportIdempotencyContext? idempotency,
        WordFile wordFile,
        string sourceLabel,
        CancellationToken cancellationToken)
    {
        try
        {
            DocumentImportExecution? execution = null;
            if (idempotency != null)
            {
                execution = await _importExecutions.GetByRequestKeyAsync(
                    idempotency.RequestKey,
                    cancellationToken);
            }
            await TryCompleteSourceCleanupAsync(execution, wordFile, sourceLabel, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{SourceLabel}导入后记录清理状态失败: fileId={FileId}", sourceLabel, wordFile.Id);
        }
    }

    private async Task TryCompleteSourceCleanupAsync(
        DocumentImportExecution? execution,
        WordFile wordFile,
        string sourceLabel,
        CancellationToken cancellationToken)
    {
        try
        {
            if (execution is { CleanupRequested: false } or { CleanupCompleted: true })
            {
                return;
            }

            var physicalPath = wordFile.FilePath;
            if (!string.IsNullOrWhiteSpace(physicalPath))
            {
                // 兼容既有“清理后仍可读取”契约，但对最大 50MB 文件只允许一个
                // 清理迁移占用大块托管内存，并使用单一缓冲区避免 MemoryStream.ToArray
                // 造成双份峰值内存。
                await SourceCleanupMemoryLease.WaitAsync(cancellationToken);
                try
                {
                    await using var stream = _documentFileAccessService.OpenReadStream(wordFile);
                    if (!stream.CanSeek || stream.Length > int.MaxValue)
                    {
                        throw new InvalidOperationException("源文件大小无法安全迁移到兼容存储");
                    }

                    var content = GC.AllocateUninitializedArray<byte>(checked((int)stream.Length));
                    await stream.ReadExactlyAsync(content, cancellationToken);
                    wordFile.FileContent = content;
                    wordFile.FilePath = null;
                }
                finally
                {
                    SourceCleanupMemoryLease.Release();
                }
            }

            if (execution != null)
            {
                execution.CleanupCompleted = true;
                _importExecutions.Update(execution);
            }

            if (!string.IsNullOrWhiteSpace(physicalPath) || execution != null)
            {
                // 数据库先持久化可读副本和完成状态；此后即使进程退出，物理文件只会
                // 成为可由孤儿巡检回收的冗余副本，不会留下不可读的文件记录。
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(physicalPath))
            {
                try
                {
                    await _documentFileAccessService.DeleteIfExistsAsync(physicalPath, cancellationToken);
                }
                catch (Exception deleteEx)
                {
                    _logger.LogWarning(
                        deleteEx,
                        "{SourceLabel}导入后删除冗余源文件失败: fileId={FileId}",
                        sourceLabel,
                        wordFile.Id);
                }
            }
        }
        catch (Exception ex)
        {
            // 规格与幂等快照已经提交，清理失败必须保持 pending，让同键重试补做，
            // 不能把已成功的导入伪装成整体失败。
            _logger.LogWarning(ex, "{SourceLabel}导入后清理源文件失败: fileId={FileId}", sourceLabel, wordFile.Id);
        }
    }


    private async Task<List<AcceptanceSpec>> LoadExistingSpecsForImportAsync(
        int customerId,
        int? processId,
        int? machineModelId,
        SpecAccessContext scope,
        CancellationToken cancellationToken)
    {
        return await scope.ApplySpecScopeToQuery(
                _unitOfWork.AcceptanceSpecs.Query(asNoTracking: false))
            .Where(spec =>
                spec.CustomerId == customerId &&
                spec.ProcessId == processId &&
                spec.MachineModelId == machineModelId)
            .OrderBy(spec => spec.Id)
            .ToListAsync(cancellationToken);
    }


    private static ImportExecutionContext CreateImportExecutionContext(
        ImportResult result,
        List<AcceptanceSpec> existingSpecs,
        IEnumerable<string>? confirmedDifferenceKeys,
        IEnumerable<string>? partiallyConfirmedDifferenceKeys,
        IEnumerable<string>? skippedDifferenceKeys,
        ImportDuplicateDetectionSession duplicateSession,
        Dictionary<string, PendingDecisionEntry> pendingDecisionMap,
        int customerId,
        int? processId,
        int? machineModelId,
        int fileId,
        int userId,
        int companyId,
        int? ownerOrgUnitId,
        bool previewSkippedRows)
    {
        return new ImportExecutionContext
        {
            PendingDecisionMap = pendingDecisionMap,
            Result = result,
            ExistingSpecs = existingSpecs,
            PendingInsertedSpecs = [],
            SpecsToInsert = [],
            ConfirmedDifferenceKeys = (confirmedDifferenceKeys ?? [])
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToHashSet(StringComparer.Ordinal),
            PartiallyConfirmedDifferenceKeys = (partiallyConfirmedDifferenceKeys ?? [])
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToHashSet(StringComparer.Ordinal),
            SkippedDifferenceKeys = (skippedDifferenceKeys ?? [])
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToHashSet(StringComparer.Ordinal),
            DuplicateSession = duplicateSession,
            CustomerId = customerId,
            ProcessId = processId,
            MachineModelId = machineModelId,
            FileId = fileId,
            UserId = userId,
            CompanyId = companyId,
            OwnerOrgUnitId = ownerOrgUnitId,
            PreviewSkippedRows = previewSkippedRows
        };
    }
}
