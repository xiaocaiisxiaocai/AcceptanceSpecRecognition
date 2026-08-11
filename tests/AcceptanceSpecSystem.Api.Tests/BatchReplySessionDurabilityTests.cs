using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class BatchReplySessionDurabilityTests
{
    [Fact]
    public async Task AddTargetFiles_WhenCancelledBeforeManifestPublish_ShouldPreservePreviousManifest()
    {
        using var directory = new TemporaryDirectory();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var storage = new TestFileStorageService(directory.Path);
        var initialService = CreateService(cache, storage, new BatchReplySessionCoordinator());
        var session = await initialService.CreateSourceSessionAsync(
            10,
            20,
            "source.docx",
            UploadedFileType.WordDocx,
            "source"u8.ToArray());
        var manifestPath = storage.GetAbsolutePath(session.ManifestRelativePath!);
        var originalManifest = await File.ReadAllBytesAsync(manifestPath);

        using var cancellation = new CancellationTokenSource();
        var cancellingCoordinator = new BatchReplySessionCoordinator(
            new CancelOnAcquireLockProvider(cancellation));
        var service = CreateService(cache, storage, cancellingCoordinator);

        var act = () => service.AddTargetFilesAsync(
            10,
            20,
            session.SessionId,
            [new BatchReplyTargetFile { TargetId = "target", FileName = "target.docx" }],
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await File.ReadAllBytesAsync(manifestPath)).Should().Equal(originalManifest);
        Directory.EnumerateFiles(Path.GetDirectoryName(manifestPath)!, "*.tmp")
            .Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteTemporaryFiles_WhenOneDeleteFails_ShouldContinueWithRemainingPaths()
    {
        using var directory = new TemporaryDirectory();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var storage = new FaultingDeleteFileStorageService(directory.Path, "first.tmp");
        var service = CreateService(cache, storage, new BatchReplySessionCoordinator());

        var act = () => service.DeleteTemporaryFilesAsync(["first.tmp", "second.tmp"]);

        await act.Should().NotThrowAsync();
        storage.DeleteAttempts.Should().Equal("first.tmp", "second.tmp");
    }

    private static BatchReplySessionService CreateService(
        IMemoryCache cache,
        IFileStorageService storage,
        BatchReplySessionCoordinator coordinator) => new(
        cache,
        storage,
        NullLogger<BatchReplySessionService>.Instance,
        BatchReplyRetentionPolicy.Default,
        coordinator,
        TimeProvider.System);

    private sealed class CancelOnAcquireLockProvider(CancellationTokenSource cancellation)
        : IBatchReplyDistributedLockProvider
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(
            string key,
            TimeSpan waitTimeout,
            CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromResult<IAsyncDisposable?>(new Lease());
        }

        private sealed class Lease : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FaultingDeleteFileStorageService : IFileStorageService
    {
        private readonly TestFileStorageService _inner;
        private readonly string _failurePath;

        public FaultingDeleteFileStorageService(string root, string failurePath)
        {
            _inner = new TestFileStorageService(root);
            _failurePath = failurePath;
        }

        public List<string> DeleteAttempts { get; } = [];

        public Task<string> SaveUploadedWordAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default) =>
            _inner.SaveUploadedWordAsync(originalFileName, content, cancellationToken);
        public Task<string> SaveUploadedWordAsync(string originalFileName, Stream content, CancellationToken cancellationToken = default) =>
            _inner.SaveUploadedWordAsync(originalFileName, content, cancellationToken);
        public Task<string> SaveUploadedExcelAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default) =>
            _inner.SaveUploadedExcelAsync(originalFileName, content, cancellationToken);
        public Task<string> SaveUploadedExcelAsync(string originalFileName, Stream content, CancellationToken cancellationToken = default) =>
            _inner.SaveUploadedExcelAsync(originalFileName, content, cancellationToken);
        public Task<string> SaveFilledWordAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default) =>
            _inner.SaveFilledWordAsync(originalFileName, content, cancellationToken);
        public Task<string> SaveSmartFillPlaybackArchiveAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default) =>
            _inner.SaveSmartFillPlaybackArchiveAsync(originalFileName, content, cancellationToken);
        public Task<string> SaveSmartFillResultArchiveAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default) =>
            _inner.SaveSmartFillResultArchiveAsync(originalFileName, content, cancellationToken);
        public Stream OpenReadStream(string relativePath) => _inner.OpenReadStream(relativePath);
        public Task<string> WriteHealthCheckFileAsync(CancellationToken cancellationToken = default) =>
            _inner.WriteHealthCheckFileAsync(cancellationToken);
        public string GetAbsolutePath(string relativePath) => _inner.GetAbsolutePath(relativePath);

        public Task DeleteIfExistsAsync(string? relativePath, CancellationToken cancellationToken = default)
        {
            DeleteAttempts.Add(relativePath ?? string.Empty);
            return string.Equals(relativePath, _failurePath, StringComparison.Ordinal)
                ? Task.FromException(new IOException("injected delete failure"))
                : _inner.DeleteIfExistsAsync(relativePath, cancellationToken);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "AcceptanceSpecSystem.BatchReplyDurabilityTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
