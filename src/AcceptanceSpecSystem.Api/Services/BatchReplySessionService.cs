using AcceptanceSpecSystem.Data.Entities;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 批量回复临时会话服务。
/// </summary>
public sealed class BatchReplySessionService
{
    private const string SessionCachePrefix = "batch-reply:session:";
    private const string ArtifactCachePrefix = "batch-reply:artifact:";
    private const string SessionManifestBaseRelativeDir = "uploads/batch-reply/sessions";
    private const string ArtifactManifestBaseRelativeDir = "uploads/filled-files/manifests";
    private static readonly TimeSpan SessionRetention = TimeSpan.FromHours(4);
    private static readonly TimeSpan ArtifactRetention = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions SessionJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions ArtifactJsonOptions = new(JsonSerializerDefaults.Web);

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

        session.ManifestRelativePath = BuildSessionManifestRelativePath(session.SessionId);
        await PersistSessionManifestAsync(session, cancellationToken);
        await CleanupExpiredSessionManifestsAsync(cancellationToken);
        SetSession(session, SessionRetention);
        return session;
    }

    internal BatchReplySourceSession? GetSession(int userId, int companyId, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        if (_memoryCache.TryGetValue(BuildSessionCacheKey(sessionId), out BatchReplySourceSession? session) &&
            session != null)
        {
            return session.OwnerUserId == userId && session.OwnerCompanyId == companyId
                ? session
                : null;
        }

        session = LoadSessionManifest(sessionId);
        if (session == null)
        {
            return null;
        }

        if (session.OwnerUserId != userId || session.OwnerCompanyId != companyId)
        {
            return null;
        }

        var age = DateTime.UtcNow - session.UpdatedAt;
        var remainingRetention = SessionRetention - age;
        if (remainingRetention <= TimeSpan.Zero)
        {
            DeleteSessionFiles(session);
            return null;
        }

        SetSession(session, remainingRetention);
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
            ManifestRelativePath = session.ManifestRelativePath,
            CreatedAt = session.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            SourceTables = sourceTables.ToList(),
            TargetFiles = targetFiles.ToList()
        };

        await PersistSessionManifestAsync(updatedSession, cancellationToken);
        await CleanupExpiredSessionManifestsAsync(cancellationToken);
        SetSession(updatedSession, SessionRetention);
        await DeleteRelativePathsAsync(oldTargetPaths, cancellationToken);
    }

    internal async Task SaveDownloadArtifactAsync(
        int userId,
        int companyId,
        BatchReplyDownloadArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        artifact.OwnerUserId = userId;
        artifact.OwnerCompanyId = companyId;
        artifact.CreatedAt = DateTime.UtcNow;
        artifact.ManifestRelativePath = BuildArtifactManifestRelativePath(artifact.TaskId);

        await PersistArtifactManifestAsync(artifact, cancellationToken);
        await CleanupExpiredArtifactManifestsAsync(cancellationToken);
        SetArtifactCache(artifact, ArtifactRetention);
    }

    internal BatchReplyDownloadArtifact? GetDownloadArtifact(int userId, int companyId, string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return null;
        }

        if (_memoryCache.TryGetValue(BuildArtifactCacheKey(taskId), out BatchReplyDownloadArtifact? artifact) &&
            artifact != null)
        {
            return ValidateArtifactOwnership(artifact, userId, companyId)
                ? artifact
                : null;
        }

        artifact = LoadArtifactManifest(taskId);
        if (artifact == null)
        {
            return null;
        }

        if (!ValidateArtifactOwnership(artifact, userId, companyId))
        {
            return null;
        }

        var age = DateTime.UtcNow - artifact.CreatedAt;
        var remainingRetention = ArtifactRetention - age;
        if (remainingRetention <= TimeSpan.Zero)
        {
            DeleteArtifactFiles(artifact);
            return null;
        }

        SetArtifactCache(artifact, remainingRetention);
        return artifact;
    }

    private void SetSession(BatchReplySourceSession session, TimeSpan retention)
    {
        if (retention <= TimeSpan.Zero)
        {
            return;
        }

        var cacheKey = BuildSessionCacheKey(session.SessionId);
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = retention
        };
        _memoryCache.Set(cacheKey, session, options);
    }

    private void SetArtifactCache(BatchReplyDownloadArtifact artifact, TimeSpan retention)
    {
        if (retention <= TimeSpan.Zero)
        {
            return;
        }

        var cacheKey = BuildArtifactCacheKey(artifact.TaskId);
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = retention
        };
        _memoryCache.Set(cacheKey, artifact, options);
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

    private static string BuildSessionManifestRelativePath(string sessionId)
    {
        return $"{SessionManifestBaseRelativeDir}/{sessionId}.json";
    }

    private async Task PersistSessionManifestAsync(
        BatchReplySourceSession session,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.ManifestRelativePath))
        {
            throw new InvalidOperationException("会话清单路径不能为空");
        }

        var manifestPath = _fileStorage.GetAbsolutePath(session.ManifestRelativePath);
        var directory = Path.GetDirectoryName(manifestPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = JsonSerializer.Serialize(session, SessionJsonOptions);
        await File.WriteAllTextAsync(manifestPath, payload, cancellationToken);
    }

    private BatchReplySourceSession? LoadSessionManifest(string sessionId)
    {
        var manifestRelativePath = BuildSessionManifestRelativePath(sessionId);
        var manifestPath = _fileStorage.GetAbsolutePath(manifestRelativePath);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            var payload = File.ReadAllText(manifestPath);
            var session = JsonSerializer.Deserialize<BatchReplySourceSession>(payload, SessionJsonOptions);
            if (session == null)
            {
                return null;
            }

            session.ManifestRelativePath = manifestRelativePath;
            return session;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取批量回复会话清单失败: {SessionId}", sessionId);
            return null;
        }
    }

    private async Task CleanupExpiredSessionManifestsAsync(CancellationToken cancellationToken)
    {
        var manifestRoot = _fileStorage.GetAbsolutePath(SessionManifestBaseRelativeDir);
        if (!Directory.Exists(manifestRoot))
        {
            return;
        }

        foreach (var manifestPath in Directory.EnumerateFiles(manifestRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var payload = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                var session = JsonSerializer.Deserialize<BatchReplySourceSession>(payload, SessionJsonOptions);
                if (session == null || DateTime.UtcNow - session.UpdatedAt > SessionRetention)
                {
                    if (session != null)
                    {
                        session.ManifestRelativePath = BuildSessionManifestRelativePath(Path.GetFileNameWithoutExtension(manifestPath));
                        DeleteSessionFiles(session);
                    }
                    else
                    {
                        File.Delete(manifestPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理批量回复过期会话清单失败: {ManifestPath}", manifestPath);
            }
        }
    }

    private static bool ValidateArtifactOwnership(BatchReplyDownloadArtifact artifact, int userId, int companyId)
    {
        return artifact.OwnerUserId == userId && artifact.OwnerCompanyId == companyId;
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

    private static string BuildArtifactManifestRelativePath(string taskId)
    {
        return $"{ArtifactManifestBaseRelativeDir}/{taskId}.json";
    }

    private void DeleteSessionFiles(BatchReplySourceSession session)
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

            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var fullPath = _fileStorage.GetAbsolutePath(path);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }

            if (!string.IsNullOrWhiteSpace(session.ManifestRelativePath))
            {
                var manifestPath = _fileStorage.GetAbsolutePath(session.ManifestRelativePath);
                if (File.Exists(manifestPath))
                {
                    File.Delete(manifestPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "清理批量回复会话临时文件失败: {SessionId}", session.SessionId);
        }
    }

    private async Task PersistArtifactManifestAsync(
        BatchReplyDownloadArtifact artifact,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artifact.ManifestRelativePath))
        {
            throw new InvalidOperationException("下载产物清单路径不能为空");
        }

        var manifestPath = _fileStorage.GetAbsolutePath(artifact.ManifestRelativePath);
        var directory = Path.GetDirectoryName(manifestPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = JsonSerializer.Serialize(artifact, ArtifactJsonOptions);
        await File.WriteAllTextAsync(manifestPath, payload, cancellationToken);
    }

    private BatchReplyDownloadArtifact? LoadArtifactManifest(string taskId)
    {
        var manifestRelativePath = BuildArtifactManifestRelativePath(taskId);
        var manifestPath = _fileStorage.GetAbsolutePath(manifestRelativePath);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            var payload = File.ReadAllText(manifestPath);
            var artifact = JsonSerializer.Deserialize<BatchReplyDownloadArtifact>(payload, ArtifactJsonOptions);
            if (artifact == null)
            {
                return null;
            }

            artifact.ManifestRelativePath = manifestRelativePath;
            return artifact;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取批量回复下载清单失败: {TaskId}", taskId);
            return null;
        }
    }

    private async Task CleanupExpiredArtifactManifestsAsync(CancellationToken cancellationToken)
    {
        var manifestRoot = _fileStorage.GetAbsolutePath(ArtifactManifestBaseRelativeDir);
        if (!Directory.Exists(manifestRoot))
        {
            return;
        }

        foreach (var manifestPath in Directory.EnumerateFiles(manifestRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var payload = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                var artifact = JsonSerializer.Deserialize<BatchReplyDownloadArtifact>(payload, ArtifactJsonOptions);
                if (artifact == null || DateTime.UtcNow - artifact.CreatedAt > ArtifactRetention)
                {
                    if (artifact != null)
                    {
                        artifact.ManifestRelativePath = GetRelativePathFromAbsolute(manifestPath);
                        DeleteArtifactFiles(artifact);
                    }
                    else
                    {
                        File.Delete(manifestPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理批量回复过期下载清单失败: {ManifestPath}", manifestPath);
            }
        }
    }

    private void DeleteArtifactFiles(BatchReplyDownloadArtifact artifact)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(artifact.RelativePath))
            {
                var artifactPath = _fileStorage.GetAbsolutePath(artifact.RelativePath);
                if (File.Exists(artifactPath))
                {
                    File.Delete(artifactPath);
                }
            }

            if (!string.IsNullOrWhiteSpace(artifact.ManifestRelativePath))
            {
                var manifestPath = _fileStorage.GetAbsolutePath(artifact.ManifestRelativePath);
                if (File.Exists(manifestPath))
                {
                    File.Delete(manifestPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "清理批量回复下载产物失败: {TaskId}", artifact.TaskId);
        }
    }

    private string GetRelativePathFromAbsolute(string fullPath)
    {
        return BuildArtifactManifestRelativePath(Path.GetFileNameWithoutExtension(fullPath));
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
    public string? ManifestRelativePath { get; set; }
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
    public string? ManifestRelativePath { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public DateTime CreatedAt { get; set; }
}
