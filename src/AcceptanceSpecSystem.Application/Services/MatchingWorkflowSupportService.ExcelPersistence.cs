using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Data.Entities;
using static AcceptanceSpecSystem.Application.Services.MatchingResultHelpers;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class MatchingWorkflowSupportService
{
    private async Task PersistExcelFillExecutionAsync(
        MatchingUserContext user,
        BatchExecuteFillRequest request,
        WordFile wordFile,
        string taskId,
        FillTaskResult taskResult,
        FillTaskResult persistedTaskResult,
        MatchingConfig executionConfig,
        Dictionary<int, AcceptanceSpec> specDict,
        Dictionary<int, HashSet<int>> adoptedRowLookup,
        Dictionary<int, ExecutionMatchSnapshot> currentMatchLookups,
        string requestFingerprint,
        CancellationToken cancellationToken)
    {
        var sourceSnapshot = await CaptureSourceFileRollbackSnapshotAsync(wordFile, cancellationToken);
        try
        {
            var renderedFile = await _matchingResultWriteBackService.RenderFillResultToSourceFileAsync(
                wordFile,
                taskResult,
                cancellationToken);
            EnsureWriteBackCompleted(renderedFile.Summary);

            persistedTaskResult.FileMutationPending = true;
            persistedTaskResult.SourceOriginalFilePath = sourceSnapshot.FilePath;
            persistedTaskResult.SourceOriginalFileHash = sourceSnapshot.FileHash;
            await _matchingTaskSnapshotService.PersistSourceRollbackArtifactAsync(
                persistedTaskResult,
                wordFile.FileName,
                sourceSnapshot.Content,
                cancellationToken);

            await PersistPendingFileMutationAsync(user, taskId, persistedTaskResult, cancellationToken);
            await FinalizeExcelFileMutationAsync(
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
                renderedFile.Content,
                sourceSnapshot,
                cancellationToken);
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

    private async Task PersistPendingFileMutationAsync(
        MatchingUserContext user,
        string taskId,
        FillTaskResult persistedTaskResult,
        CancellationToken cancellationToken)
    {
        // 先独立提交恢复日志。只有恢复日志可见后，才允许替换物理文件。
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _matchingTaskSnapshotService.SaveAsync(
                user,
                persistedTaskResult,
                saveImmediately: false,
                cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            var durablePendingTask = await LoadTaskFromFreshScopeAsync(user, taskId);
            if (durablePendingTask?.FileMutationPending != true)
            {
                await _matchingTaskSnapshotService.DeleteSourceRollbackArtifactAsync(
                    persistedTaskResult,
                    CancellationToken.None);
                throw;
            }
        }
    }

    private async Task FinalizeExcelFileMutationAsync(
        MatchingUserContext user,
        BatchExecuteFillRequest request,
        WordFile wordFile,
        string taskId,
        FillTaskResult taskResult,
        FillTaskResult persistedTaskResult,
        MatchingConfig executionConfig,
        Dictionary<int, AcceptanceSpec> specDict,
        Dictionary<int, HashSet<int>> adoptedRowLookup,
        Dictionary<int, ExecutionMatchSnapshot> currentMatchLookups,
        string requestFingerprint,
        byte[] renderedContent,
        SourceFileRollbackSnapshot sourceSnapshot,
        CancellationToken cancellationToken)
    {
        var finalCommitConfirmed = false;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            persistedTaskResult.FileMutationPending = false;
            await _matchingTaskSnapshotService.SaveAsync(
                user,
                persistedTaskResult,
                saveImmediately: false,
                cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

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

            await PersistExcelExecutionAsync(wordFile, renderedContent, cancellationToken);
            await PersistDownloadArtifactAsync(
                taskId,
                persistedTaskResult,
                wordFile,
                renderedContent,
                cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            finalCommitConfirmed = true;
            await _matchingTaskSnapshotService.CompleteDeferredExpiredArtifactCleanupAsync(cancellationToken);
        }
        catch (Exception finalizationException)
        {
            await _unitOfWork.RollbackTransactionAsync();
            var committedTask = await LoadTaskFromFreshScopeAsync(user, taskId);
            finalCommitConfirmed = committedTask != null &&
                                   !committedTask.FileMutationPending &&
                                   string.Equals(
                                       committedTask.RequestFingerprint,
                                       requestFingerprint,
                                       StringComparison.Ordinal);
            if (!finalCommitConfirmed)
            {
                await RestoreSourceFileAfterFailedExecutionAsync(wordFile, sourceSnapshot);
                await DeleteFailedDownloadArtifactAsync(persistedTaskResult);
                _matchingTaskSnapshotService.DiscardDeferredExpiredArtifactCleanup();
                await RecoverPendingTaskFromFreshScopeAsync(user, taskId, CancellationToken.None);
                throw;
            }

            _logger.LogWarning(
                finalizationException,
                "Excel 最终提交返回异常，但已从数据库确认提交成功: 任务{TaskId}",
                taskId);
        }

        if (finalCommitConfirmed)
        {
            await _matchingTaskSnapshotService.DeleteSourceRollbackArtifactAsync(
                persistedTaskResult,
                CancellationToken.None);
        }
    }
}
