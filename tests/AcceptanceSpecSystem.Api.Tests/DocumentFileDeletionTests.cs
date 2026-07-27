using System.Net;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace AcceptanceSpecSystem.Api.Tests;

public class DocumentFileDeletionTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public DocumentFileDeletionTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(5, 16)]
    public void 删除失败退避_应按分钟指数增长且最多二十四小时(int retryCount, int expectedMinutes)
    {
        var delay = WordFileDeletionCleanupAppService.CalculateRetryDelay(retryCount);

        delay.Should().Be(TimeSpan.FromMinutes(expectedMinutes));
    }

    [Fact]
    public void 删除失败退避_超过上限应限制为二十四小时()
    {
        WordFileDeletionCleanupAppService.CalculateRetryDelay(100)
            .Should().Be(TimeSpan.FromHours(24));
    }

    [Theory]
    [MemberData(nameof(FailureCategories))]
    public void 删除失败_只应保存稳定分类而非原始错误(Exception exception, string expectedCategory)
    {
        WordFileDeletionCleanupAppService.ClassifyFailure(exception).Should().Be(expectedCategory);
    }

    public static TheoryData<Exception, string> FailureCategories => new()
    {
        { new IOException(@"C:\secret\private.docx"), "IoError" },
        { new UnauthorizedAccessException(@"C:\secret\private.docx"), "AccessDenied" },
        { new UnsafeWordFilePathException(), "UnsafePath" },
        { new InvalidOperationException(@"C:\secret\private.docx"), "Unexpected" }
    };

    [Theory]
    [InlineData("uploads/word-files/2026-07-27/0123456789abcdef0123456789abcdef.docx", UploadedFileType.WordDocx)]
    [InlineData("uploads/excel-files/2026-07-27/0123456789abcdef0123456789abcdef.xlsx", UploadedFileType.ExcelXlsx)]
    public void 持久文件路径_只允许受控上传命名空间(string path, UploadedFileType fileType)
    {
        WordFileStoragePathPolicy.IsAllowed(path, fileType).Should().BeTrue();
    }

    [Theory]
    [InlineData("../secret.docx")]
    [InlineData("uploads/filled-files/2026-07-27/0123456789abcdef0123456789abcdef.docx")]
    [InlineData("uploads/word-files/not-a-date/0123456789abcdef0123456789abcdef.docx")]
    [InlineData("uploads/word-files/2026-07-27/not-a-guid.docx")]
    public void 持久文件路径_应拒绝越界或非标准路径(string path)
    {
        WordFileStoragePathPolicy.IsAllowed(path, UploadedFileType.WordDocx).Should().BeFalse();
    }

    [ReparsePointFact]
    public async Task 持久文件删除_遇到符号链接路径组件应拒绝且不删除链接外文件()
    {
        var root = Path.Combine(Path.GetTempPath(), "AcceptanceSpecSystem-Reparse", Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "AcceptanceSpecSystem-Reparse-Outside", Guid.NewGuid().ToString("N"));
        var link = Path.Combine(root, "uploads", "word-files", "2026-07-27");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        Directory.CreateDirectory(outside);
        var fileName = $"{Guid.NewGuid():N}.docx";
        var outsideFile = Path.Combine(outside, fileName);
        await File.WriteAllBytesAsync(outsideFile, [1, 2, 3]);

        try
        {
            Directory.CreateSymbolicLink(link, outside);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["FileStorage:BasePath"] = root })
                .Build();
            var storage = new FileStorageService(new DeletionTestWebHostEnvironment(root), configuration);

            await storage.Invoking(service => service.DeleteUploadedWordFileIfExistsAsync(
                    $"uploads/word-files/2026-07-27/{fileName}",
                    UploadedFileType.WordDocx))
                .Should().ThrowAsync<UnsafeWordFilePathException>();
            File.Exists(outsideFile).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
            if (Directory.Exists(outside))
                Directory.Delete(outside, recursive: true);
        }
    }

    [ReparsePointFact]
    public void Windows持久文件删除_word日期目录指向base内filled目录也必须拒绝()
    {
        var root = Path.Combine(Path.GetTempPath(), "AcceptanceSpecSystem-InternalJunction", Guid.NewGuid().ToString("N"));
        var targetDirectory = Path.Combine(root, "uploads", "filled-files", "2026-07-27");
        var linkDirectory = Path.Combine(root, "uploads", "word-files", "2026-07-27");
        var fileName = $"{Guid.NewGuid():N}.docx";
        Directory.CreateDirectory(targetDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(linkDirectory)!);
        Directory.CreateSymbolicLink(linkDirectory, targetDirectory);
        var targetFile = Path.Combine(targetDirectory, fileName);
        File.WriteAllBytes(targetFile, [1, 2, 3]);

        try
        {
            var action = () => new SafeUploadedFileDeleter(root).DeleteIfExists(
                $"uploads/word-files/2026-07-27/{fileName}");

            action.Should().Throw<UnsafeWordFilePathException>();
            File.Exists(targetFile).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(linkDirectory))
                Directory.Delete(linkDirectory);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [ReparsePointFact]
    public void Windows持久文件删除_最终GUID链接指向base内其他文件也必须拒绝()
    {
        var root = Path.Combine(Path.GetTempPath(), "AcceptanceSpecSystem-InternalFileLink", Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "uploads", "word-files", "2026-07-27");
        Directory.CreateDirectory(directory);
        var targetFile = Path.Combine(directory, $"{Guid.NewGuid():N}.docx");
        var linkFile = Path.Combine(directory, $"{Guid.NewGuid():N}.docx");
        File.WriteAllBytes(targetFile, [1, 2, 3]);
        File.CreateSymbolicLink(linkFile, targetFile);

        try
        {
            var relativePath = Path.GetRelativePath(root, linkFile).Replace('\\', '/');
            var action = () => new SafeUploadedFileDeleter(root).DeleteIfExists(relativePath);

            action.Should().Throw<UnsafeWordFilePathException>();
            File.Exists(targetFile).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(linkFile))
                File.Delete(linkFile);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [WindowsOnlyFact]
    public void Windows持久文件删除_路径在打开后被替换不得删除替换对象()
    {
        var root = Path.Combine(Path.GetTempPath(), "AcceptanceSpecSystem-HandleRace", Guid.NewGuid().ToString("N"));
        var relativePath = $"uploads/word-files/2026-07-27/{Guid.NewGuid():N}.docx";
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, [1, 2, 3]);
        var movedOriginal = $"{fullPath}.opened";
        var hook = new ReplaceOpenedTargetHook(fullPath, movedOriginal);

        try
        {
            new SafeUploadedFileDeleter(root, hook).DeleteIfExists(relativePath);

            File.Exists(fullPath).Should().BeTrue("打开后放回路径的新对象不能被旧句柄删除");
            File.ReadAllBytes(fullPath).Should().Equal(9, 8, 7);
            File.Exists(movedOriginal).Should().BeFalse("被打开并验证的原对象应通过同一句柄删除");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [LinuxOnlyFact]
    public void Linux持久文件删除_目录句柄固定后替换为外部链接不得删除外部对象()
    {
        var root = Path.Combine(Path.GetTempPath(), "AcceptanceSpecSystem-DirFdRace", Guid.NewGuid().ToString("N"));
        var outsideRoot = $"{root}-outside";
        var relativePath = $"uploads/word-files/2026-07-27/{Guid.NewGuid():N}.docx";
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var outsideFile = Path.Combine(outsideRoot, "outside.docx");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        Directory.CreateDirectory(outsideRoot);
        File.WriteAllBytes(fullPath, [1, 2, 3]);
        File.WriteAllBytes(outsideFile, [9, 8, 7]);
        var movedOriginal = $"{fullPath}.opened";
        var hook = new ReplaceWithExternalLinkHook(fullPath, movedOriginal, outsideFile);

        try
        {
            var action = () => new SafeUploadedFileDeleter(root, hook).DeleteIfExists(relativePath);

            action.Should().Throw<UnsafeWordFilePathException>();
            File.Exists(outsideFile).Should().BeTrue("替换链接指向的外部对象绝不能被删除");
            File.ReadAllBytes(outsideFile).Should().Equal(9, 8, 7);
            File.Exists(fullPath).Should().BeTrue("未通过同对象校验的目录项应恢复到原名称");
            File.Exists(movedOriginal).Should().BeTrue("竞态下原对象已被重命名，安全优先保留等待孤儿巡检");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
            if (Directory.Exists(outsideRoot))
                Directory.Delete(outsideRoot, recursive: true);
        }
    }

    [LinuxOnlyFact]
    public void Linux持久文件删除_打开后替换为普通文件不得删除替换对象()
    {
        var root = Path.Combine(Path.GetTempPath(), "AcceptanceSpecSystem-InodeRace", Guid.NewGuid().ToString("N"));
        var relativePath = $"uploads/word-files/2026-07-27/{Guid.NewGuid():N}.docx";
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, [1, 2, 3]);
        var movedOriginal = $"{fullPath}.opened";
        var hook = new ReplaceOpenedTargetHook(fullPath, movedOriginal);

        try
        {
            var action = () => new SafeUploadedFileDeleter(root, hook).DeleteIfExists(relativePath);

            action.Should().Throw<UnsafeWordFilePathException>();
            File.Exists(fullPath).Should().BeTrue("替换后的普通文件未通过 inode 校验，必须恢复并保留");
            File.ReadAllBytes(fullPath).Should().Equal(9, 8, 7);
            File.Exists(movedOriginal).Should().BeTrue("原对象已被竞态方改名，不应误删");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [LinuxOnlyFact]
    public void Linux持久文件删除_恢复原名被占用应报告失败并保留可巡检隔离项()
    {
        var root = Path.Combine(Path.GetTempPath(), "AcceptanceSpecSystem-RestoreRace", Guid.NewGuid().ToString("N"));
        var relativePath = $"uploads/word-files/2026-07-27/{Guid.NewGuid():N}.docx";
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(fullPath, [1, 2, 3]);
        var movedOriginal = $"{fullPath}.opened";
        var hook = new OccupyNameAfterIsolationHook(fullPath, movedOriginal);

        try
        {
            var action = () => new SafeUploadedFileDeleter(root, hook).DeleteIfExists(relativePath);

            var exception = action.Should().Throw<IOException>().Which;
            exception.Message.Should().Be("持久文件删除原名称已被占用，隔离项已保留等待巡检");
            exception.ToString().Should().NotContain(root);
            WordFileDeletionCleanupAppService.ClassifyFailure(exception).Should().Be("IoError");
            File.ReadAllBytes(fullPath).Should().Equal(
                new byte[] { 6, 5, 4 },
                "恢复时新占用原名的对象不能被删除");
            File.Exists(movedOriginal).Should().BeTrue("最初打开的对象已被竞态方移动，不能误删");

            var quarantine = Directory.EnumerateFiles(directory, ".delete-*.quarantine")
                .Should().ContainSingle().Subject;
            OrphanFilePathRules.IsDeletionQuarantineFileName(Path.GetFileName(quarantine)).Should().BeTrue();
            File.ReadAllBytes(quarantine).Should().Equal(
                new byte[] { 9, 8, 7 },
                "未通过同对象校验的替换对象应保留待巡检");

            var snapshots = new OrphanFileStore(CreateStorageAt(root)).EnumerateManagedFiles();
            snapshots.Should().Contain(item =>
                item.RelativePath.EndsWith(
                    $"/{Path.GetFileName(quarantine)}",
                    StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task 删除请求_应仅标记待删除并在普通查询中隐藏且重复请求幂等()
    {
        var fileId = await SeedFileAsync();

        (await _client.DeleteAsync($"/api/documents/{fileId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.WordFiles.SingleOrDefaultAsync(file => file.Id == fileId)).Should().BeNull();
            var pending = await db.WordFiles.IgnoreQueryFilters().SingleAsync(file => file.Id == fileId);
            pending.DeletionStatus.Should().Be(WordFileDeletionStatus.PendingDeletion);
            pending.DeletionRequestedAt.Should().NotBeNull();
        }

        (await _client.GetAsync($"/api/documents/{fileId}/tables"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.DeleteAsync($"/api/documents/{fileId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("spec")]
    [InlineData("matching")]
    [InlineData("import")]
    public async Task 删除请求_存在任务或导入执行引用时应拒绝且保持活动(string referenceKind)
    {
        var fileId = await SeedFileAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (referenceKind == "spec")
            {
                var customer = new Customer { Name = $"删除保护客户-{Guid.NewGuid():N}" };
                db.Customers.Add(customer);
                await db.SaveChangesAsync();
                db.AcceptanceSpecs.Add(new AcceptanceSpec
                {
                    CustomerId = customer.Id,
                    Project = "删除保护",
                    Specification = "不得删除",
                    WordFileId = fileId
                });
            }
            else if (referenceKind == "matching")
            {
                db.MatchingFillTasks.Add(new MatchingFillTask
                {
                    TaskId = Guid.NewGuid().ToString("N"),
                    SourceFileId = fileId,
                    PayloadJson = "{}"
                });
            }
            else
            {
                db.DocumentImportExecutions.Add(new DocumentImportExecution
                {
                    RequestKey = Guid.NewGuid().ToString("N"),
                    RequestFingerprint = Guid.NewGuid().ToString("N"),
                    SourceFileId = fileId,
                    CreatedByUserId = 1,
                    CompanyId = 1,
                    ResultJson = "{}",
                    Message = string.Empty
                });
            }
            await db.SaveChangesAsync();
        }

        (await _client.DeleteAsync($"/api/documents/{fileId}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verifyDb.WordFiles.SingleAsync(file => file.Id == fileId))
            .DeletionStatus.Should().Be(WordFileDeletionStatus.Active);
    }

    [Fact]
    public async Task 清理器_文件不存在时应删除元数据且第二实例无法重复领取()
    {
        var fileId = await SeedPendingFileAsync();
        var cleanup = _factory.Services.GetRequiredService<IWordFileDeletionCleanupAppService>();

        await cleanup.RunBatchAsync(100, CancellationToken.None);
        await cleanup.RunBatchAsync(100, CancellationToken.None);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.WordFiles.IgnoreQueryFilters().AnyAsync(file => file.Id == fileId)).Should().BeFalse();
    }

    [Fact]
    public async Task 清理器_两个实例并发时应只有一个领取并完成删除()
    {
        var fileId = await SeedPendingFileAsync();
        var cleanup = _factory.Services.GetRequiredService<IWordFileDeletionCleanupAppService>();

        var results = await Task.WhenAll(
            cleanup.RunBatchAsync(100, CancellationToken.None),
            cleanup.RunBatchAsync(100, CancellationToken.None));

        results.Sum().Should().BeGreaterThanOrEqualTo(1);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.WordFiles.IgnoreQueryFilters().AnyAsync(file => file.Id == fileId)).Should().BeFalse();
    }

    [Fact]
    public async Task 清理器_租约未到期不领取而超时后可重新领取()
    {
        var fileId = await SeedPendingFileAsync(leaseToken: "other", leaseExpiresAt: DateTime.UtcNow.AddMinutes(1));
        var cleanup = _factory.Services.GetRequiredService<IWordFileDeletionCleanupAppService>();

        await cleanup.RunBatchAsync(100, CancellationToken.None);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var file = await db.WordFiles.IgnoreQueryFilters().SingleAsync(item => item.Id == fileId);
            file.DeletionLeaseExpiresAt = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        await cleanup.RunBatchAsync(100, CancellationToken.None);
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verifyDb.WordFiles.IgnoreQueryFilters().AnyAsync(item => item.Id == fileId)).Should().BeFalse();
    }

    [Fact]
    public async Task 清理器_租约令牌存在但过期时间为空时应允许重新领取()
    {
        var fileId = await SeedPendingFileAsync(leaseToken: "broken-lease", leaseExpiresAt: null);
        var cleanup = _factory.Services.GetRequiredService<IWordFileDeletionCleanupAppService>();

        await cleanup.RunBatchAsync(100, CancellationToken.None);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.WordFiles.IgnoreQueryFilters().AnyAsync(item => item.Id == fileId)).Should().BeFalse();
    }

    [Fact]
    public async Task 清理器_旧工作者记录失败不得覆盖新租约()
    {
        var fileId = await SeedPendingFileAsync(
            leaseToken: "worker-b",
            leaseExpiresAt: DateTime.UtcNow.AddMinutes(5));
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<WordFileDeletionLeaseStore>();

        (await store.RecordFailureAsync(fileId, "worker-a", "IoError", CancellationToken.None))
            .Should().BeFalse();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var file = await db.WordFiles.IgnoreQueryFilters().SingleAsync(item => item.Id == fileId);
        file.DeletionLeaseToken.Should().Be("worker-b");
        file.DeletionRetryCount.Should().Be(0);
        file.LastDeletionError.Should().BeNull();
    }

    [Fact]
    public async Task 清理器_非法路径应分类退避且不删除元数据()
    {
        var fileId = await SeedPendingFileAsync("../outside.docx");
        var cleanup = _factory.Services.GetRequiredService<IWordFileDeletionCleanupAppService>();

        await cleanup.RunBatchAsync(100, CancellationToken.None);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var file = await db.WordFiles.IgnoreQueryFilters().SingleAsync(item => item.Id == fileId);
        file.LastDeletionError.Should().Be("UnsafePath");
        file.DeletionRetryCount.Should().Be(1);
        file.NextDeletionAttemptAt.Should().BeAfter(DateTime.UtcNow);
        file.DeletionLeaseToken.Should().BeNull();
    }

    [Fact]
    public async Task 清理器_最终复核发现引用应分类退避且保留文件()
    {
        var fileId = await SeedFileAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MatchingFillTasks.Add(new MatchingFillTask
            {
                TaskId = Guid.NewGuid().ToString("N"),
                SourceFileId = fileId,
                PayloadJson = "{}"
            });
            await db.SaveChangesAsync();
            await db.WordFiles
                .IgnoreQueryFilters()
                .Where(item => item.Id == fileId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.DeletionStatus, WordFileDeletionStatus.PendingDeletion)
                    .SetProperty(item => item.DeletionRequestedAt, DateTime.UtcNow)
                    .SetProperty(item => item.NextDeletionAttemptAt, DateTime.UtcNow.AddSeconds(-1)));
        }

        var cleanup = _factory.Services.GetRequiredService<IWordFileDeletionCleanupAppService>();
        await cleanup.RunBatchAsync(100, CancellationToken.None);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var file = await verifyDb.WordFiles.IgnoreQueryFilters().SingleAsync(item => item.Id == fileId);
        file.LastDeletionError.Should().Be("Referenced");
        file.DeletionRetryCount.Should().Be(1);
    }

    [Fact]
    public async Task 清理器_取消应重新抛出且不计失败()
    {
        var fileId = await SeedPendingFileAsync();
        var cleanup = _factory.Services.GetRequiredService<IWordFileDeletionCleanupAppService>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await cleanup.Invoking(service => service.RunBatchAsync(10, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var file = await db.WordFiles.IgnoreQueryFilters().SingleAsync(item => item.Id == fileId);
        file.DeletionRetryCount.Should().Be(0);
        file.LastDeletionError.Should().BeNull();
    }

    [Theory]
    [InlineData("io", "IoError")]
    [InlineData("access", "AccessDenied")]
    public async Task 清理器_IO或权限失败应持久化分类递增退避并释放租约(
        string failure,
        string expectedCategory)
    {
        await using var provider = BuildFaultingCleanupProvider(failure);
        int fileId;
        using (var seedScope = provider.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            var file = new WordFile
            {
                FileName = "fault.docx",
                FileHash = Guid.NewGuid().ToString("N"),
                FilePath = $"uploads/word-files/2026-07-27/{Guid.NewGuid():N}.docx",
                DeletionStatus = WordFileDeletionStatus.PendingDeletion,
                DeletionRequestedAt = DateTime.UtcNow,
                NextDeletionAttemptAt = DateTime.UtcNow.AddSeconds(-1)
            };
            db.WordFiles.Add(file);
            await db.SaveChangesAsync();
            fileId = file.Id;
        }

        var cleanup = provider.GetRequiredService<IWordFileDeletionCleanupAppService>();
        var firstStartedAt = DateTime.UtcNow;
        await cleanup.RunBatchAsync(10, CancellationToken.None);

        using (var firstScope = provider.CreateScope())
        {
            var db = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var file = await db.WordFiles.IgnoreQueryFilters().SingleAsync(item => item.Id == fileId);
            file.DeletionRetryCount.Should().Be(1);
            file.LastDeletionError.Should().Be(expectedCategory);
            file.DeletionLeaseToken.Should().BeNull();
            file.DeletionLeaseExpiresAt.Should().BeNull();
            file.NextDeletionAttemptAt.Should().BeOnOrAfter(firstStartedAt.AddMinutes(1));
            file.NextDeletionAttemptAt.Should().BeBefore(DateTime.UtcNow.AddMinutes(1).AddSeconds(5));
            file.NextDeletionAttemptAt = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        var secondStartedAt = DateTime.UtcNow;
        await cleanup.RunBatchAsync(10, CancellationToken.None);
        using var secondScope = provider.CreateScope();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var second = await secondDb.WordFiles.IgnoreQueryFilters().SingleAsync(item => item.Id == fileId);
        second.DeletionRetryCount.Should().Be(2);
        second.LastDeletionError.Should().Be(expectedCategory);
        second.DeletionLeaseToken.Should().BeNull();
        second.NextDeletionAttemptAt.Should().BeOnOrAfter(secondStartedAt.AddMinutes(2));
        second.NextDeletionAttemptAt.Should().BeBefore(DateTime.UtcNow.AddMinutes(2).AddSeconds(5));
    }

    private async Task<int> SeedFileAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.SystemUsers.OrderBy(item => item.Id).FirstAsync();
        var owner = await db.AuthUserOrgUnits
            .Where(item => item.UserId == user.Id)
            .Select(item => item.OrgUnitId)
            .FirstAsync();
        var file = new WordFile
        {
            CompanyId = user.CompanyId,
            CreatedByUserId = user.Id,
            OwnerOrgUnitId = owner,
            FileName = $"{Guid.NewGuid():N}.docx",
            FileHash = Guid.NewGuid().ToString("N"),
            FilePath = $"uploads/word-files/2026-07-27/{Guid.NewGuid():N}.docx",
            FileType = UploadedFileType.WordDocx
        };
        db.WordFiles.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
    }

    private async Task<int> SeedPendingFileAsync(
        string? path = null,
        string? leaseToken = null,
        DateTime? leaseExpiresAt = null)
    {
        var fileId = await SeedFileAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var file = await db.WordFiles.SingleAsync(item => item.Id == fileId);
        file.FilePath = path ?? file.FilePath;
        file.DeletionStatus = WordFileDeletionStatus.PendingDeletion;
        file.DeletionRequestedAt = DateTime.UtcNow;
        file.NextDeletionAttemptAt = DateTime.UtcNow.AddSeconds(-1);
        file.DeletionLeaseToken = leaseToken;
        file.DeletionLeaseExpiresAt = leaseExpiresAt;
        await db.SaveChangesAsync();
        return fileId;
    }

    private static ServiceProvider BuildFaultingCleanupProvider(string failure)
    {
        var services = new ServiceCollection();
        var connection = new SqliteConnection($"Data Source=fault-cleanup-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        connection.Open();
        services.AddSingleton(connection);
        services.AddDbContext<AppDbContext>((provider, options) =>
            options.UseSqlite(provider.GetRequiredService<SqliteConnection>()));
        services.AddLogging();
        services.AddSingleton<IFileStorageService>(_ => new FaultingDeletionStorage(failure));
        services.AddScoped<WordFileDeletionLeaseStore>();
        services.AddSingleton<IWordFileDeletionCleanupAppService, WordFileDeletionCleanupAppService>();
        return services.BuildServiceProvider();
    }

    private static FileStorageService CreateStorageAt(string root)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FileStorage:BasePath"] = root })
            .Build();
        return new FileStorageService(new DeletionTestWebHostEnvironment(root), configuration);
    }

    private sealed class DeletionTestWebHostEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = nameof(DocumentFileDeletionTests);
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class ReplaceOpenedTargetHook(string fullPath, string movedPath) : ISafeFileDeletionRaceHook
    {
        public void AfterTargetOpened(string relativePath)
        {
            File.Move(fullPath, movedPath);
            File.WriteAllBytes(fullPath, [9, 8, 7]);
        }
    }

    private sealed class ReplaceWithExternalLinkHook(
        string fullPath,
        string movedPath,
        string outsideFile) : ISafeFileDeletionRaceHook
    {
        public void AfterTargetOpened(string relativePath)
        {
            File.Move(fullPath, movedPath);
            File.CreateSymbolicLink(fullPath, outsideFile);
        }
    }

    private sealed class OccupyNameAfterIsolationHook(string fullPath, string movedPath)
        : ISafeFileDeletionRaceHook
    {
        public void AfterTargetOpened(string relativePath)
        {
            File.Move(fullPath, movedPath);
            File.WriteAllBytes(fullPath, [9, 8, 7]);
        }

        public void AfterTargetIsolated(string relativePath)
        {
            File.WriteAllBytes(fullPath, [6, 5, 4]);
        }
    }

    private sealed class FaultingDeletionStorage : TestFileStorageService, IDisposable
    {
        private readonly string _failure;
        private readonly string _root;

        public FaultingDeletionStorage(string failure)
            : this(failure, Path.Combine(Path.GetTempPath(), "AcceptanceSpecSystem-FaultDelete", Guid.NewGuid().ToString("N")))
        {
        }

        private FaultingDeletionStorage(string failure, string root) : base(root)
        {
            _failure = failure;
            _root = root;
            Directory.CreateDirectory(root);
        }

        public override Task DeleteUploadedWordFileIfExistsAsync(
            string? relativePath,
            UploadedFileType fileType,
            CancellationToken cancellationToken = default)
        {
            return _failure == "access"
                ? Task.FromException(new UnauthorizedAccessException("模拟权限失败"))
                : Task.FromException(new IOException("模拟 IO 失败"));
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}

internal sealed class ReparsePointFactAttribute : FactAttribute
{
    public ReparsePointFactAttribute()
    {
        var root = Path.Combine(Path.GetTempPath(), "AcceptanceSpecSystem-Reparse-Probe", Guid.NewGuid().ToString("N"));
        var target = $"{root}-target";
        try
        {
            Directory.CreateDirectory(target);
            Directory.CreateSymbolicLink(root, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Skip = "当前运行环境不允许创建符号链接，跳过 reparse-point 实体烟测";
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root);
                if (Directory.Exists(target))
                    Directory.Delete(target, recursive: true);
            }
            catch
            {
            }
        }
    }
}

internal sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "该测试验证 Windows 同句柄删除语义";
    }
}

internal sealed class LinuxOnlyFactAttribute : FactAttribute
{
    public LinuxOnlyFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "该测试验证 Linux dirfd/openat/unlinkat 删除语义";
    }
}
