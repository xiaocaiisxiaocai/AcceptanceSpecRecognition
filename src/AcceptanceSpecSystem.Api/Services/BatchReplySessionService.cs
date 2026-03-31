using AcceptanceSpecSystem.Data.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 批量回复临时会话服务。
/// </summary>
public sealed class BatchReplySessionService
{
    private const string SessionCachePrefix = "batch-reply:session:";
    private const string ArtifactCachePrefix = "batch-reply:artifact:";
    private static readonly TimeSpan SessionRetention = TimeSpan.FromHours(4);
    private static readonly TimeSpan ArtifactRetention = TimeSpan.FromHours(24);

    private readonly IMemoryCache _memoryCache;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<BatchReplySessionService> _logger;

    public BatchReplySessionService(
        IMemoryCache memoryCache,
        IFileStorageService fileStorage,
        ILogger<BatchReplySessionService> logger)
    {
        _memoryCache = memoryCache;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    internal async Task<BatchReplySourceSession> CreateSourceSessionAsync(
        int userId,
        int companyId,
        string fileName,
        UploadedFileType fileType,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        var relativePath = await SaveTemporaryFileAsync(fileType, fileName, content, cancellationToken);
        var session = new BatchReplySourceSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            OwnerUserId = userId,
            OwnerCompanyId = companyId,
            SourceFileName = fileName,
            SourceFileType = fileType,
            SourceFileRelativePath = relativePath,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        SetSession(session);
        return session;
    }

    internal BatchReplySourceSession? GetSession(int userId, int companyId, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        if (!_memoryCache.TryGetValue(BuildSessionCacheKey(sessionId), out BatchReplySourceSession? session) ||
            session == null)
        {
            return null;
        }

        if (session.OwnerUserId != userId || session.OwnerCompanyId != companyId)
        {
            return null;
        }

        return session;
    }

    internal async Task<string?> SaveTargetFileAsync(
        string fileName,
        UploadedFileType fileType,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        return await SaveTemporaryFileAsync(fileType, fileName, content, cancellationToken);
    }

    internal async Task ReplacePreviewAsync(
        int userId,
        int companyId,
        string sessionId,
        IReadOnlyCollection<BatchReplySourceTable> sourceTables,
        IReadOnlyCollection<BatchReplyTargetFile> targetFiles,
        CancellationToken cancellationToken = default)
    {
        var session = GetSession(userId, companyId, sessionId);
        if (session == null)
        {
            return;
        }

        var oldTargetPaths = session.TargetFiles
            .Select(file => file.RelativePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();

        var updatedSession = new BatchReplySourceSession
        {
            SessionId = session.SessionId,
            OwnerUserId = session.OwnerUserId,
            OwnerCompanyId = session.OwnerCompanyId,
            SourceFileName = session.SourceFileName,
            SourceFileType = session.SourceFileType,
            SourceFileRelativePath = session.SourceFileRelativePath,
            CreatedAt = session.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            SourceTables = sourceTables.ToList(),
            TargetFiles = targetFiles.ToList()
        };

        SetSession(updatedSession);
        await DeleteRelativePathsAsync(oldTargetPaths, cancellationToken);
    }

    internal void SaveDownloadArtifact(int userId, int companyId, BatchReplyDownloadArtifact artifact)
    {
        artifact.OwnerUserId = userId;
        artifact.OwnerCompanyId = companyId;
        artifact.CreatedAt = DateTime.UtcNow;

        var cacheKey = BuildArtifactCacheKey(artifact.TaskId);
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ArtifactRetention
        };
        options.RegisterPostEvictionCallback(OnArtifactEvicted);
        _memoryCache.Set(cacheKey, artifact, options);
    }

    internal BatchReplyDownloadArtifact? GetDownloadArtifact(int userId, int companyId, string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return null;
        }

        if (!_memoryCache.TryGetValue(BuildArtifactCacheKey(taskId), out BatchReplyDownloadArtifact? artifact) ||
            artifact == null)
        {
            return null;
        }

        if (artifact.OwnerUserId != userId || artifact.OwnerCompanyId != companyId)
        {
            return null;
        }

        return artifact;
    }

