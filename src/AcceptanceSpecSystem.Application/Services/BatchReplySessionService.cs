using System.Collections.Concurrent;
using System.Text.Json;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.Extensions.Caching.Memory;

using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 批量回复临时会话服务。
/// </summary>
public sealed class BatchReplySessionService
{
    private const string SessionCachePrefix = "batch-reply:session:";
    private const string ArtifactCachePrefix = "batch-reply:artifact:";
    private const string SessionManifestBaseRelativeDir = "uploads/batch-reply/sessions";
    private const string ArtifactManifestBaseRelativeDir = "uploads/filled-files/manifests";
    private static readonly JsonSerializerOptions SessionJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions ArtifactJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMemoryCache _memoryCache;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<BatchReplySessionService> _logger;
    private readonly BatchReplyRetentionPolicy _retentionPolicy;
    private readonly BatchReplySessionCoordinator _coordinator;
    private readonly TimeProvider _timeProvider;

    public BatchReplySessionService(
        IMemoryCache memoryCache,
        IFileStorageService fileStorage,
        ILogger<BatchReplySessionService> logger,
        BatchReplyRetentionPolicy retentionPolicy,
        BatchReplySessionCoordinator coordinator,
        TimeProvider timeProvider)
    {
        _memoryCache = memoryCache;
        _fileStorage = fileStorage;
        _logger = logger;
        _retentionPolicy = retentionPolicy;
        _coordinator = coordinator;
        _timeProvider = timeProvider;
    }

    public async Task<BatchReplySourceSession> CreateSourceSessionAsync(
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
            CreatedAt = UtcNow,
            UpdatedAt = UtcNow
        };

