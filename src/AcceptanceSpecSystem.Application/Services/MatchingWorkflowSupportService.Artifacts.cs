using System.Collections.Concurrent;
using static AcceptanceSpecSystem.Application.Services.MatchingResultHelpers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
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
    private async Task<SmartFillResultArchiveDraft> SaveSmartFillResultArchiveAsync(
        WordFile wordFile,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var fileName = GetDownloadFileName(wordFile);
        var relativePath = await _fileStorage.SaveSmartFillResultArchiveAsync(
            fileName,
            content,
            cancellationToken);
        return new SmartFillResultArchiveDraft(
            relativePath,
            fileName,
            GetDownloadContentType(wordFile.FileType),
            content.LongLength,
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());
    }

    private async Task DeleteFailedResultArchiveAsync(SmartFillResultArchiveDraft? archive)
    {
        if (archive == null)
            return;

        try
        {
            await _fileStorage.DeleteIfExistsAsync(archive.RelativePath, CancellationToken.None);
        }
        catch (Exception cleanupException)
        {
            _logger.LogError(
                cleanupException,
                "填充失败后清理结果存档失败: {ArchivePath}",
                archive.RelativePath);
        }
    }

    private sealed record SourceFileRollbackSnapshot(
        string? FilePath,
        byte[] FileContent,
        string FileHash,
        byte[] Content);

    private static void EnsureWriteBackCompleted(WriteBackSummary summary)
    {
        if (summary.RequestedCells > 0 && summary.WrittenCells == 0)
        {
            throw Failure(400, "未写入任何单元格，请检查列索引和行配置是否正确");
        }

        if (summary.WrittenCells < summary.RequestedCells)
        {
            throw Failure(500, $"写回不完整：期望写入{summary.RequestedCells}个单元格，实际仅写入{summary.WrittenCells}个");
        }
    }

    private async Task PersistExcelExecutionAsync(WordFile wordFile, byte[] updatedContent, CancellationToken cancellationToken = default)
    {
        var originalContent = await ReadSourceFileContentAsync(wordFile, cancellationToken);
        var filePersisted = false;

        try
        {
            await _documentFileAccessService.PersistUpdatedFileContentAsync(wordFile, updatedContent, cancellationToken);
            filePersisted = true;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            if (filePersisted)
            {
                try
                {
                    await _documentFileAccessService.PersistUpdatedFileContentAsync(wordFile, originalContent, cancellationToken);
                }
                catch (Exception restoreEx)
                {
                    _logger.LogError(restoreEx, "Excel 源文件回滚失败: 文件{FileId}", wordFile.Id);
                }
            }

            throw;
        }
    }

    private async Task<byte[]> ReadSourceFileContentAsync(WordFile wordFile, CancellationToken cancellationToken)
    {
        await using var stream = _documentFileAccessService.OpenReadStream(wordFile);
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);
        return memoryStream.ToArray();
    }

    private async Task<SourceFileRollbackSnapshot> CaptureSourceFileRollbackSnapshotAsync(
        WordFile wordFile,
        CancellationToken cancellationToken)
    {
        return new SourceFileRollbackSnapshot(
            wordFile.FilePath,
            wordFile.FileContent.ToArray(),
            wordFile.FileHash,
            await ReadSourceFileContentAsync(wordFile, cancellationToken));
    }

    private async Task RestoreSourceFileAfterFailedExecutionAsync(
        WordFile wordFile,
        SourceFileRollbackSnapshot snapshot)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(snapshot.FilePath))
            {
                wordFile.FilePath = snapshot.FilePath;
                await _documentFileAccessService.PersistUpdatedFileContentAsync(
                    wordFile,
                    snapshot.Content,
                    CancellationToken.None);
            }
            else if (!string.IsNullOrWhiteSpace(wordFile.FilePath))
            {
                await _documentFileAccessService.DeleteIfExistsAsync(wordFile.FilePath, CancellationToken.None);
            }

            // 数据库事务会恢复持久化值；这里同步恢复当前 DbContext 中的实体状态，
            // 防止后续 SaveChanges 把失败写回产生的元数据再次提交。
            wordFile.FilePath = snapshot.FilePath;
            wordFile.FileContent = snapshot.FileContent.ToArray();
            wordFile.FileHash = snapshot.FileHash;
        }
        catch (Exception restoreException)
        {
            _logger.LogCritical(
                restoreException,
                "填充失败后恢复源文件失败: 文件{FileId}",
                wordFile.Id);
        }
    }

    private async Task DeleteFailedDownloadArtifactAsync(FillTaskResult taskResult)
    {
        if (string.IsNullOrWhiteSpace(taskResult.DownloadArtifactRelativePath))
        {
            return;
        }

        try
        {
            await _documentFileAccessService.DeleteIfExistsAsync(
                taskResult.DownloadArtifactRelativePath,
                CancellationToken.None);
            taskResult.DownloadArtifactRelativePath = null;
            taskResult.DownloadArtifactFileName = null;
            taskResult.DownloadArtifactContentType = null;
        }
        catch (Exception cleanupException)
        {
            _logger.LogError(
                cleanupException,
                "填充失败后清理下载产物失败: 任务{TaskId}",
                taskResult.TaskId);
        }
    }

    private async Task PersistDownloadArtifactAsync(
        string taskId,
        FillTaskResult taskResult,
        WordFile wordFile,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        await _matchingTaskSnapshotService.PersistDownloadArtifactAsync(
            taskId,
            taskResult,
            GetDownloadFileName(wordFile),
            GetDownloadContentType(wordFile.FileType),
            content,
            cancellationToken);
    }

    private static string GetDownloadFileName(WordFile wordFile)
    {
        var downloadFileName = Path.GetFileName(wordFile.FileName);
        if (!string.IsNullOrWhiteSpace(downloadFileName))
        {
            return downloadFileName;
        }

        return wordFile.FileType == UploadedFileType.ExcelXlsx ? "filled.xlsx" : "filled.docx";
    }

    private static string GetDownloadContentType(UploadedFileType fileType)
    {
        return fileType == UploadedFileType.ExcelXlsx
            ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            : "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    }

    private static FillTaskResult CreatePersistableTaskResult(FillTaskResult taskResult, bool includeFillEntries)
    {
        return new FillTaskResult
        {
            PayloadVersion = taskResult.PayloadVersion,
            TaskId = taskResult.TaskId,
            SourceFileId = taskResult.SourceFileId,
            RequestFingerprint = taskResult.RequestFingerprint,
            FilledCount = taskResult.FilledCount,
            SkippedCount = taskResult.SkippedCount,
            SourceTableIndex = taskResult.SourceTableIndex,
            AcceptanceColumnIndex = taskResult.AcceptanceColumnIndex,
            RemarkColumnIndex = taskResult.RemarkColumnIndex,
            FillResults = includeFillEntries
                ? taskResult.FillResults
                    .Select(CloneFillResult)
                    .ToList()
                : [],
            FilledFilePath = taskResult.FilledFilePath,
            CreatedAt = taskResult.CreatedAt,
            IsBatchMode = taskResult.IsBatchMode,
            TableEntries = includeFillEntries
                ? taskResult.TableEntries
                    .Select(entry => new TableFillEntry
                    {
                        TableIndex = entry.TableIndex,
                        AcceptanceColumnIndex = entry.AcceptanceColumnIndex,
                        RemarkColumnIndex = entry.RemarkColumnIndex,
                        FillResults = entry.FillResults
                            .Select(CloneFillResult)
                            .ToList()
                    })
                    .ToList()
                : [],
            DownloadArtifactRelativePath = taskResult.DownloadArtifactRelativePath,
            DownloadArtifactFileName = taskResult.DownloadArtifactFileName,
            DownloadArtifactContentType = taskResult.DownloadArtifactContentType,
            FileMutationPending = taskResult.FileMutationPending,
            SourceRollbackArtifactRelativePath = taskResult.SourceRollbackArtifactRelativePath,
            SourceOriginalFilePath = taskResult.SourceOriginalFilePath,
            SourceOriginalFileHash = taskResult.SourceOriginalFileHash
        };
    }

    private static FillResult CloneFillResult(FillResult fillResult)
    {
        return new FillResult
        {
            RowIndex = fillResult.RowIndex,
            SpecId = fillResult.SpecId,
            Acceptance = fillResult.Acceptance,
            Remark = fillResult.Remark
        };
    }

    private static bool TryCreateManualFillResult(FillMapping mapping, out FillResult fillResult)
    {
        fillResult = null!;
        if (!mapping.ManualFill)
        {
            return false;
        }

        var hasManualValue =
            !string.IsNullOrWhiteSpace(mapping.OverrideAcceptance) ||
            !string.IsNullOrWhiteSpace(mapping.OverrideRemark);
        if (!hasManualValue)
        {
            return false;
        }

        fillResult = new FillResult
        {
            RowIndex = mapping.RowIndex,
            SpecId = 0,
            Acceptance = mapping.OverrideAcceptance ?? string.Empty,
            Remark = mapping.OverrideRemark
        };
        return true;
    }

}
