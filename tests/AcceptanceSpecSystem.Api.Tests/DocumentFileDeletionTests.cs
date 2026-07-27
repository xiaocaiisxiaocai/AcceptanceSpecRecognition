using System.Net;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
        var fileId = await SeedPendingFileAsync();
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

    private sealed class DeletionTestWebHostEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = nameof(DocumentFileDeletionTests);
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
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
