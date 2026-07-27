using System.Security.Cryptography;
using System.ComponentModel;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using AcceptanceSpecSystem.Application;
using AcceptanceSpecSystem.Application.Services;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

public sealed class FileCompareTemporaryStorageOptions
{
    public const string SectionName = "FileCompareTemporaryStorage";
    public int RetentionHours { get; set; } = 24;
    public int CleanupIntervalMinutes { get; set; } = 60;
    public int HeartbeatSeconds { get; set; } = 60;
}

internal interface IFileCompareTemporaryStorageFaultHook
{
    void AfterRequestDirectoryCreated() { }
    void BeforeMarkerFlush() { }
    void AfterMarkerCreated() { }
    void AfterCleanupDirectoryOpened(string requestId) { }
    void BeforeCleanupDirectory() { }
    void BeforeRequestDirectoryRename(string requestId) { }
    void AfterRequestDirectoryQuarantined(string requestId) { }
    void BeforeEntryDisposition(string entryName) { }
    void AfterTrackedStreamDisposed() { }
}

public sealed class FileCompareTemporaryCleanupException(int failureCount)
    : IOException($"文件比较临时资源清理有 {failureCount} 项失败")
{
    public int FailureCount { get; } = failureCount;
}

public sealed class FileCompareTemporaryStorage : IFileCompareTemporaryStorage, IDisposable
{
    private readonly TimeProvider _timeProvider;
    private readonly FileCompareTemporaryStorageOptions _options;
    private readonly IFileCompareTemporaryStorageFaultHook? _faultHook;
    private readonly INativeTemporaryFileSystem _fileSystem;
    private readonly object _lifecycleGate = new();
    private readonly ConcurrentDictionary<FileSystemTemporaryFileLease, byte> _activeLeases = new();
    private int _disposed;

    public FileCompareTemporaryStorage(
        IWebHostEnvironment environment,
        IConfiguration configuration,
        IOptions<FileCompareTemporaryStorageOptions> options,
        TimeProvider timeProvider)
        : this(environment, configuration, options, timeProvider, null)
    {
    }

