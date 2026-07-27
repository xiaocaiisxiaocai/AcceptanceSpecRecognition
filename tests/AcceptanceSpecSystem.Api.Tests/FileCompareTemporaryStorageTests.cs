using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application;
using AcceptanceSpecSystem.Application.Services;
using FluentAssertions;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class FileCompareTemporaryStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "AcceptanceSpecSystem.Api.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void WindowsNativeAbi_当前支持架构的布局应与系统契约一致()
    {
        if (!OperatingSystem.IsWindows())
            return;

        WindowsNativeTemporaryFileSystem.ValidateAbi();

        WindowsNativeTemporaryFileSystem.ObjectAttributesSize.Should().Be(48);
        WindowsNativeTemporaryFileSystem.ObjectAttributesRootOffset.Should().Be(8);
        WindowsNativeTemporaryFileSystem.ObjectAttributesNameOffset.Should().Be(16);
        WindowsNativeTemporaryFileSystem.IoStatusBlockSize.Should().Be(16);
    }

    [Fact]
    public async Task StageUpload_按64KiB分块计算实际长度和哈希且释放后清理()
    {
        var service = CreateService();
        var content = Enumerable.Range(0, 150_000).Select(index => (byte)(index % 251)).ToArray();
        var stream = new MaximumReadStream(content, 64 * 1024);

        var lease = await service.StageUploadAsync(stream, content.Length);
        lease.Length.Should().Be(content.Length);
        lease.Sha256.Should().Be(Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant());
        using (var read = lease.OpenRead())
        {
            using var copied = new MemoryStream();
            await read.CopyToAsync(copied);
            copied.ToArray().Should().Equal(content);
        }
        Directory.EnumerateDirectories(_root).Should().ContainSingle();

        await lease.DisposeAsync();
        await lease.DisposeAsync();
        Directory.EnumerateDirectories(_root).Should().BeEmpty();
    }

    [Fact]
    public async Task StageUpload_实际字节超过上限返回413并只清理自身目录()
    {
        var service = CreateService();
        var existing = await service.StageUploadAsync(new MemoryStream([1]), 1);
        Func<Task> stage = async () =>
            await service.StageUploadAsync(new MaximumReadStream(new byte[65_537], 64 * 1024), 65_536);

        await stage.Should().ThrowAsync<ApplicationServiceException>()
            .Where(exception => exception.Code == 413);
        Directory.EnumerateDirectories(_root).Should().ContainSingle();
        await existing.DisposeAsync();
    }

    [Fact]
    public async Task CleanupExpired_仅删除带合法标记的过期目录并保留新目录和未知目录()
    {
        var service = CreateService();
        await using var active = await service.StageUploadAsync(new MemoryStream([1]), 1);
        var activeDirectory = Directory.EnumerateDirectories(_root).Single();
        Directory.SetLastWriteTimeUtc(activeDirectory, DateTime.UtcNow.AddHours(-25));
        var orphanName = Guid.NewGuid().ToString("N");
        var expiredDirectory = Path.Combine(_root, orphanName);
        Directory.CreateDirectory(expiredDirectory);
        File.WriteAllText(
            Path.Combine(expiredDirectory, ".acceptance-file-compare"),
            $"{orphanName}:{Guid.NewGuid():N}");
        File.WriteAllBytes(Path.Combine(expiredDirectory, "payload.tmp"), [2]);
        File.SetLastWriteTimeUtc(
            Path.Combine(expiredDirectory, ".acceptance-file-compare"),
            DateTime.UtcNow.AddHours(-25));
        await using var recent = await service.StageUploadAsync(new MemoryStream([2]), 1);
        var recentDirectory = Directory.EnumerateDirectories(_root)
            .Single(path =>
                !string.Equals(path, expiredDirectory, StringComparison.Ordinal) &&
                !string.Equals(path, activeDirectory, StringComparison.Ordinal));
        var unknown = Path.Combine(_root, "not-managed");
        Directory.CreateDirectory(unknown);
        Directory.SetLastWriteTimeUtc(unknown, DateTime.UtcNow.AddHours(-25));

        await service.CleanupExpiredAsync();
        await service.CleanupExpiredAsync();

        Directory.Exists(expiredDirectory).Should().BeFalse();
        Directory.Exists(activeDirectory).Should().BeTrue();
        Directory.Exists(recentDirectory).Should().BeTrue();
        Directory.Exists(unknown).Should().BeTrue();
    }

    [Fact]
    public async Task Lease_活动标记句柄应阻止外部替换并保持有效()
    {
        var service = CreateService();
        var lease = await service.StageUploadAsync(new MemoryStream([1]), 1);
        var directory = Directory.EnumerateDirectories(_root).Single();

        Action replace = () =>
            File.WriteAllText(Path.Combine(directory, ".acceptance-file-compare"), "tampered");
        replace.Should().Throw<IOException>();
        using (var read = lease.OpenRead())
            read.ReadByte().Should().Be(1);

        await lease.DisposeAsync();
        Directory.Exists(directory).Should().BeFalse();
    }

    [Fact]
    public async Task StageUpload_取消后应清理本请求目录()
    {
        var service = CreateService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> stage = async () =>
            await service.StageUploadAsync(new MemoryStream([1]), 1, cancellation.Token);

        await stage.Should().ThrowAsync<OperationCanceledException>();
        Directory.EnumerateDirectories(_root).Should().BeEmpty();
    }

    [Theory]
    [InlineData(CreationFailureStage.Directory)]
    [InlineData(CreationFailureStage.Flush)]
    [InlineData(CreationFailureStage.Marker)]
    public async Task StageUpload_创建任一步失败都应回滚新目录(CreationFailureStage stage)
    {
        var service = CreateService(new CreationFaultHook(stage));

        Func<Task> stageUpload = async () =>
            await service.StageUploadAsync(new MemoryStream([1]), 1);

        await stageUpload.Should().ThrowAsync<IOException>();
        Directory.EnumerateDirectories(_root).Should().BeEmpty();
    }

    [Fact]
    public async Task CreateOutput_标记持久化前目录只能以可接管Init名称存在()
    {
        var hook = new CreationNameProbeHook(_root);
        var service = CreateService(hook);

        await using var lease = await service.CreateOutputAsync();

        hook.DirectoryNameAfterCreate.Should().MatchRegex(
            @"^\.init-[0-9a-f]{32}-[0-9a-f]{32}$");
        hook.DirectoryNameAfterMarker.Should().Be(hook.DirectoryNameAfterCreate);
        Directory.EnumerateDirectories(_root)
            .Select(Path.GetFileName)
            .Count(name =>
                name != null &&
                name.Length == 32 &&
                name.All(character => char.IsAsciiHexDigitLower(character)))
            .Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupExpired_重启后应接管崩溃遗留Init目录(bool markerWasDurable)
    {
        Directory.CreateDirectory(_root);
        var requestId = Guid.NewGuid().ToString("N");
        var init = Path.Combine(
            _root,
            $".init-{requestId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(init);
        if (markerWasDurable)
        {
            File.WriteAllText(
                Path.Combine(init, ".acceptance-file-compare"),
                $"{requestId}:{Guid.NewGuid():N}");
        }
        var service = CreateService();

        await service.CleanupExpiredAsync();

        Directory.Exists(init).Should().BeFalse();
    }

    [Fact]
    public async Task CleanupExpired_崩溃遗留Init含未知条目时必须FailClosed保留()
    {
        Directory.CreateDirectory(_root);
        var init = Path.Combine(
            _root,
            $".init-{Guid.NewGuid():N}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(init);
        File.WriteAllText(Path.Combine(init, "unknown.txt"), "sentinel");
        var service = CreateService();

        var exception = await FluentActions.Awaiting(() => service.CleanupExpiredAsync())
            .Should().ThrowAsync<FileCompareTemporaryCleanupException>();

        exception.Which.FailureCount.Should().Be(1);
        File.ReadAllText(Path.Combine(init, "unknown.txt")).Should().Be("sentinel");
    }

    [Fact]
    public async Task CreateOutput_发布时同名被占用不得覆盖且应回滚Init()
    {
        var hook = new PublishCollisionHook(_root);
        var service = CreateService(hook);

        Func<Task> create = async () => await service.CreateOutputAsync();

        await create.Should().ThrowAsync<System.ComponentModel.Win32Exception>()
            .Where(exception => exception.NativeErrorCode == 183);
        Directory.EnumerateDirectories(_root, ".init-*").Should().BeEmpty();
        File.ReadAllText(Path.Combine(hook.CollisionDirectory!, "sentinel.txt"))
            .Should().Be("replacement");
    }

    [Fact]
    public async Task CleanupExpired_单目录失败应继续并抛出不含路径的聚合异常()
    {
        var service = CreateService(new CleanupFirstFaultHook());
        var first = CreateOrphanDirectory();
        var second = CreateOrphanDirectory();

        var exception = await FluentActions.Awaiting(() => service.CleanupExpiredAsync())
            .Should().ThrowAsync<FileCompareTemporaryCleanupException>();

        exception.Which.FailureCount.Should().Be(1);
        exception.Which.Message.Should().NotContain(_root);
        new[] { Directory.Exists(first), Directory.Exists(second) }
            .Should().ContainSingle(exists => exists);
    }

    [Fact]
    public async Task CleanupExpired_标记即使过期也不得清理另一实例持有的活动Lease()
    {
        var owner = CreateService(heartbeatSeconds: 3_600);
        var cleaner = CreateService();
        await using var lease = await owner.StageUploadAsync(new MemoryStream([1]), 1);
        var directory = Directory.EnumerateDirectories(_root).Single();
        var marker = Path.Combine(directory, ".acceptance-file-compare");
        File.SetLastWriteTimeUtc(marker, DateTime.UtcNow.AddHours(-25));

        await cleaner.CleanupExpiredAsync();

        Directory.Exists(directory).Should().BeTrue();
    }

    [Fact]
    public async Task CleanupExpired_打开候选后原名被替换时只删除已固定对象()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var orphan = CreateOrphanDirectory();
        var requestId = Path.GetFileName(orphan);
        var displaced = Path.Combine(_root, $".outside-{Guid.NewGuid():N}");
        var sentinel = Path.Combine(orphan, "sentinel.txt");
        var hook = new ReplaceCandidateAfterOpenHook(_root, displaced);
        var service = CreateService(hook);

        await service.CleanupExpiredAsync();

        Directory.Exists(displaced).Should().BeFalse("清理器应删除已固定的旧候选对象");
        Directory.Exists(orphan).Should().BeTrue("同名替代目录不是已固定候选");
        File.ReadAllText(sentinel).Should().Be("outside-sentinel");
        Path.GetFileName(orphan).Should().Be(requestId);
    }

    [Fact]
    public async Task LeaseDispose_隔离后重建同名目录时不得删除替代对象()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var hook = new RecreateRequestAfterQuarantineHook();
        var service = CreateService(hook);
        var lease = await service.StageUploadAsync(new MemoryStream([1]), 1);
        var requestDirectory = Directory.EnumerateDirectories(_root).Single();
        hook.RequestDirectory = requestDirectory;

        await lease.DisposeAsync();

        Directory.Exists(requestDirectory).Should().BeTrue();
        File.ReadAllText(Path.Combine(requestDirectory, "sentinel.txt"))
            .Should().Be("replacement");
        Directory.EnumerateDirectories(_root, ".gc-*").Should().BeEmpty();
    }

    [Fact]
    public async Task CleanupExpired_隔离后出现未知条目应保留隔离对象且不递归删除()
    {
        if (!OperatingSystem.IsWindows())
            return;

        CreateOrphanDirectory();
        var hook = new AddUnknownEntryAfterQuarantineHook(_root);
        var service = CreateService(hook);

        var exception = await FluentActions.Awaiting(() => service.CleanupExpiredAsync())
            .Should().ThrowAsync<FileCompareTemporaryCleanupException>();

        exception.Which.FailureCount.Should().Be(1);
        var quarantine = Directory.EnumerateDirectories(_root, ".gc-*").Single();
        File.ReadAllText(Path.Combine(quarantine, "unknown.txt")).Should().Be("sentinel");
    }

    [Fact]
    public async Task CleanupExpired_payload打开后名称被替换时不得删除替代文件()
    {
        if (!OperatingSystem.IsWindows())
            return;

        CreateOrphanDirectory();
        var hook = new ReplacePayloadAfterOpenHook(_root);
        var service = CreateService(hook);

        await FluentActions.Awaiting(() => service.CleanupExpiredAsync())
            .Should().ThrowAsync<FileCompareTemporaryCleanupException>();

        var quarantine = Directory.EnumerateDirectories(_root, ".gc-*").Single();
        File.ReadAllText(Path.Combine(quarantine, "payload.tmp")).Should().Be("replacement");
        File.Exists(Path.Combine(quarantine, "opened-payload.old")).Should().BeFalse(
            "原先打开的对象应通过其句柄删除");
    }

    [Fact]
    public async Task CleanupExpired_两个实例并发认领同一候选时只能有一个进入隔离()
    {
        if (!OperatingSystem.IsWindows())
            return;

        CreateOrphanDirectory();
        var hook = new TwoCleanerOpenBarrierHook();
        var first = CreateService(hook);
        var second = CreateService(hook);

        await Task.WhenAll(
            Task.Run(() => first.CleanupExpiredAsync()),
            Task.Run(() => second.CleanupExpiredAsync()));

        Directory.EnumerateDirectories(_root)
            .Where(path => Path.GetFileName(path).Length == 32)
            .Should().BeEmpty();
        Directory.EnumerateDirectories(_root, ".gc-*").Should().BeEmpty();
    }

    [ReparsePointFact]
    public async Task CleanupExpired_payload为符号链接时应拒绝且根外文件保持不变()
    {
        var directory = CreateOrphanDirectory();
        var payload = Path.Combine(directory, "payload.tmp");
        File.Delete(payload);
        var outsideRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideRoot);
        var outside = Path.Combine(outsideRoot, "sentinel.txt");
        File.WriteAllText(outside, "outside");
        try
        {
            File.CreateSymbolicLink(payload, outside);
            var service = CreateService();

            await FluentActions.Awaiting(() => service.CleanupExpiredAsync())
                .Should().ThrowAsync<FileCompareTemporaryCleanupException>();

            File.ReadAllText(outside).Should().Be("outside");
            Directory.EnumerateDirectories(_root, ".gc-*").Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(outsideRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CleanupExpired_应接管上次中断后留下的合法隔离目录()
    {
        var hook = new InterruptAfterQuarantineHook();
        var owner = CreateService(hook);
        var lease = await owner.StageUploadAsync(new MemoryStream([1]), 1);

        await lease.DisposeAsync();
        Directory.EnumerateDirectories(_root, ".gc-*").Should().ContainSingle();

        var cleaner = CreateService();
        await cleaner.CleanupExpiredAsync();

        Directory.EnumerateDirectories(_root, ".gc-*").Should().BeEmpty();
    }

    [Fact]
    public async Task StorageDispose_应先释放活动Lease再关闭根句柄()
    {
        var service = CreateService();
        var lease = await service.CreateOutputAsync();
        var output = lease.OpenWrite();
        await output.WriteAsync(new byte[] { 1 });

        service.Dispose();

        Directory.EnumerateDirectories(_root).Should().BeEmpty();
        Action write = () => output.WriteByte(2);
        write.Should().Throw<ObjectDisposedException>();
        await lease.DisposeAsync();
        Action open = () => lease.OpenRead();
        open.Should().Throw<ObjectDisposedException>();
    }

    [WindowsOnlyFact]
    [SupportedOSPlatform("windows")]
    public async Task CreateLease_Windows请求目录仅允许当前用户和System()
    {
        var service = CreateService();
        await using var lease = await service.StageUploadAsync(new MemoryStream([1]), 1);
        var directory = new DirectoryInfo(Directory.EnumerateDirectories(_root).Single());

        var security = FileSystemAclExtensions.GetAccessControl(
            directory,
            AccessControlSections.Access);
        security.AreAccessRulesProtected.Should().BeTrue();
        var allowed = security
            .GetAccessRules(true, false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Where(rule => rule.AccessControlType == AccessControlType.Allow)
            .Select(rule => rule.IdentityReference.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        allowed.Should().BeEquivalentTo(
            WindowsIdentity.GetCurrent().User!.Value,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value);
    }

    [Fact]
    public async Task StorageDispose_并发Lease释放未完成时应等待同一清理任务()
    {
        var hook = new BlockingQuarantineHook();
        var service = CreateService(hook);
        var lease = await service.StageUploadAsync(new MemoryStream([1]), 1);
        var firstDispose = Task.Run(async () => await lease.DisposeAsync());
        hook.Entered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
        try
        {
            var storageDispose = Task.Run(service.Dispose);
            await Task.Delay(100);
            storageDispose.IsCompleted.Should().BeFalse();

            hook.Release.Set();
            await Task.WhenAll(firstDispose, storageDispose);
        }
        finally
        {
            hook.Release.Set();
        }

        Directory.EnumerateDirectories(_root).Should().BeEmpty();
    }

    [Fact]
    public async Task LeaseDispose_流释放发生IO错误时仍应清理目录()
    {
        var service = CreateService(new StreamDisposeFaultHook(new IOException("injected")));
        var lease = await service.CreateOutputAsync();
        var output = lease.OpenWrite();
        await output.WriteAsync(new byte[] { 1 });

        await lease.DisposeAsync();

        Directory.EnumerateDirectories(_root).Should().BeEmpty();
    }

    [Fact]
    public async Task StorageDispose_非预期流错误后仍应继续清理全部Lease并关闭根()
    {
        var hook = new StreamDisposeFaultHook(new InvalidOperationException("injected"));
        var service = CreateService(hook);
        var first = await service.CreateOutputAsync();
        var second = await service.CreateOutputAsync();
        await first.OpenWrite().WriteAsync(new byte[] { 1 });
        await second.OpenWrite().WriteAsync(new byte[] { 2 });

        Action dispose = service.Dispose;
        dispose.Should().Throw<InvalidOperationException>();

        Directory.EnumerateDirectories(_root).Should().BeEmpty();
        Func<Task> create = async () => await service.CreateOutputAsync();
        await create.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task DownloadReadStream_响应消费前保留输出目录且释放后删除()
    {
        var service = CreateService();
        var lease = await service.CreateOutputAsync();
        await using (var output = lease.OpenWrite())
        {
            await output.WriteAsync(new byte[] { 1, 2, 3 });
        }
        Directory.EnumerateDirectories(_root).Should().ContainSingle();

        var responseStream = new LeaseOwnedReadStream(lease.OpenRead(), lease);
        (await responseStream.ReadAsync(new byte[3])).Should().Be(3);
        Directory.EnumerateDirectories(_root).Should().ContainSingle(
            "控制器创建响应流后不得提前删除输出文件");

        await responseStream.DisposeAsync();
        await responseStream.DisposeAsync();
        Directory.EnumerateDirectories(_root).Should().BeEmpty();
    }

    [Fact]
    public void DownloadReadStream_同步释放内部流失败也必须释放Lease并稳定组合异常()
    {
        var lease = new DisposeFaultLease(new InvalidOperationException("lease-sync"));
        var responseStream = new LeaseOwnedReadStream(
            new DisposeFaultStream(new IOException("inner-sync")),
            lease);

        var exception = responseStream.Invoking(stream => stream.Dispose())
            .Should().Throw<AggregateException>().Which;

        lease.DisposeCount.Should().Be(1);
        exception.InnerExceptions.Select(item => item.Message)
            .Should().Equal("inner-sync", "lease-sync");
    }

    [Fact]
    public async Task DownloadReadStream_异步释放内部流失败也必须释放Lease并稳定组合异常()
    {
        var lease = new DisposeFaultLease(new InvalidOperationException("lease-async"));
        var responseStream = new LeaseOwnedReadStream(
            new DisposeFaultStream(new IOException("inner-async")),
            lease);

        var exception = (await responseStream.Invoking(stream => stream.DisposeAsync().AsTask())
                .Should().ThrowAsync<AggregateException>())
            .Which;

        lease.DisposeCount.Should().Be(1);
        exception.InnerExceptions.Select(item => item.Message)
            .Should().Equal("inner-async", "lease-async");
    }

    private FileCompareTemporaryStorage CreateService(
        IFileCompareTemporaryStorageFaultHook? faultHook = null,
        int heartbeatSeconds = 60)
    {
        Directory.CreateDirectory(_root);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileCompareTemporaryStorage:Root"] = _root
            })
            .Build();
        return new FileCompareTemporaryStorage(
            new TestEnvironment { ContentRootPath = _root },
            configuration,
            Microsoft.Extensions.Options.Options.Create(new FileCompareTemporaryStorageOptions
            {
                HeartbeatSeconds = heartbeatSeconds
            }),
            TimeProvider.System,
            faultHook);
    }

    private string CreateOrphanDirectory()
    {
        var name = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, ".acceptance-file-compare"),
            $"{name}:{Guid.NewGuid():N}");
        File.WriteAllBytes(Path.Combine(directory, "payload.tmp"), [1]);
        File.SetLastWriteTimeUtc(
            Path.Combine(directory, ".acceptance-file-compare"),
            DateTime.UtcNow.AddHours(-25));
        return directory;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class MaximumReadStream(byte[] content, int maximumRead) : MemoryStream(content)
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            buffer.Length.Should().BeLessThanOrEqualTo(maximumRead);
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class DisposeFaultStream(Exception exception) : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                throw exception;
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() => ValueTask.FromException(exception);
    }

    private sealed class DisposeFaultLease(Exception exception) : TemporaryFileLease
    {
        public int DisposeCount { get; private set; }
        public override long Length => 0;
        public override string Sha256 => string.Empty;
        public override Stream OpenRead() => throw new NotSupportedException();
        public override Stream OpenWrite() => throw new NotSupportedException();

        public override ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.FromException(exception);
        }
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    public enum CreationFailureStage
    {
        Directory,
        Flush,
        Marker
    }

    private sealed class CreationFaultHook(CreationFailureStage stage)
        : IFileCompareTemporaryStorageFaultHook
    {
        public void AfterRequestDirectoryCreated()
        {
            if (stage == CreationFailureStage.Directory)
                throw new IOException("injected");
        }

        public void AfterMarkerCreated()
        {
            if (stage == CreationFailureStage.Marker)
                throw new IOException("injected");
        }

        public void BeforeMarkerFlush()
        {
            if (stage == CreationFailureStage.Flush)
                throw new IOException("injected");
        }
    }

    private sealed class CreationNameProbeHook(string root)
        : IFileCompareTemporaryStorageFaultHook
    {
        public string? DirectoryNameAfterCreate { get; private set; }
        public string? DirectoryNameAfterMarker { get; private set; }

        public void AfterRequestDirectoryCreated() =>
            DirectoryNameAfterCreate = Path.GetFileName(
                Directory.EnumerateDirectories(root).Single());

        public void AfterMarkerCreated() =>
            DirectoryNameAfterMarker = Path.GetFileName(
                Directory.EnumerateDirectories(root).Single());
    }

    private sealed class PublishCollisionHook(string root)
        : IFileCompareTemporaryStorageFaultHook
    {
        public string? CollisionDirectory { get; private set; }

        public void BeforeRequestDirectoryPublished(string requestId)
        {
            CollisionDirectory = Path.Combine(root, requestId);
            Directory.CreateDirectory(CollisionDirectory);
            File.WriteAllText(
                Path.Combine(CollisionDirectory, "sentinel.txt"),
                "replacement");
        }
    }

    private sealed class CleanupFirstFaultHook : IFileCompareTemporaryStorageFaultHook
    {
        private int _calls;
        public void BeforeCleanupDirectory()
        {
            if (Interlocked.Increment(ref _calls) == 1)
                throw new IOException("injected");
        }
    }

    private sealed class ReplaceCandidateAfterOpenHook(string root, string displaced)
        : IFileCompareTemporaryStorageFaultHook
    {
        private int _called;

        public void AfterCleanupDirectoryOpened(string requestId)
        {
            if (Interlocked.Exchange(ref _called, 1) != 0)
                return;

            var original = Path.Combine(root, requestId);
            Directory.Move(original, displaced);
            Directory.CreateDirectory(original);
            File.WriteAllText(Path.Combine(original, "sentinel.txt"), "outside-sentinel");
        }
    }

    private sealed class RecreateRequestAfterQuarantineHook
        : IFileCompareTemporaryStorageFaultHook
    {
        public string? RequestDirectory { get; set; }
        private int _called;

        public void AfterRequestDirectoryQuarantined(string requestId)
        {
            if (Interlocked.Exchange(ref _called, 1) != 0)
                return;
            RequestDirectory.Should().NotBeNull();
            Path.GetFileName(RequestDirectory).Should().Be(requestId);
            Directory.CreateDirectory(RequestDirectory!);
            File.WriteAllText(Path.Combine(RequestDirectory!, "sentinel.txt"), "replacement");
        }
    }

    private sealed class AddUnknownEntryAfterQuarantineHook(string root)
        : IFileCompareTemporaryStorageFaultHook
    {
        private int _called;

        public void AfterRequestDirectoryQuarantined(string requestId)
        {
            if (Interlocked.Exchange(ref _called, 1) != 0)
                return;
            var quarantine = Directory.EnumerateDirectories(root, $".gc-{requestId}-*").Single();
            File.WriteAllText(Path.Combine(quarantine, "unknown.txt"), "sentinel");
        }
    }

    private sealed class ReplacePayloadAfterOpenHook(string root)
        : IFileCompareTemporaryStorageFaultHook
    {
        private int _called;

        public void BeforeEntryDisposition(string entryName)
        {
            if (entryName != "payload.tmp" ||
                Interlocked.Exchange(ref _called, 1) != 0)
                return;
            var quarantine = Directory.EnumerateDirectories(root, ".gc-*").Single();
            File.Move(
                Path.Combine(quarantine, "payload.tmp"),
                Path.Combine(quarantine, "opened-payload.old"));
            File.WriteAllText(Path.Combine(quarantine, "payload.tmp"), "replacement");
        }
    }

    private sealed class TwoCleanerOpenBarrierHook : IFileCompareTemporaryStorageFaultHook
    {
        private readonly Barrier _barrier = new(2);

        public void AfterCleanupDirectoryOpened(string requestId)
        {
            if (!_barrier.SignalAndWait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("cleaner barrier timeout");
        }
    }

    private sealed class InterruptAfterQuarantineHook : IFileCompareTemporaryStorageFaultHook
    {
        private int _called;

        public void AfterRequestDirectoryQuarantined(string requestId)
        {
            if (Interlocked.Exchange(ref _called, 1) == 0)
                throw new IOException("injected");
        }
    }

    private sealed class BlockingQuarantineHook : IFileCompareTemporaryStorageFaultHook
    {
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();

        public void AfterRequestDirectoryQuarantined(string requestId)
        {
            Entered.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("quarantine release timeout");
        }
    }

    private sealed class StreamDisposeFaultHook(Exception exception)
        : IFileCompareTemporaryStorageFaultHook
    {
        public void AfterTrackedStreamDisposed() => throw exception;
    }
}
