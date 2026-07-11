using System.Text.Json;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Application.Services;

public sealed record OrphanFileSnapshot(
    string RelativePath,
    DateTimeOffset LastWriteTimeUtc,
    long Length);

public sealed record OrphanReferenceSnapshot(
    IReadOnlySet<string> ReferencedPaths,
    IReadOnlySet<string> IncompleteNamespaces,
    int FailureCount)
{
    public bool IsCompleteFor(string relativePath)
    {
        var managedNamespace = OrphanFilePathRules.GetManagedNamespace(relativePath);
        return managedNamespace != null && !IncompleteNamespaces.Contains(managedNamespace);
    }
}

public sealed record OrphanReferenceProbe(bool IsReferenced, bool IsComplete, int FailureCount = 0);

public interface IOrphanFileStore
{
    IReadOnlyList<OrphanFileSnapshot> EnumerateManagedFiles();

    Task<OrphanReferenceSnapshot> ReadManifestReferencesAsync(CancellationToken cancellationToken);

    Task<OrphanReferenceProbe> ProbeManifestReferenceAsync(
        string relativePath,
        CancellationToken cancellationToken);

    Task<bool> DeleteIfUnchangedAsync(
        OrphanFileSnapshot snapshot,
        CancellationToken cancellationToken);
}

public interface IOrphanDatabaseReferenceQuery
{
    Task<OrphanReferenceSnapshot> ReadReferencesAsync(CancellationToken cancellationToken);

    Task<OrphanReferenceProbe> ProbeReferenceAsync(
        string relativePath,
        CancellationToken cancellationToken);
}

public sealed record OrphanFileInspectionRequest(bool ObservationMode, TimeSpan GracePeriod);

public sealed record OrphanFileInspectionResult(
    bool SkippedBecauseAlreadyRunning,
    bool ObservationMode,
    int Scanned,
    int Retained,
    int Referenced,
    int Eligible,
    int Deleted,
    int Failures,
    TimeSpan Elapsed);

public interface IOrphanFileInspectionAppService
{
    Task<OrphanFileInspectionResult> InspectAsync(
        OrphanFileInspectionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class OrphanFileInspectionCoordinator
{
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly object _candidateGate = new();
    private readonly Dictionary<string, StableCandidate> _candidates = new(StringComparer.OrdinalIgnoreCase);
    private long _generation;

    public async ValueTask<RunLease?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        if (!await _runGate.WaitAsync(0, cancellationToken))
        {
            return null;
        }

        return new RunLease(this, _runGate, Interlocked.Increment(ref _generation));
    }

    public bool ConfirmStable(OrphanFileSnapshot snapshot, long generation)
    {
        var path = OrphanFilePathRules.Normalize(snapshot.RelativePath);
        lock (_candidateGate)
        {
            if (_candidates.TryGetValue(path, out var previous) &&
                previous.Generation < generation &&
                previous.LastWriteTimeUtc == snapshot.LastWriteTimeUtc &&
                previous.Length == snapshot.Length)
            {
                _candidates[path] = new StableCandidate(snapshot.LastWriteTimeUtc, snapshot.Length, generation);
                return true;
            }

            _candidates[path] = new StableCandidate(snapshot.LastWriteTimeUtc, snapshot.Length, generation);
            return false;
        }
    }

    public void Forget(string relativePath)
    {
        lock (_candidateGate)
        {
            _candidates.Remove(OrphanFilePathRules.Normalize(relativePath));
        }
    }

    public void InvalidateAllCandidates()
    {
        lock (_candidateGate)
        {
            _candidates.Clear();
        }
    }

    private sealed record StableCandidate(DateTimeOffset LastWriteTimeUtc, long Length, long Generation);

    public sealed class RunLease(
        OrphanFileInspectionCoordinator owner,
        SemaphoreSlim gate,
        long generation) : IDisposable
    {
        private OrphanFileInspectionCoordinator? _owner = owner;
        private SemaphoreSlim? _gate = gate;
        private bool _completed;

        public long Generation { get; } = generation;

        public void Complete() => _completed = true;

        public void Dispose()
        {
            var currentOwner = Interlocked.Exchange(ref _owner, null);
            if (!_completed)
            {
                currentOwner?.InvalidateAllCandidates();
            }

            Interlocked.Exchange(ref _gate, null)?.Release();
        }
    }
}

/// <summary>
/// 巡检由文件系统枚举驱动，但删除证明同时要求数据库引用与 manifest 引用均完整且不存在。
/// 任一引用源不可读时按命名空间 fail closed，不猜测文件已失去引用。
/// </summary>
public sealed class OrphanFileInspectionAppService : IOrphanFileInspectionAppService
{
    private readonly IOrphanFileStore _store;
    private readonly IOrphanDatabaseReferenceQuery _databaseReferences;
    private readonly OrphanFileInspectionCoordinator _coordinator;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrphanFileInspectionAppService> _logger;

