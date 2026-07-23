using AcceptanceSpecSystem.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class StreamingDownloadTests
{
    [Fact]
    public void FileStorageOpenReadStream_ShouldBeReadOnlyRejectTraversalAndReleaseHandle()
    {
        var root = Path.Combine(Path.GetTempPath(), "AcceptanceSpecSystem.StreamingTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string relativePath = "uploads/filled-files/result.docx";
            var absolutePath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            File.WriteAllBytes(absolutePath, [1, 2, 3, 4]);
            var storage = CreateStorage(root);

            var stream = storage.OpenReadStream(relativePath);
            stream.CanRead.Should().BeTrue();
            stream.CanWrite.Should().BeFalse();
            stream.Dispose();

            using (File.Open(absolutePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                // 能取得独占句柄，证明响应结束后的流释放不会遗留文件占用。
            }

            Action openTraversal = () => storage.OpenReadStream("../outside.bin");
            openTraversal.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FileStreamResult_WhenRequestIsCancelled_ShouldDisposeSourceStream()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddMvcCore()
            .Services
            .BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            RequestAborted = cancellation.Token
        };
        httpContext.Response.Body = new MemoryStream();
        var source = new TrackingMemoryStream(new byte[128 * 1024]);
        var result = new FileStreamResult(source, "application/octet-stream")
        {
            FileDownloadName = "result.bin"
        };
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        cancellation.Cancel();
        try
        {
            await result.ExecuteResultAsync(actionContext);
        }
        catch (OperationCanceledException)
        {
            // 框架版本可能传播或吞掉客户端断开；两种情况下都必须释放源流。
        }

        source.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task FileStreamResult_WhenCancelledAfterFirstRead_ShouldDisposeSourceStream()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddMvcCore()
            .Services
            .BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            RequestAborted = cancellation.Token
        };
        httpContext.Response.Body = new MemoryStream();
        var source = new CancelAfterFirstReadStream(new byte[256 * 1024], cancellation);
        var result = new FileStreamResult(source, "application/octet-stream");
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        try
        {
            await result.ExecuteResultAsync(actionContext);
        }
        catch (OperationCanceledException)
        {
        }

        source.ReadCount.Should().BeGreaterThan(0);
        source.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void DownloadImplementations_ShouldUseStreamContractsInsteadOfReadAllBytes()
    {
        ReadRepositoryFile("src/AcceptanceSpecSystem.Application/Services/BatchReplyAppService.Download.cs")
            .Should().NotContain("ReadAllBytes");
        ReadRepositoryFile("src/AcceptanceSpecSystem.Application/Services/MatchingTaskAppService.cs")
            .Should().NotContain("ReadAllBytes");
        ReadRepositoryFile("src/AcceptanceSpecSystem.Api/Controllers/MatchingApiControllerBase.cs")
            .Should().Contain("File(result.Content");
        ReadRepositoryFile("src/AcceptanceSpecSystem.Application/Services/MatchingOperationResults.cs")
            .Should().Contain("MatchingDownloadResult(Stream Content");
        ReadRepositoryFile("src/AcceptanceSpecSystem.Application/Services/ExecutionHistoryAppService.cs")
            .Should().Contain("JsonSerializer.DeserializeAsync<ExecutionHistoryDetailDto>");
    }

    private static FileStorageService CreateStorage(string root)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:BasePath"] = root
            })
            .Build();
        return new FileStorageService(new TestWebHostEnvironment(root), configuration);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        return File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AcceptanceSpecSystem.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("未找到仓库根目录");
    }

    private sealed class TrackingMemoryStream(byte[] buffer) : MemoryStream(buffer)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class CancelAfterFirstReadStream(
        byte[] buffer,
        CancellationTokenSource cancellation) : MemoryStream(buffer)
    {
        public int ReadCount { get; private set; }
        public bool IsDisposed { get; private set; }

        public override ValueTask<int> ReadAsync(
            Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            var read = base.Read(destination.Span);
            if (ReadCount == 1)
            {
                cancellation.Cancel();
            }

            return ValueTask.FromResult(read);
        }

        public override async Task CopyToAsync(
            Stream destination,
            int bufferSize,
            CancellationToken cancellationToken)
        {
            var copyBuffer = new byte[Math.Min(bufferSize, 16 * 1024)];
            while (true)
            {
                var read = await ReadAsync(copyBuffer, cancellationToken);
                if (read == 0)
                {
                    return;
                }

                await destination.WriteAsync(copyBuffer.AsMemory(0, read), cancellationToken);
            }
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "StreamingDownloadTests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
