using System.Security.Cryptography;
using System.Collections.Concurrent;
using AcceptanceSpecSystem.Application;
using AcceptanceSpecSystem.Application.Services;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

public sealed class FileCompareTemporaryStorageOptions
{
    public const string SectionName = "FileCompareTemporaryStorage";
    public int RetentionHours { get; set; } = 24;
    public int CleanupIntervalMinutes { get; set; } = 60;
}

public sealed class FileCompareTemporaryStorage : IFileCompareTemporaryStorage
{
    private const string MarkerName = ".acceptance-file-compare";
    private readonly string _root;
    private readonly TimeProvider _timeProvider;
    private readonly FileCompareTemporaryStorageOptions _options;
    private readonly ConcurrentDictionary<string, byte> _activeDirectories =
        new(StringComparer.OrdinalIgnoreCase);

    public FileCompareTemporaryStorage(
        IWebHostEnvironment environment,
        IConfiguration configuration,
        IOptions<FileCompareTemporaryStorageOptions> options,
        TimeProvider timeProvider)
    {
        var configuredRoot = configuration["FileCompareTemporaryStorage:Root"];
        _root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(Path.GetTempPath(), "AcceptanceSpecSystem", "file-compare")
            : Path.IsPathRooted(configuredRoot)
                ? Path.GetFullPath(configuredRoot)
                : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredRoot));
        Directory.CreateDirectory(_root);
        RejectReparsePath(_root);
        _timeProvider = timeProvider;
        _options = options.Value;
    }

    public async Task<TemporaryFileLease> StageUploadAsync(
        Stream content,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var lease = CreateLease();
        try
        {
            await using var output = lease.OpenWrite();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                    break;
                total = checked(total + read);
                if (total > maxBytes)
                    throw new ApplicationServiceException(413, "单个比较文件大小不能超过50MB");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            if (total == 0)
                throw new ApplicationServiceException(400, "请选择要上传的文件");
            await output.FlushAsync(cancellationToken);
            lease.Seal(total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
            return lease;
        }
        catch
        {
            await lease.DisposeAsync();
            throw;
        }
    }

    public Task<TemporaryFileLease> CreateOutputAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<TemporaryFileLease>(CreateLease());
    }

    public Task CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        RejectReparsePath(_root);
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime.AddHours(-_options.RetentionHours);
        foreach (var directory in Directory.EnumerateDirectories(_root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_activeDirectories.ContainsKey(directory) ||
                !TryValidateManagedDirectory(directory) ||
                Directory.GetLastWriteTimeUtc(directory) > cutoff)
                continue;
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return Task.CompletedTask;
    }

    private FileSystemTemporaryFileLease CreateLease()
    {
        RejectReparsePath(_root);
        var directoryName = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var directory = Path.Combine(_root, directoryName);
        Directory.CreateDirectory(directory);
        var markerValue = $"{directoryName}:{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}";
        File.WriteAllText(Path.Combine(directory, MarkerName), markerValue);
        _activeDirectories.TryAdd(directory, 0);
        return new FileSystemTemporaryFileLease(
            _root,
            directory,
            Path.Combine(directory, "payload.tmp"),
            markerValue,
            () => _activeDirectories.TryRemove(directory, out _));
    }

    private bool TryValidateManagedDirectory(string directory)
    {
        var name = Path.GetFileName(directory);
        if (name.Length != 32 || !name.All(Uri.IsHexDigit))
            return false;
        var expected = Path.Combine(_root, name);
        if (!string.Equals(Path.GetFullPath(directory), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            RejectReparsePoint(directory);
            var marker = Path.Combine(directory, MarkerName);
            if (!File.Exists(marker) ||
                (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0 ||
                new FileInfo(marker).Length > 65)
                return false;
            var markerValue = File.ReadAllText(marker);
            var parts = markerValue.Split(':');
            if (parts.Length != 2 ||
                !string.Equals(parts[0], name, StringComparison.Ordinal) ||
                parts[1].Length != 32 ||
                !parts[1].All(Uri.IsHexDigit))
                return false;
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var entryName = Path.GetFileName(entry);
                if (entryName is not MarkerName and not "payload.tmp" ||
                    (File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                    return false;
            }
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new ApplicationServiceException(400, "文件比较临时目录不安全");
    }

    private static void RejectReparsePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new ApplicationServiceException(400, "文件比较临时目录不安全");
        var current = root;
        foreach (var segment in Path.GetRelativePath(root, fullPath).Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current) || File.Exists(current))
                RejectReparsePoint(current);
        }
    }

    private sealed class FileSystemTemporaryFileLease : TemporaryFileLease
    {
        private readonly string _root;
        private readonly string _directory;
        private readonly string _path;
        private readonly string _markerValue;
        private readonly Action _release;
        private int _disposed;
        private long _length;
        private string _sha256 = string.Empty;

        public FileSystemTemporaryFileLease(
            string root,
            string directory,
            string path,
            string markerValue,
            Action release)
        {
            _root = root;
            _directory = directory;
            _path = path;
            _markerValue = markerValue;
            _release = release;
        }

        public override long Length => _length;
        public override string Sha256 => _sha256;

        public void Seal(long length, string sha256)
        {
            _length = length;
            _sha256 = sha256;
        }

        public override Stream OpenRead()
        {
            EnsureSafe();
            return new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }

        public override Stream OpenWrite()
        {
            EnsureSafe();
            return new FileStream(_path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }

        public override ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return ValueTask.CompletedTask;
            try
            {
                var expected = Path.Combine(_root, Path.GetFileName(_directory));
                if (string.Equals(Path.GetFullPath(_directory), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase))
                {
                    RejectReparsePath(_root);
                    RejectReparsePoint(_directory);
                    var marker = Path.Combine(_directory, MarkerName);
                    if (File.Exists(marker) &&
                        (File.GetAttributes(marker) & FileAttributes.ReparsePoint) == 0 &&
                        string.Equals(File.ReadAllText(marker), _markerValue, StringComparison.Ordinal) &&
                        HasOnlySafeOwnedEntries())
                        Directory.Delete(_directory, recursive: true);
                }
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (FileNotFoundException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            finally
            {
                _release();
            }
            return ValueTask.CompletedTask;
        }

        private void EnsureSafe()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(TemporaryFileLease));
            var expected = Path.Combine(_root, Path.GetFileName(_directory));
            if (!string.Equals(Path.GetFullPath(_directory), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase))
                throw new ApplicationServiceException(400, "文件比较临时资源不安全");
            RejectReparsePath(_root);
            RejectReparsePoint(_directory);
            var marker = Path.Combine(_directory, MarkerName);
            if (!File.Exists(marker) ||
                (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0 ||
                !string.Equals(File.ReadAllText(marker), _markerValue, StringComparison.Ordinal))
                throw new ApplicationServiceException(400, "文件比较临时资源不安全");
            if (File.Exists(_path) && (File.GetAttributes(_path) & FileAttributes.ReparsePoint) != 0)
                throw new ApplicationServiceException(400, "文件比较临时资源不安全");
        }

        private bool HasOnlySafeOwnedEntries()
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(_directory))
            {
                var name = Path.GetFileName(entry);
                if (name is not MarkerName and not "payload.tmp" ||
                    (File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                    return false;
            }
            return true;
        }
    }
}

public sealed class FileCompareTemporaryCleanupHostedService(
    IFileCompareTemporaryStorage storage,
    IOptions<FileCompareTemporaryStorageOptions> options,
    ILogger<FileCompareTemporaryCleanupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.CleanupIntervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await storage.CleanupExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "文件比较临时资源清理失败，错误类别：{ErrorCategory}",
                    exception.GetType().Name);
            }
            if (!await timer.WaitForNextTickAsync(stoppingToken))
                break;
        }
    }
}