    public OrphanFileInspectionAppService(
        IOrphanFileStore store,
        IOrphanDatabaseReferenceQuery databaseReferences,
        OrphanFileInspectionCoordinator coordinator,
        TimeProvider timeProvider,
        ILogger<OrphanFileInspectionAppService> logger)
    {
        _store = store;
        _databaseReferences = databaseReferences;
        _coordinator = coordinator;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<OrphanFileInspectionResult> InspectAsync(
        OrphanFileInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.GracePeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "孤儿文件安全宽限期必须大于 0");
        }

        using var runLease = await _coordinator.TryAcquireAsync(cancellationToken);
        if (runLease == null)
        {
            return new OrphanFileInspectionResult(
                true, request.ObservationMode, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var startedAt = _timeProvider.GetTimestamp();
        IReadOnlyList<OrphanFileSnapshot> files;
        try
        {
            files = _store.EnumerateManagedFiles();
        }
        catch (Exception ex)
        {
            _coordinator.InvalidateAllCandidates();
            _logger.LogWarning(ex, "枚举受管文件失败，本轮孤儿巡检不执行删除");
            return new OrphanFileInspectionResult(
                false, request.ObservationMode, 0, 0, 0, 0, 0, 1,
                _timeProvider.GetElapsedTime(startedAt));
        }

        OrphanReferenceSnapshot databaseSnapshot;
        try
        {
            databaseSnapshot = await _databaseReferences.ReadReferencesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _coordinator.InvalidateAllCandidates();
            _logger.LogWarning(ex, "读取数据库文件引用失败，本轮孤儿巡检 fail closed");
            return new OrphanFileInspectionResult(
                false, request.ObservationMode, files.Count, files.Count, 0, 0, 0, 1,
                _timeProvider.GetElapsedTime(startedAt));
        }

        OrphanReferenceSnapshot manifestSnapshot;
        try
        {
            manifestSnapshot = await _store.ReadManifestReferencesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _coordinator.InvalidateAllCandidates();
            _logger.LogWarning(ex, "读取文件清单引用失败，本轮孤儿巡检 fail closed");
            return new OrphanFileInspectionResult(
                false, request.ObservationMode, files.Count, files.Count, 0, 0, 0, 1,
                _timeProvider.GetElapsedTime(startedAt));
        }

        var metrics = new InspectionMetrics(request.ObservationMode)
        {
            Failures = databaseSnapshot.FailureCount + manifestSnapshot.FailureCount
        };
        var cutoff = _timeProvider.GetUtcNow() - request.GracePeriod;

        foreach (var file in files.OrderBy(item => item.RelativePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            metrics.Scanned++;

            if (!OrphanFilePathRules.IsManagedContentPath(file.RelativePath) ||
                file.LastWriteTimeUtc > cutoff ||
                file.LastWriteTimeUtc > _timeProvider.GetUtcNow())
            {
                _coordinator.Forget(file.RelativePath);
                metrics.Retained++;
                continue;
            }

            if (!databaseSnapshot.IsCompleteFor(file.RelativePath) ||
                !manifestSnapshot.IsCompleteFor(file.RelativePath))
            {
                _coordinator.Forget(file.RelativePath);
                metrics.Retained++;
                continue;
            }

            var normalizedPath = OrphanFilePathRules.Normalize(file.RelativePath);
            if (databaseSnapshot.ReferencedPaths.Contains(normalizedPath) ||
                manifestSnapshot.ReferencedPaths.Contains(normalizedPath))
            {
                _coordinator.Forget(file.RelativePath);
                metrics.Referenced++;
                metrics.Retained++;
                continue;
            }

            if (!_coordinator.ConfirmStable(file, runLease.Generation))
            {
                metrics.Retained++;
                continue;
            }

            metrics.Eligible++;
            if (request.ObservationMode)
            {
                metrics.Retained++;
                continue;
            }

            try
            {
                var databaseProbe = await _databaseReferences.ProbeReferenceAsync(
                    normalizedPath,
                    cancellationToken);
                metrics.Failures += databaseProbe.FailureCount;
                if (!databaseProbe.IsComplete || databaseProbe.IsReferenced)
                {
                    _coordinator.Forget(normalizedPath);
                    if (databaseProbe.IsReferenced)
                    {
                        metrics.Referenced++;
                    }

                    metrics.Retained++;
                    continue;
                }

                var manifestProbe = await _store.ProbeManifestReferenceAsync(
                    normalizedPath,
                    cancellationToken);
                metrics.Failures += manifestProbe.FailureCount;
                if (!manifestProbe.IsComplete || manifestProbe.IsReferenced)
                {
                    _coordinator.Forget(normalizedPath);
                    if (manifestProbe.IsReferenced)
                    {
                        metrics.Referenced++;
                    }

                    metrics.Retained++;
                    continue;
                }

                if (await _store.DeleteIfUnchangedAsync(file, cancellationToken))
                {
                    _coordinator.Forget(normalizedPath);
                    metrics.Deleted++;
                }
                else
                {
                    _coordinator.Forget(normalizedPath);
                    metrics.Retained++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _coordinator.Forget(normalizedPath);
                metrics.Failures++;
                metrics.Retained++;
                _logger.LogWarning(ex, "删除孤儿文件失败，继续巡检后续文件: {RelativePath}", normalizedPath);
            }
        }

        var result = metrics.ToResult(_timeProvider.GetElapsedTime(startedAt));
        runLease.Complete();
        return result;
    }

    private sealed class InspectionMetrics(bool observationMode)
    {
        public bool ObservationMode { get; } = observationMode;
        public int Scanned { get; set; }
        public int Retained { get; set; }
        public int Referenced { get; set; }
        public int Eligible { get; set; }
        public int Deleted { get; set; }
        public int Failures { get; set; }

        public OrphanFileInspectionResult ToResult(TimeSpan elapsed) => new(
            false,
            ObservationMode,
            Scanned,
            Retained,
            Referenced,
            Eligible,
            Deleted,
            Failures,
            elapsed);
    }
}

public sealed class OrphanDatabaseReferenceQuery(IUnitOfWork unitOfWork) : IOrphanDatabaseReferenceQuery
{
    public async Task<OrphanReferenceSnapshot> ReadReferencesAsync(CancellationToken cancellationToken)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var incompleteNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failures = 0;

        var wordFilePaths = await unitOfWork.WordFiles.Query()
            .Where(file => file.FilePath != null && file.FilePath != string.Empty)
            .Select(file => file.FilePath!)
            .ToListAsync(cancellationToken);
        AddPaths(paths, wordFilePaths);

        var matchingPayloads = await unitOfWork.MatchingFillTasks.Query()
            .Where(task => task.PayloadJson != string.Empty)
            .Select(task => task.PayloadJson)
            .ToListAsync(cancellationToken);
        foreach (var payload in matchingPayloads)
        {
            if (!TryAddJsonPath(payload, "downloadArtifactRelativePath", paths))
            {
                failures++;
                incompleteNamespaces.Add(OrphanFilePathRules.FilledFilesNamespace);
            }
        }

        var historyPayloads = await unitOfWork.ExecutionHistoryRecords.Query()
            .Where(record => record.DetailJson != string.Empty)
            .Select(record => record.DetailJson)
            .ToListAsync(cancellationToken);
        foreach (var payload in historyPayloads)
        {
            if (!TryAddJsonPath(payload, "fullArchiveRelativePath", paths))
            {
                failures++;
                incompleteNamespaces.Add(OrphanFilePathRules.ExecutionHistoryNamespace);
            }
        }

        return new OrphanReferenceSnapshot(paths, incompleteNamespaces, failures);
    }

    public async Task<OrphanReferenceProbe> ProbeReferenceAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        var normalized = OrphanFilePathRules.Normalize(relativePath);
        if (await unitOfWork.WordFiles.Query()
                .AnyAsync(file => file.FilePath == normalized || file.FilePath == normalized.Replace('/', '\\'), cancellationToken))
        {
            return new OrphanReferenceProbe(true, true);
        }

        var managedNamespace = OrphanFilePathRules.GetManagedNamespace(normalized);
        if (managedNamespace == OrphanFilePathRules.FilledFilesNamespace)
        {
            var referenced = await unitOfWork.MatchingFillTasks.Query()
                .AnyAsync(task => task.PayloadJson.Contains(normalized), cancellationToken);
            return new OrphanReferenceProbe(referenced, true);
        }

        if (managedNamespace == OrphanFilePathRules.ExecutionHistoryNamespace)
        {
            var referenced = await unitOfWork.ExecutionHistoryRecords.Query()
                .AnyAsync(record => record.DetailJson.Contains(normalized), cancellationToken);
            return new OrphanReferenceProbe(referenced, true);
        }

        return new OrphanReferenceProbe(false, true);
    }

    private static void AddPaths(HashSet<string> paths, IEnumerable<string> values)
    {
        foreach (var value in values.Where(OrphanFilePathRules.IsManagedContentPath))
        {
            paths.Add(OrphanFilePathRules.Normalize(value));
        }
    }

    private static bool TryAddJsonPath(string payload, string propertyName, HashSet<string> paths)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            foreach (var value in FindStringProperties(document.RootElement, propertyName))
            {
                if (OrphanFilePathRules.IsManagedContentPath(value))
                {
                    paths.Add(OrphanFilePathRules.Normalize(value));
                }
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IEnumerable<string> FindStringProperties(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String &&
                    property.Value.GetString() is { } value)
                {
                    yield return value;
                }

                foreach (var nested in FindStringProperties(property.Value, propertyName))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in FindStringProperties(item, propertyName))
                {
                    yield return nested;
                }
            }
        }
    }
}