        session.ManifestRelativePath = BuildSessionManifestRelativePath(session.SessionId);
        await PersistSessionManifestAsync(session, cancellationToken);
        SetSession(session, _retentionPolicy.SessionRetention);
        return session;
    }

    public BatchReplySourceSession? GetSession(int userId, int companyId, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        if (_memoryCache.TryGetValue(BuildSessionCacheKey(sessionId), out BatchReplySourceSession? session) &&
            session != null)
        {
            if (IsSessionExpired(session))
            {
                _memoryCache.Remove(BuildSessionCacheKey(sessionId));
                DeleteSessionFiles(session);
                return null;
            }

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

        var age = UtcNow - session.UpdatedAt;
        var remainingRetention = _retentionPolicy.SessionRetention - age;
        if (remainingRetention <= TimeSpan.Zero)
        {
            DeleteSessionFiles(session);
            return null;
        }

        SetSession(session, remainingRetention);
        return session;
    }

    public async Task<string?> SaveTargetFileAsync(
        string fileName,
        UploadedFileType fileType,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        return await SaveTemporaryFileAsync(fileType, fileName, content, cancellationToken);
    }

    public async Task<BatchReplySourceSession?> AddTargetFilesAsync(
        int userId,
        int companyId,
        string sessionId,
        IReadOnlyCollection<BatchReplyTargetFile> targetFiles,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteSessionMutationAsync(
            sessionId,
            cancellationToken,
            async session =>
            {
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
                    UpdatedAt = UtcNow,
                    SourceTables = session.SourceTables.ToList(),
                    TargetFiles = session.TargetFiles
                        .Concat(targetFiles)
                        .ToList()
                };

                await PersistSessionManifestAsync(updatedSession, cancellationToken);
                SetSession(updatedSession, _retentionPolicy.SessionRetention);
                return updatedSession;
            },
            userId,
            companyId);
    }

    public async Task ReplacePreviewAsync(
        int userId,
        int companyId,
        string sessionId,
        IReadOnlyCollection<BatchReplySourceTable> sourceTables,
        IReadOnlyCollection<BatchReplyTargetFile> targetFiles,
        CancellationToken cancellationToken = default)
    {
        string[] oldTargetPaths = [];

        var updatedSession = await ExecuteSessionMutationAsync(
            sessionId,
            cancellationToken,
            async session =>
            {
                oldTargetPaths = session.TargetFiles
                    .Select(file => file.RelativePath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Cast<string>()
                    .ToArray();

                var nextSession = new BatchReplySourceSession
                {
                    SessionId = session.SessionId,
                    OwnerUserId = session.OwnerUserId,
                    OwnerCompanyId = session.OwnerCompanyId,
                    SourceFileName = session.SourceFileName,
                    SourceFileType = session.SourceFileType,
                    SourceFileRelativePath = session.SourceFileRelativePath,
                    ManifestRelativePath = session.ManifestRelativePath,
                    CreatedAt = session.CreatedAt,
                    UpdatedAt = UtcNow,
                    SourceTables = sourceTables.ToList(),
                    TargetFiles = targetFiles.ToList()
                };

                await PersistSessionManifestAsync(nextSession, cancellationToken);
                SetSession(nextSession, _retentionPolicy.SessionRetention);
                return nextSession;
            },
            userId,
            companyId);

        if (updatedSession == null)
        {
            return;
        }

        await DeleteRelativePathsAsync(oldTargetPaths, cancellationToken);
    }

    public async Task SaveDownloadArtifactAsync(
        int userId,
        int companyId,
        BatchReplyDownloadArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        await using var artifactLock = await _coordinator.AcquireArtifactAsync(artifact.TaskId, cancellationToken);
        artifact.OwnerUserId = userId;
        artifact.OwnerCompanyId = companyId;
        artifact.CreatedAt = UtcNow;
        artifact.ManifestRelativePath = BuildArtifactManifestRelativePath(artifact.TaskId);

        await PersistArtifactManifestAsync(artifact, cancellationToken);
        SetArtifactCache(artifact, _retentionPolicy.ArtifactRetention);
    }

    public BatchReplyDownloadArtifact? GetDownloadArtifact(int userId, int companyId, string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return null;
        }

        if (_memoryCache.TryGetValue(BuildArtifactCacheKey(taskId), out BatchReplyDownloadArtifact? artifact) &&
            artifact != null)
        {
            if (IsArtifactExpired(artifact))
            {
                _memoryCache.Remove(BuildArtifactCacheKey(taskId));
                DeleteArtifactFiles(artifact);
                return null;
            }

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

        var age = UtcNow - artifact.CreatedAt;
        var remainingRetention = _retentionPolicy.ArtifactRetention - age;
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

    private async Task<BatchReplySourceSession?> ExecuteSessionMutationAsync(
        string sessionId,
        CancellationToken cancellationToken,
        Func<BatchReplySourceSession, Task<BatchReplySourceSession>> mutation,
        int userId,
        int companyId)
    {
        await using var sessionLock = await _coordinator.AcquireSessionAsync(sessionId, cancellationToken);
        var session = GetSession(userId, companyId, sessionId);
        if (session == null)
        {
            return null;
        }

        return await mutation(session);
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

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    private bool IsSessionExpired(BatchReplySourceSession session)
    {
        return UtcNow - session.UpdatedAt >= _retentionPolicy.SessionRetention;
    }

    private bool IsArtifactExpired(BatchReplyDownloadArtifact artifact)
    {
        return UtcNow - artifact.CreatedAt >= _retentionPolicy.ArtifactRetention;
    }
}

/// <summary>
/// 按键提供互斥锁，并在最后一个持有者或等待者退出后回收锁对象。
/// </summary>
public sealed class ReferenceCountedKeyedLock<TKey> where TKey : notnull
{
    private readonly object _gate = new();
    private readonly Dictionary<TKey, LockEntry> _entries;

    public ReferenceCountedKeyedLock(IEqualityComparer<TKey>? comparer = null)
    {
        _entries = new Dictionary<TKey, LockEntry>(comparer);
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public async ValueTask<IDisposable> AcquireAsync(
        TKey key,
        CancellationToken cancellationToken = default)
    {
        LockEntry entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new LockEntry();
                _entries.Add(key, entry);
            }

            entry.ReferenceCount++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new LockLease(this, key, entry);
        }
        catch
        {
            ReleaseReference(key, entry);
            throw;
        }
    }

    private void Release(TKey key, LockEntry entry)
    {
        entry.Semaphore.Release();
        ReleaseReference(key, entry);
    }

    private void ReleaseReference(TKey key, LockEntry entry)
    {
        lock (_gate)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount != 0)
            {
                return;
            }

            if (_entries.TryGetValue(key, out var currentEntry) && ReferenceEquals(currentEntry, entry))
            {
                _entries.Remove(key);
            }

            entry.Semaphore.Dispose();
        }
    }

    private sealed class LockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int ReferenceCount { get; set; }
    }

    private sealed class LockLease : IDisposable
    {
        private ReferenceCountedKeyedLock<TKey>? _owner;
        private readonly TKey _key;
        private readonly LockEntry _entry;

        public LockLease(ReferenceCountedKeyedLock<TKey> owner, TKey key, LockEntry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release(_key, _entry);
        }
    }
}

public sealed class BatchReplySourceSession
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

public sealed class BatchReplySourceTable
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
    public List<BatchReplyDuplicateResolutionDto> DuplicateResolutions { get; set; } = [];
    public List<BatchReplySourceRow> Rows { get; set; } = [];
}

public sealed class BatchReplySourceRow
{
    public int RowIndex { get; set; }
    public string Project { get; set; } = string.Empty;
    public string Specification { get; set; } = string.Empty;
    public string Acceptance { get; set; } = string.Empty;
    public string? Remark { get; set; }
}

public sealed class BatchReplyWriteTable
{
    public int TableIndex { get; set; }
    public int AcceptanceColumnIndex { get; set; }
    public int? RemarkColumnIndex { get; set; }
    public List<BatchReplyWriteRow> Rows { get; set; } = [];
}

public sealed class BatchReplyWriteRow
{
    public int RowIndex { get; set; }
    public string Project { get; set; } = string.Empty;
    public string Specification { get; set; } = string.Empty;
    public string Acceptance { get; set; } = string.Empty;
    public string? Remark { get; set; }
}

public sealed class BatchReplyTargetFile
{
    public string TargetId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public UploadedFileType? FileType { get; set; }
    public string? RelativePath { get; set; }
    public int TableCount { get; set; }
    public bool CanApply { get; set; }
    public List<string> Errors { get; set; } = [];
}

public sealed class BatchReplyDownloadArtifact
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
