using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class WordFileDeletionCleanupMySqlTests
{
    [MySqlSmokeFact]
    public async Task 真实MySql清理器_IO失败后应释放租约并在下次幂等收敛()
    {
        await using var database = await MySqlEmbeddingCacheTestDatabase.CreateAsync();
        await database.MigrateAsync();
        var storageRoot = Path.Combine(
            Path.GetTempPath(),
            "AcceptanceSpecSystem-MySqlCleanup",
            Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(storageRoot);
            var storage = new FailOnceDeletionStorage(storageRoot);
            await using var provider = BuildProvider(database.ConnectionString, storage);
            var relativePath =
                $"uploads/word-files/{DateTime.UtcNow:yyyy-MM-dd}/{Guid.NewGuid():N}.docx";
            var fullPath = storage.GetAbsolutePath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, [1, 2, 3]);

            int fileId;
            using (var seedScope = provider.CreateScope())
            {
                var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var file = new WordFile
                {
                    FileName = "cleanup-retry.docx",
                    FileHash = Guid.NewGuid().ToString("N"),
                    FilePath = relativePath,
                    FileContent = [],
                    FileType = UploadedFileType.WordDocx,
                    DeletionStatus = WordFileDeletionStatus.PendingDeletion,
                    DeletionRequestedAt = DateTime.UtcNow,
                    NextDeletionAttemptAt = DateTime.UtcNow.AddSeconds(-1)
                };
                db.WordFiles.Add(file);
                await db.SaveChangesAsync();
                fileId = file.Id;
            }

            var cleanup =
                provider.GetRequiredService<IWordFileDeletionCleanupAppService>();
            var firstStartedAt = DateTime.UtcNow;
            (await cleanup.RunBatchAsync(10, CancellationToken.None)).Should().Be(0);

            using (var failedScope = provider.CreateScope())
            {
                var db =
                    failedScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var failed = await db.WordFiles
                    .IgnoreQueryFilters()
                    .SingleAsync(item => item.Id == fileId);
                failed.DeletionRetryCount.Should().Be(1);
                failed.LastDeletionError.Should().Be("IoError");
                failed.NextDeletionAttemptAt.Should()
                    .BeOnOrAfter(firstStartedAt.AddMinutes(1));
                failed.DeletionLeaseToken.Should().BeNull();
                failed.DeletionLeaseExpiresAt.Should().BeNull();
                File.Exists(fullPath).Should().BeTrue();

                failed.NextDeletionAttemptAt = DateTime.UtcNow.AddSeconds(-1);
                await db.SaveChangesAsync();
            }

            (await cleanup.RunBatchAsync(10, CancellationToken.None)).Should().Be(1);
            using var completedScope = provider.CreateScope();
            var completedDb =
                completedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await completedDb.WordFiles
                    .IgnoreQueryFilters()
                    .AnyAsync(item => item.Id == fileId))
                .Should().BeFalse();
            File.Exists(fullPath).Should().BeFalse();
            storage.AttemptCount.Should().Be(2);

            (await cleanup.RunBatchAsync(10, CancellationToken.None)).Should().Be(0);
            storage.AttemptCount.Should().Be(2);
        }
        finally
        {
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }

    [MySqlSmokeFact]
    public async Task 真实MySql清理器_记录失败本身失败时应同时保留原始和次生异常()
    {
        await using var database = await MySqlEmbeddingCacheTestDatabase.CreateAsync();
        await database.MigrateAsync();
        var storageRoot = Path.Combine(
            Path.GetTempPath(),
            "AcceptanceSpecSystem-MySqlCleanup",
            Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(storageRoot);
            var failureInterceptor = new ActivatedConnectionFailureInterceptor(
                new RecordFailureProbeException());
            var storage = new ActivateThenFailDeletionStorage(
                storageRoot,
                failureInterceptor);
            await using var provider = BuildProvider(
                database.ConnectionString,
                storage,
                failureInterceptor);
            using (var seedScope = provider.CreateScope())
            {
                var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.WordFiles.Add(new WordFile
                {
                    FileName = "cleanup-secondary-failure.docx",
                    FileHash = Guid.NewGuid().ToString("N"),
                    FilePath = $"uploads/word-files/{Guid.NewGuid():N}.docx",
                    FileContent = [],
                    FileType = UploadedFileType.WordDocx,
                    DeletionStatus = WordFileDeletionStatus.PendingDeletion,
                    DeletionRequestedAt = DateTime.UtcNow,
                    NextDeletionAttemptAt = DateTime.UtcNow.AddSeconds(-1)
                });
                await db.SaveChangesAsync();
            }

            var cleanup =
                provider.GetRequiredService<IWordFileDeletionCleanupAppService>();
            var action = () => cleanup.RunBatchAsync(10, CancellationToken.None);

            var aggregate = await action.Should().ThrowAsync<AggregateException>();
            aggregate.Which.InnerExceptions.Should()
                .Contain(exception => exception is IOException)
                .And.Contain(exception => exception is RecordFailureProbeException);
        }
        finally
        {
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }

    [MySqlSmokeFact]
    public async Task 真实MySql清理器_取消后释放租约失败仍应保留原始取消并记录次生异常()
    {
        await using var database = await MySqlEmbeddingCacheTestDatabase.CreateAsync();
        await database.MigrateAsync();
        var storageRoot = Path.Combine(
            Path.GetTempPath(),
            "AcceptanceSpecSystem-MySqlCleanup",
            Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(storageRoot);
            var failureInterceptor = new ActivatedConnectionFailureInterceptor(
                new ReleaseLeaseProbeException());
            var storage = new ActivateThenCancelDeletionStorage(
                storageRoot,
                failureInterceptor);
            var logger = new CollectingLogger<WordFileDeletionCleanupAppService>();
            await using var provider = BuildProvider(
                database.ConnectionString,
                storage,
                failureInterceptor,
                logger);
            using (var seedScope = provider.CreateScope())
            {
                var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.WordFiles.Add(new WordFile
                {
                    FileName = "cleanup-cancel-release-failure.docx",
                    FileHash = Guid.NewGuid().ToString("N"),
                    FilePath = $"uploads/word-files/{Guid.NewGuid():N}.docx",
                    FileContent = [],
                    FileType = UploadedFileType.WordDocx,
                    DeletionStatus = WordFileDeletionStatus.PendingDeletion,
                    DeletionRequestedAt = DateTime.UtcNow,
                    NextDeletionAttemptAt = DateTime.UtcNow.AddSeconds(-1)
                });
                await db.SaveChangesAsync();
            }

            var cleanup =
                provider.GetRequiredService<IWordFileDeletionCleanupAppService>();
            var action = () => cleanup.RunBatchAsync(10, CancellationToken.None);

            await action.Should().ThrowAsync<OperationCanceledException>();
            logger.Entries.Should().ContainSingle(entry =>
                entry.Level == LogLevel.Error &&
                entry.Exception is ReleaseLeaseProbeException);

            failureInterceptor.Deactivate();
            using var verifyScope = provider.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var retained = await verifyDb.WordFiles
                .IgnoreQueryFilters()
                .SingleAsync();
            retained.DeletionLeaseToken.Should().NotBeNullOrWhiteSpace();
            retained.DeletionLeaseExpiresAt.Should().BeAfter(DateTime.UtcNow);
        }
        finally
        {
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }

    private static ServiceProvider BuildProvider(
        string connectionString,
        IFileStorageService storage,
        IInterceptor? interceptor = null,
        ILogger<WordFileDeletionCleanupAppService>? logger = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseMySql(
                connectionString,
                ServerVersion.AutoDetect(connectionString));
            if (interceptor != null)
            {
                options.AddInterceptors(interceptor);
            }
        });
        services.AddSingleton(storage);
        if (logger != null)
        {
            services.AddSingleton(logger);
        }
        services.AddScoped<WordFileDeletionLeaseStore>();
        services.AddSingleton<
            IWordFileDeletionCleanupAppService,
            WordFileDeletionCleanupAppService>();
        return services.BuildServiceProvider();
    }

    private sealed class FailOnceDeletionStorage(string root)
        : TestFileStorageService(root)
    {
        private int _attemptCount;

        public int AttemptCount => Volatile.Read(ref _attemptCount);

        public override Task DeleteUploadedWordFileIfExistsAsync(
            string? relativePath,
            UploadedFileType fileType,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _attemptCount) == 1)
            {
                return Task.FromException(new IOException("模拟一次 IO 失败"));
            }

            return base.DeleteUploadedWordFileIfExistsAsync(
                relativePath,
                fileType,
                cancellationToken);
        }
    }

    private sealed class ActivateThenFailDeletionStorage(
        string root,
        ActivatedConnectionFailureInterceptor failureInterceptor)
        : TestFileStorageService(root)
    {
        public override Task DeleteUploadedWordFileIfExistsAsync(
            string? relativePath,
            UploadedFileType fileType,
            CancellationToken cancellationToken = default)
        {
            failureInterceptor.Activate();
            return Task.FromException(new IOException("原始文件删除失败"));
        }
    }

    private sealed class ActivateThenCancelDeletionStorage(
        string root,
        ActivatedConnectionFailureInterceptor failureInterceptor)
        : TestFileStorageService(root)
    {
        public override Task DeleteUploadedWordFileIfExistsAsync(
            string? relativePath,
            UploadedFileType fileType,
            CancellationToken cancellationToken = default)
        {
            failureInterceptor.Activate();
            return Task.FromException(new OperationCanceledException("原始取消"));
        }
    }

    private sealed class ActivatedConnectionFailureInterceptor(Exception exception)
        : DbConnectionInterceptor
    {
        private int _active;

        public void Activate() => Interlocked.Exchange(ref _active, 1);

        public void Deactivate() => Interlocked.Exchange(ref _active, 0);

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            System.Data.Common.DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _active) == 1)
            {
                throw exception;
            }

            return base.ConnectionOpeningAsync(
                connection,
                eventData,
                result,
                cancellationToken);
        }
    }

    private sealed class RecordFailureProbeException : Exception;

    private sealed class ReleaseLeaseProbeException : Exception;

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, exception, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, Exception? Exception, string Message);
}
