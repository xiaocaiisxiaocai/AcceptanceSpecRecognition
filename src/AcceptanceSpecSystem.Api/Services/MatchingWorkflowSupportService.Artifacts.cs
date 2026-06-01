using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace AcceptanceSpecSystem.Api.Services;

public sealed partial class MatchingWorkflowSupportService
{
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
            await _unitOfWork.SaveChangesAsync();
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
            DownloadArtifactContentType = taskResult.DownloadArtifactContentType
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
