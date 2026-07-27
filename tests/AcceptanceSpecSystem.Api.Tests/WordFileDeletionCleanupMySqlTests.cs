using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    private static ServiceProvider BuildProvider(
        string connectionString,
        IFileStorageService storage)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options =>
            options.UseMySql(
                connectionString,
                ServerVersion.AutoDetect(connectionString)));
        services.AddSingleton(storage);
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
}
