using System.Text;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Services;

public sealed partial class DocumentImportAppService
{
    private async Task ValidateImportTargetAsync(int customerId, int? processId, int? machineModelId)
    {
        var customer = await _unitOfWork.Customers.GetByIdAsync(customerId);
        if (customer == null)
        {
            throw new ApplicationServiceException(400, "客户不存在");
        }

        if (processId.HasValue)
        {
            var process = await _unitOfWork.Processes.GetByIdAsync(processId.Value);
            if (process == null)
            {
                throw new ApplicationServiceException(400, "制程不存在");
            }
        }

        if (machineModelId.HasValue)
        {
            var machineModel = await _unitOfWork.MachineModels.GetByIdAsync(machineModelId.Value);
            if (machineModel == null)
            {
                throw new ApplicationServiceException(400, "机型不存在");
            }
        }
    }

    private async Task<DocumentImportAppResult> ExecuteImportAsync(
        DataScopeResult scope,
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
        CancellationToken cancellationToken)
    {
        var result = new ImportResult
        {
            TotalCount = tableData.Rows.Count
        };

        var excludedSet = (excludedRowIndexes ?? [])
            .Where(index => index >= 0)
            .ToHashSet();
        if (excludedSet.Count > 0)
        {
            result.TotalCount = Math.Max(0, tableData.Rows.Count - tableData.Rows.Count(row => excludedSet.Contains(row.Index)));
        }

        var existingSpecsInScope = await LoadExistingSpecsForImportAsync(
            customerId,
            processId,
            machineModelId,
            scope,
            cancellationToken);
        var duplicateSession = await CreateDuplicateDetectionSessionAsync(
            existingSpecsInScope,
            confirmedDifferenceKeys,
            partiallyConfirmedDifferenceKeys,
            skippedDifferenceKeys,
            duplicateCheckOptions,
            cancellationToken);
        var executionContext = CreateImportExecutionContext(
            result,
            existingSpecsInScope,
            confirmedDifferenceKeys,
            partiallyConfirmedDifferenceKeys,
            skippedDifferenceKeys,
            duplicateSession,
            customerId,
            processId,
            machineModelId,
            wordFile.Id,
            scope.UserId,
            scope.OrgUnitId,
            previewSkippedRows);

        foreach (var row in tableData.Rows)
        {
            if (excludedSet.Contains(row.Index))
            {
                continue;
            }

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

        if (result.PendingCount > 0)
        {
            return new DocumentImportAppResult(
                result,
                $"检测到{result.PendingCount}条重复或疑似重复数据，请逐条确认后再导入");
        }

        if (executionContext.SpecsToInsert.Count > 0 || executionContext.OverwriteCount > 0)
        {
            await _unitOfWork.BeginTransactionAsync();
            try
            {
                if (executionContext.SpecsToInsert.Count > 0)
                {
                    await _unitOfWork.AcceptanceSpecs.AddRangeAsync(executionContext.SpecsToInsert);
                }

                await _unitOfWork.SaveChangesAsync();
                await _unitOfWork.CommitTransactionAsync();
                result.SuccessCount = executionContext.SpecsToInsert.Count + executionContext.OverwriteCount;
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }

        if (cleanupSourceFile)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(wordFile.FilePath))
                {
                    await using var stream = _documentFileAccessService.OpenReadStream(wordFile);
                    using var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream, cancellationToken);
                    wordFile.FileContent = memoryStream.ToArray();
                    await _documentFileAccessService.DeleteIfExistsAsync(wordFile.FilePath, cancellationToken);
                    wordFile.FilePath = null;
                }

                await _unitOfWork.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{SourceLabel}导入后清理源文件失败: fileId={FileId}", sourceLabel, wordFile.Id);
            }
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

        // 导入成功后后台触发 Embedding 预热，不阻塞当前请求
        if (result.SuccessCount > 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    // 刻意使用 CancellationToken.None：这是脱离当前请求的 fire-and-forget 预热，
                    // 不应随请求结束/客户端断开而取消，否则预热会被立即中断失去意义。
                    await _embeddingCacheWarmupManager.RunOnceAsync(CancellationToken.None);
                    _logger.LogInformation("导入后自动触发 Embedding 预热完成");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "导入后自动触发 Embedding 预热失败");
                }
            });
        }

        return new DocumentImportAppResult(
            result,
            $"导入完成：成功{result.SuccessCount}条，失败{result.FailedCount}条，跳过{result.SkippedCount}条");
    }


    private async Task<List<AcceptanceSpec>> LoadExistingSpecsForImportAsync(
        int customerId,
        int? processId,
        int? machineModelId,
        DataScopeResult scope,
        CancellationToken cancellationToken)
    {
        return await SpecDataScopeHelper.ApplyScopeToQuery(
                _unitOfWork.AcceptanceSpecs.Query(asNoTracking: false),
                scope)
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
        int customerId,
        int? processId,
        int? machineModelId,
        int fileId,
        int userId,
        int? ownerOrgUnitId,
        bool previewSkippedRows)
    {
        return new ImportExecutionContext
        {
            PendingDecisionMap = BuildPendingDecisionMap(
                confirmedDifferenceKeys,
                partiallyConfirmedDifferenceKeys,
                skippedDifferenceKeys),
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
            OwnerOrgUnitId = ownerOrgUnitId,
            PreviewSkippedRows = previewSkippedRows
        };
    }
}
