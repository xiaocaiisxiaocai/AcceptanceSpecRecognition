using System.IO.Compression;
using System.Security.Claims;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.Services;

public sealed partial class BatchReplyAppService
{
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


    private async Task<BatchReplyDownloadArtifact> SaveDownloadArtifactAsync(
        string taskId,
        string sourceFileName,
        IReadOnlyCollection<GeneratedArtifactFile> generatedFiles,
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