public static class OrphanFilePathRules
{
    public const string WordFilesNamespace = "uploads/word-files";
    public const string ExcelFilesNamespace = "uploads/excel-files";
    public const string FilledFilesNamespace = "uploads/filled-files";
    public const string ExecutionHistoryNamespace = "uploads/execution-history/smart-fill";

    public static IReadOnlyList<string> ManagedNamespaces { get; } =
    [
        WordFilesNamespace,
        ExcelFilesNamespace,
        FilledFilesNamespace,
        ExecutionHistoryNamespace
    ];

    public static string Normalize(string path) => path.Trim().Replace('\\', '/').TrimStart('/');

    public static bool IsManagedContentPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsRootedOnAnyPlatform(path))
        {
            return false;
        }

        var normalized = Normalize(path);
        if (normalized.Split('/').Any(segment => segment is "" or "." or "..") ||
            normalized.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith($"{FilledFilesNamespace}/manifests/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return GetManagedNamespace(normalized) != null;
    }

    private static bool IsRootedOnAnyPlatform(string path)
    {
        var trimmed = path.Trim();
        return Path.IsPathRooted(trimmed) ||
               trimmed.StartsWith('/') ||
               trimmed.StartsWith('\\') ||
               (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':');
    }

    public static string? GetManagedNamespace(string path)
    {
        var normalized = Normalize(path);
        return ManagedNamespaces.FirstOrDefault(root =>
            normalized.StartsWith($"{root}/", StringComparison.OrdinalIgnoreCase));
    }
}