    private void SetSession(BatchReplySourceSession session)
    {
        var cacheKey = BuildSessionCacheKey(session.SessionId);
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = SessionRetention
        };
        options.RegisterPostEvictionCallback(OnSessionEvicted);
        _memoryCache.Set(cacheKey, session, options);
    }

    private async Task<string> SaveTemporaryFileAsync(
        UploadedFileType fileType,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken)
    {
        return fileType == UploadedFileType.ExcelXlsx
            ? await _fileStorage.SaveUploadedExcelAsync(fileName, content, cancellationToken)
            : await _fileStorage.SaveUploadedWordAsync(fileName, content, cancellationToken);
    }

    private void OnSessionEvicted(object key, object? value, EvictionReason reason, object? state)
    {
        if (value is not BatchReplySourceSession session)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var paths = new List<string>();
                if (!string.IsNullOrWhiteSpace(session.SourceFileRelativePath))
                {
                    paths.Add(session.SourceFileRelativePath);
                }

                paths.AddRange(session.TargetFiles
                    .Select(file => file.RelativePath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Cast<string>());

                await DeleteRelativePathsAsync(paths, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理批量回复会话临时文件失败: {SessionId}", session.SessionId);
            }
        });
    }

    private void OnArtifactEvicted(object key, object? value, EvictionReason reason, object? state)
    {
        if (value is not BatchReplyDownloadArtifact artifact ||
            string.IsNullOrWhiteSpace(artifact.RelativePath))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _fileStorage.DeleteIfExistsAsync(artifact.RelativePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理批量回复下载产物失败: {TaskId}", artifact.TaskId);
            }
        });
    }

    private async Task DeleteRelativePathsAsync(
        IEnumerable<string> relativePaths,
        CancellationToken cancellationToken)
    {
        foreach (var relativePath in relativePaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await _fileStorage.DeleteIfExistsAsync(relativePath, cancellationToken);
        }
    }

    private static string BuildSessionCacheKey(string sessionId)
    {
        return $"{SessionCachePrefix}{sessionId}";
    }

    private static string BuildArtifactCacheKey(string taskId)
    {
        return $"{ArtifactCachePrefix}{taskId}";
    }
}

internal sealed class BatchReplySourceSession
{
    public string SessionId { get; set; } = string.Empty;
    public int OwnerUserId { get; set; }
    public int OwnerCompanyId { get; set; }
    public string SourceFileName { get; set; } = string.Empty;
    public UploadedFileType SourceFileType { get; set; }
    public string SourceFileRelativePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<BatchReplySourceTable> SourceTables { get; set; } = [];
    public List<BatchReplyTargetFile> TargetFiles { get; set; } = [];
}

internal sealed class BatchReplySourceTable
{
    public int TableIndex { get; set; }
    public int ProjectColumnIndex { get; set; }
    public int SpecificationColumnIndex { get; set; }
    public int AcceptanceColumnIndex { get; set; }
    public int? RemarkColumnIndex { get; set; }
    public int? HeaderRowStart { get; set; }
    public int? HeaderRowCount { get; set; }
    public int? DataStartRow { get; set; }
    public bool FilterEmptySourceRows { get; set; } = true;
    public List<BatchReplySourceRow> Rows { get; set; } = [];
}

internal sealed class BatchReplySourceRow
{
    public int RowIndex { get; set; }
    public string Project { get; set; } = string.Empty;
    public string Specification { get; set; } = string.Empty;
    public string Acceptance { get; set; } = string.Empty;
    public string? Remark { get; set; }
}

internal sealed class BatchReplyTargetFile
{
    public string TargetId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public UploadedFileType? FileType { get; set; }
    public string? RelativePath { get; set; }
    public bool CanApply { get; set; }
    public List<string> Errors { get; set; } = [];
}

internal sealed class BatchReplyDownloadArtifact
{
    public string TaskId { get; set; } = string.Empty;
    public int OwnerUserId { get; set; }
    public int OwnerCompanyId { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public DateTime CreatedAt { get; set; }
}
