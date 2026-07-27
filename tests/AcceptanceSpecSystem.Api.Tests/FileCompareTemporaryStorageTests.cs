using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application;
using AcceptanceSpecSystem.Application.Services;
using FluentAssertions;
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
        Directory.SetLastWriteTimeUtc(expiredDirectory, DateTime.UtcNow.AddHours(-25));
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
    public async Task Lease_标记被替换后拒绝打开且不得递归删除伪造目录()
    {
        var service = CreateService();
        var lease = await service.StageUploadAsync(new MemoryStream([1]), 1);
        var directory = Directory.EnumerateDirectories(_root).Single();
        File.WriteAllText(Path.Combine(directory, ".acceptance-file-compare"), "tampered");

        Action open = () => lease.OpenRead();
        open.Should().Throw<ApplicationServiceException>()
            .Where(exception => exception.Code == 400);

        await lease.DisposeAsync();
        Directory.Exists(directory).Should().BeTrue("标记异常时宁可保留也不能递归删除未知目录");
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

    private FileCompareTemporaryStorage CreateService()
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
            Microsoft.Extensions.Options.Options.Create(new FileCompareTemporaryStorageOptions()),
            TimeProvider.System);
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

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