    internal FileCompareTemporaryStorage(
        IWebHostEnvironment environment,
        IConfiguration configuration,
        IOptions<FileCompareTemporaryStorageOptions> options,
        TimeProvider timeProvider,
        IFileCompareTemporaryStorageFaultHook? faultHook = null)
    {
        var configuredRoot = configuration["FileCompareTemporaryStorage:Root"];
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(Path.GetTempPath(), "AcceptanceSpecSystem", "file-compare")
            : Path.IsPathRooted(configuredRoot)
                ? Path.GetFullPath(configuredRoot)
                : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredRoot));
        _timeProvider = timeProvider;
        _options = options.Value;
        _faultHook = faultHook;
        _fileSystem = OperatingSystem.IsWindows()
            ? new WindowsNativeTemporaryFileSystem(root)
            : throw new PlatformNotSupportedException(
                "文件比较原生临时存储仅支持 Windows");
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
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            var cutoff = _timeProvider.GetUtcNow().AddHours(-_options.RetentionHours);
            var failureCount = _fileSystem.CleanupExpired(cutoff, _faultHook, cancellationToken);
            if (failureCount > 0)
                throw new FileCompareTemporaryCleanupException(failureCount);
        }
        return Task.CompletedTask;
    }

    private FileSystemTemporaryFileLease CreateLease()
    {
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            var requestId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var markerValue =
                $"{requestId}:{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}";
            var nativeDirectory = _fileSystem.CreateRequestDirectory(
                requestId,
                markerValue,
                _timeProvider.GetUtcNow(),
                _faultHook);
            Action? afterStreamDisposed = _faultHook is null
                ? null
                : () => _faultHook.AfterTrackedStreamDisposed();
            FileSystemTemporaryFileLease? lease = null;
            lease = new FileSystemTemporaryFileLease(
                nativeDirectory,
                TimeSpan.FromSeconds(_options.HeartbeatSeconds),
                _timeProvider,
                afterStreamDisposed,
                () =>
                {
                    if (lease is not null)
                        _activeLeases.TryRemove(lease, out _);
                });
            _activeLeases.TryAdd(lease, 0);
            return lease;
        }
    }

    public void Dispose()
    {
        FileSystemTemporaryFileLease[] leases;
        lock (_lifecycleGate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            leases = _activeLeases.Keys.ToArray();
        }
        Exception? failure = null;
        try
        {
            foreach (var lease in leases)
            {
                try
                {
                    lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                }
            }
        }
        finally
        {
            try
            {
                _fileSystem.Dispose();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class FileSystemTemporaryFileLease : TemporaryFileLease
    {
        private readonly NativeTemporaryDirectory _directory;
        private readonly TimeSpan _heartbeatInterval;
        private readonly TimeProvider _timeProvider;
        private readonly Action? _afterStreamDisposed;
        private readonly Action _release;
        private readonly ConcurrentDictionary<LeaseTrackedStream, byte> _openStreams = new();
        private readonly object _stateGate = new();
        private readonly CancellationTokenSource _heartbeatCancellation = new();
        private readonly Task _heartbeatTask;
        private Task? _disposeTask;
        private int _disposed;
        private long _length;
        private string _sha256 = string.Empty;

        public FileSystemTemporaryFileLease(
            NativeTemporaryDirectory directory,
            TimeSpan heartbeatInterval,
            TimeProvider timeProvider,
            Action? afterStreamDisposed,
            Action release)
        {
            _directory = directory;
            _heartbeatInterval = heartbeatInterval;
            _timeProvider = timeProvider;
            _afterStreamDisposed = afterStreamDisposed;
            _release = release;
            _heartbeatTask = RunHeartbeatAsync();
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
            lock (_stateGate)
            {
                ThrowIfDisposed();
                _directory.Heartbeat(_timeProvider.GetUtcNow());
                return Track(_directory.OpenPayloadReadStream());
            }
        }

        public override Stream OpenWrite()
        {
            lock (_stateGate)
            {
                ThrowIfDisposed();
                _directory.Heartbeat(_timeProvider.GetUtcNow());
                return Track(_directory.CreatePayloadWriteStream());
            }
        }

        public override ValueTask DisposeAsync()
        {
            lock (_stateGate)
                return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }

        private async Task DisposeCoreAsync()
        {
            Exception? failure = null;
            Volatile.Write(ref _disposed, 1);
            _heartbeatCancellation.Cancel();
            try
            {
                try
                {
                    await _heartbeatTask;
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or
                    ApplicationServiceException or Win32Exception)
                {
                }
            }
            finally
            {
                try
                {
                    foreach (var stream in _openStreams.Keys)
                    {
                        try
                        {
                            await stream.DisposeAsync();
                        }
                        catch (Exception exception) when (
                            exception is IOException or UnauthorizedAccessException or
                            Win32Exception or ObjectDisposedException)
                        {
                        }
                        catch (Exception exception)
                        {
                            failure ??= exception;
                        }
                    }
                }
                finally
                {
                    try
                    {
                        await _directory.DisposeAsync();
                    }
                    finally
                    {
                        _heartbeatCancellation.Dispose();
                        _release();
                    }
                }
            }
            if (failure is not null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private void ThrowIfDisposed() =>
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0,
                nameof(TemporaryFileLease));

        private Stream Track(Stream inner)
        {
            LeaseTrackedStream? tracked = null;
            tracked = new LeaseTrackedStream(
                inner,
                _afterStreamDisposed,
                () =>
                {
                    if (tracked is not null)
                        _openStreams.TryRemove(tracked, out _);
                });
            _openStreams.TryAdd(tracked, 0);
            return tracked;
        }

        private async Task RunHeartbeatAsync()
        {
            while (true)
            {
                await Task.Delay(
                    _heartbeatInterval,
                    _timeProvider,
                    _heartbeatCancellation.Token);
                _directory.Heartbeat(_timeProvider.GetUtcNow());
            }
        }
    }

    private sealed class LeaseTrackedStream(
        Stream inner,
        Action? afterDisposed,
        Action release) : Stream
    {
        private readonly object _disposeGate = new();
        private Task? _disposeTask;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) =>
            inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);
        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                GetOrCreateDisposeTask().GetAwaiter().GetResult();
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() =>
            new(GetOrCreateDisposeTask());

        private Task GetOrCreateDisposeTask()
        {
            lock (_disposeGate)
                return _disposeTask ??= DisposeCoreAsync();
        }

        private async Task DisposeCoreAsync()
        {
            try
            {
                await inner.DisposeAsync();
            }
            finally
            {
                release();
            }
            afterDisposed?.Invoke();
            GC.SuppressFinalize(this);
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
