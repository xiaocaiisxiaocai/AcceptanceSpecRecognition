using System.Collections.Concurrent;
using System.Diagnostics;
using AcceptanceSpecSystem.Application.Options;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Interfaces;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 为已上传文档提供跨请求、短生命周期、容量有界的完整表格解析快照。
/// </summary>
public sealed class UploadedDocumentSnapshotService :
    IUploadedDocumentSnapshotProvider,
    IUploadedDocumentSnapshotInvalidator
{
    private readonly DocumentServiceFactory _documentServiceFactory;
    private readonly IUploadedDocumentPathResolver _documentPathResolver;
    private readonly IResourceBudgetGovernor _resourceBudgetGovernor;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly IOptions<UploadedDocumentSnapshotOptions> _options;
    private readonly ILogger<UploadedDocumentSnapshotService> _logger;
    private readonly MemoryCache _cache;
    private readonly ConcurrentDictionary<string, Task<CachedSnapshotEntry?>> _inflight =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<string, byte>> _fileIndex = new();
    private readonly ConcurrentDictionary<int, long> _invalidationVersions = new();
    private readonly object _cacheMutationGate = new();
    private readonly object _indexGate = new();
    private int _parseInvocationCount;

    public UploadedDocumentSnapshotService(
        DocumentServiceFactory documentServiceFactory,
        IUploadedDocumentPathResolver documentPathResolver,
        IResourceBudgetGovernor resourceBudgetGovernor,
        IHostApplicationLifetime hostApplicationLifetime,
        IOptions<UploadedDocumentSnapshotOptions> options,
        ILogger<UploadedDocumentSnapshotService> logger)
    {
        _documentServiceFactory = documentServiceFactory;
        _documentPathResolver = documentPathResolver;
        _resourceBudgetGovernor = resourceBudgetGovernor;
        _hostApplicationLifetime = hostApplicationLifetime;
        _options = options;
        _logger = logger;
        var configured = options.Value;
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = configured.Enabled && configured.TotalBudgetBytes > 0
                ? configured.TotalBudgetBytes
                : null
        });
    }

    internal int ParseInvocationCount => Volatile.Read(ref _parseInvocationCount);
    internal int IndexedKeyCount => _fileIndex.Values.Sum(keys => keys.Count);

    public async Task<DocumentTableSnapshot> GetSnapshotAsync(
        WordFile wordFile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wordFile);
        if (string.IsNullOrWhiteSpace(wordFile.FilePath) &&
            (wordFile.FileContent == null || wordFile.FileContent.Length == 0))
        {
            throw new ApplicationServiceException(400, "文件路径为空");
        }

        var configured = _options.Value;
        var contentKey = BuildContentKey(wordFile);
        if (!configured.Enabled)
        {
            var directSnapshot = await ParseSnapshotAsync(wordFile, cancellationToken);
            LogOutcome(wordFile, "disabled", 0, 0, directSnapshot, configured);
            return DocumentTableSnapshotCloner.Clone(directSnapshot);
        }

        if (_cache.TryGetValue(contentKey, out CachedSnapshotEntry? cachedEntry) && cachedEntry != null)
        {
            _cache.Set(
                contentKey,
                cachedEntry,
                CreateCacheEntryOptions(cachedEntry.EstimatedSizeBytes, configured));
            LogOutcome(wordFile, "hit", 0, 0, cachedEntry.Snapshot, configured);
            return DocumentTableSnapshotCloner.Clone(cachedEntry.Snapshot);
        }

        var waitStopwatch = Stopwatch.StartNew();
        Task<CachedSnapshotEntry?>? createdTask = null;
        IndexKey(wordFile.Id, contentKey);
        var sharedTask = _inflight.GetOrAdd(
            contentKey,
            _ =>
            {
                createdTask = StartInflightParseAsync(
                    wordFile,
                    contentKey,
                    configured,
                    GetInvalidationVersion(wordFile.Id));
                return createdTask!;
            });
        CachedSnapshotEntry? resolved;
        try
        {
            resolved = await sharedTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            if (sharedTask.IsCompleted && !sharedTask.IsCompletedSuccessfully)
            {
                _inflight.TryRemove(contentKey, out _);
            }

            throw;
        }

        if (resolved == null)
        {
            throw new ApplicationServiceException(500, "文档解析快照不可用");
        }

        var waitMs = waitStopwatch.ElapsedMilliseconds;
        var outcome = ReferenceEquals(sharedTask, createdTask) ? "miss" : "shared";
        LogOutcome(wordFile, outcome, waitMs, resolved.ParseElapsedMs, resolved.Snapshot, configured);
        return DocumentTableSnapshotCloner.Clone(resolved.Snapshot);
    }

    public void Invalidate(int fileId)
    {
        lock (_cacheMutationGate)
        {
            KeyValuePair<string, byte>[] indexed;
            lock (_indexGate)
            {
                if (!_fileIndex.TryRemove(fileId, out var keys))
                {
                    return;
                }

                indexed = keys.ToArray();
            }

            _invalidationVersions.AddOrUpdate(fileId, 1, static (_, version) => version + 1);
            foreach (var pair in indexed)
            {
                _cache.Remove(pair.Key);
                _inflight.TryRemove(pair.Key, out _);
            }
        }
    }

    private Task<CachedSnapshotEntry?> StartInflightParseAsync(
        WordFile wordFile,
        string contentKey,
        UploadedDocumentSnapshotOptions configured,
        long invalidationVersion)
    {
        var task = ParseAndMaybeCacheAsync(
            wordFile,
            contentKey,
            configured,
            invalidationVersion);
        _ = task.ContinueWith(
            _ =>
            {
                if (_inflight.TryGetValue(contentKey, out var current) &&
                    ReferenceEquals(current, task))
                {
                    _inflight.TryRemove(contentKey, out Task<CachedSnapshotEntry?>? removed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return task;
    }

    private async Task<CachedSnapshotEntry?> ParseAndMaybeCacheAsync(
        WordFile wordFile,
        string contentKey,
        UploadedDocumentSnapshotOptions configured,
        long invalidationVersion)
    {
        var parseStopwatch = Stopwatch.StartNew();
        var cached = false;
        try
        {
            var snapshot = await ParseSnapshotAsync(
                wordFile,
                _hostApplicationLifetime.ApplicationStopping);
            var estimatedSize = DocumentTableSnapshotSizeEstimator.EstimateBytes(snapshot);
            var entry = new CachedSnapshotEntry(snapshot, estimatedSize, parseStopwatch.ElapsedMilliseconds);

            if (estimatedSize > configured.MaxEntryBytes)
            {
                LogSkip(wordFile, estimatedSize, configured, "entry-too-large");
                return entry;
            }

            var chargeSize = Math.Max(configured.MinEntryChargeBytes, estimatedSize);
            lock (_cacheMutationGate)
            {
                if (GetInvalidationVersion(wordFile.Id) != invalidationVersion)
                {
                    return entry;
                }

                _cache.Set(contentKey, entry, CreateCacheEntryOptions(chargeSize, configured));
                cached = _cache.TryGetValue(contentKey, out CachedSnapshotEntry? stored) &&
                    stored != null;
            }
            return entry;
        }
        catch (OperationCanceledException) when (_hostApplicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "文档解析快照失败: FileId={FileId}, FileType={FileType}",
                wordFile.Id,
                wordFile.FileType);
            throw;
        }
        finally
        {
            if (!cached)
            {
                RemoveIndexedKey(contentKey, wordFile.Id);
            }
        }
    }

    private async Task<DocumentTableSnapshot> ParseSnapshotAsync(
        WordFile wordFile,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _parseInvocationCount);
        var parser = GetRequiredParser(wordFile.FileType);
        await using var stream = OpenReadStream(wordFile);
        if (stream.CanSeek)
        {
            _resourceBudgetGovernor.ValidateDocumentSize(stream.Length);
        }

        using var resourceLease = await _resourceBudgetGovernor.AcquireAsync(
            ResourceWorkload.DocumentParsing,
            cancellationToken);
        return await parser.ExtractDocumentSnapshotAsync(stream, cancellationToken);
    }

    private Stream OpenReadStream(WordFile wordFile)
    {
        if (!string.IsNullOrWhiteSpace(wordFile.FilePath))
        {
            var absolutePath = _documentPathResolver.ResolveAbsolutePath(wordFile.FilePath);
            if (File.Exists(absolutePath))
            {
                return File.OpenRead(absolutePath);
            }
        }

        if (wordFile.FileContent is { Length: > 0 })
        {
            return new MemoryStream(wordFile.FileContent, writable: false);
        }

        throw new ApplicationServiceException(400, "文件内容不可用");
    }

    private IDocumentParser GetRequiredParser(UploadedFileType fileType)
    {
        var parser = _documentServiceFactory.GetParser(
            fileType == UploadedFileType.ExcelXlsx ? DocumentType.Excel : DocumentType.Word);
        if (parser == null)
        {
            throw new ApplicationServiceException(500, "文档解析器不可用");
        }

        return parser;
    }

    private static string BuildContentKey(WordFile wordFile) =>
        $"{wordFile.Id}:{wordFile.FileHash}:{(int)wordFile.FileType}";

    private long GetInvalidationVersion(int fileId) =>
        _invalidationVersions.TryGetValue(fileId, out var version) ? version : 0;

    private void IndexKey(int fileId, string contentKey)
    {
        lock (_indexGate)
        {
            var keys = _fileIndex.GetOrAdd(
                fileId,
                _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
            keys[contentKey] = 0;
        }
    }

    private void RemoveIndexedKey(string contentKey, int? expectedFileId = null)
    {
        var separator = contentKey.IndexOf(':');
        if (separator <= 0 ||
            !int.TryParse(contentKey[..separator], out var fileId) ||
            (expectedFileId.HasValue && expectedFileId.Value != fileId))
        {
            return;
        }

        lock (_indexGate)
        {
            if (!_fileIndex.TryGetValue(fileId, out var keys))
            {
                return;
            }

            keys.TryRemove(contentKey, out _);
            if (keys.IsEmpty)
            {
                _fileIndex.TryRemove(fileId, out _);
            }
        }
    }

    private MemoryCacheEntryOptions CreateCacheEntryOptions(
        long estimatedSizeBytes,
        UploadedDocumentSnapshotOptions configured) =>
        new()
        {
            Size = estimatedSizeBytes,
            SlidingExpiration = TimeSpan.FromSeconds(Math.Max(1, configured.SlidingExpirationSeconds)),
            PostEvictionCallbacks =
            {
                new PostEvictionCallbackRegistration
                {
                    EvictionCallback = (key, _, reason, _) =>
                    {
                        if (reason != EvictionReason.Replaced)
                        {
                            RemoveIndexedKey(key?.ToString() ?? string.Empty);
                        }
                    }
                }
            }
        };

    private void LogOutcome(
        WordFile wordFile,
        string outcome,
        long waitMs,
        long parseMs,
        DocumentTableSnapshot snapshot,
        UploadedDocumentSnapshotOptions configured)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? "none";
        _logger.LogInformation(
            "文档解析快照: FileId={FileId}, FileType={FileType}, Outcome={Outcome}, TableCount={TableCount}, WaitMs={WaitMs}, ParseMs={ParseMs}, EstimatedBytes={EstimatedBytes}, TraceId={TraceId}",
            wordFile.Id,
            wordFile.FileType,
            outcome,
            snapshot.TableData.Count,
            waitMs,
            parseMs,
            DocumentTableSnapshotSizeEstimator.EstimateBytes(snapshot),
            traceId);
    }

    private void LogSkip(
        WordFile wordFile,
        long estimatedSizeBytes,
        UploadedDocumentSnapshotOptions configured,
        string reason)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? "none";
        _logger.LogInformation(
            "文档解析快照跳过缓存: FileId={FileId}, FileType={FileType}, Reason={Reason}, EstimatedBytes={EstimatedBytes}, MaxEntryBytes={MaxEntryBytes}, TraceId={TraceId}",
            wordFile.Id,
            wordFile.FileType,
            reason,
            estimatedSizeBytes,
            configured.MaxEntryBytes,
            traceId);
    }

    private sealed record CachedSnapshotEntry(
        DocumentTableSnapshot Snapshot,
        long EstimatedSizeBytes,
        long ParseElapsedMs);
}
