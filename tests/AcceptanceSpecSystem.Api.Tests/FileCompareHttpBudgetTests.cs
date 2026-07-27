using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Controllers;
using AcceptanceSpecSystem.Application;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Application.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class FileCompareHttpBudgetTests :
    IClassFixture<FileCompareHttpBudgetTests.LowBudgetFactory>
{
    private readonly HttpClient _client;

    public FileCompareHttpBudgetTests(LowBudgetFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Preview_节点超过文件比较预算应返回Http和Json422()
    {
        var paragraphs = Enumerable.Range(1, 51).Select(index => $"P{index}").ToArray();
        var bytes = CreateWord(paragraphs);
        var (a, b) = await UploadAsync(bytes, bytes);

        var response = await _client.PostAsync(
            "/api/file-compare/preview",
            ApiClientJson.ToJsonContent(new { fileIdA = a, fileIdB = b }));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ReadAsAsync<ApiResponse<JsonElement>>()).Code.Should().Be(422);
    }

    [Fact]
    public async Task Download_结果字节超过预算应返回Http和Json422且无部分文件()
    {
        var bytes = CreateWord("same");
        var (a, b) = await UploadAsync(bytes, bytes);

        var response = await _client.PostAsync(
            "/api/file-compare/download",
            ApiClientJson.ToJsonContent(new { fileIdA = a, fileIdB = b }));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        (await response.ReadAsAsync<ApiResponse<JsonElement>>()).Code.Should().Be(422);
    }

    [Fact]
    public async Task Preview_真实Json字节超过预算应返回完整Http和Json422()
    {
        var bytes = CreateWord("same");
        var (a, b) = await UploadAsync(bytes, bytes);

        var response = await _client.PostAsync(
            "/api/file-compare/preview",
            ApiClientJson.ToJsonContent(new { fileIdA = a, fileIdB = b }));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        var body = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        body.Code.Should().Be(422);
        body.Message.Should().Contain("文件比较");
    }

    [Fact]
    public void OpenApi_应声明上传413及预览下载422()
    {
        GetStatuses(nameof(FileCompareController.Upload))
            .Should().Contain(StatusCodes.Status413PayloadTooLarge);
        GetStatuses(nameof(FileCompareController.Preview))
            .Should().Contain(StatusCodes.Status422UnprocessableEntity);
        GetStatuses(nameof(FileCompareController.Download))
            .Should().Contain(StatusCodes.Status422UnprocessableEntity);
    }

    private static IEnumerable<int> GetStatuses(string methodName) =>
        typeof(FileCompareController).GetMethod(methodName)!
            .GetCustomAttributes(typeof(ProducesResponseTypeAttribute), inherit: true)
            .Cast<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode);

    private async Task<(int A, int B)> UploadAsync(byte[] first, byte[] second)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(OfficeContent(first), "fileA", "a.docx");
        multipart.Add(OfficeContent(second), "fileB", "b.docx");
        using var response = await _client.PostAsync("/api/file-compare/upload", multipart);
        response.EnsureSuccessStatusCode();
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return (
            json.Data.GetProperty("fileA").GetProperty("fileId").GetInt32(),
            json.Data.GetProperty("fileB").GetProperty("fileId").GetInt32());
    }

    private static ByteArrayContent OfficeContent(byte[] content)
    {
        var result = new ByteArrayContent(content);
        result.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        return result;
    }

    private static byte[] CreateWord(params string[] paragraphs)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(
                   stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(
                paragraphs.Select(text => new Paragraph(new Run(new Text(text))))));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    public sealed class LowBudgetFactory : ApiWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ResourceBudgets:MaxFileCompareCells"] = "100",
                    ["ResourceBudgets:MaxFileCompareDiffItems"] = "100",
                    ["ResourceBudgets:MaxFileCompareResultBytes"] = "1"
                }));
        }
    }
}

public sealed class FileCompareUploadLimitHttpTests :
    IClassFixture<FileCompareUploadLimitHttpTests.UploadLimitFactory>
{
    private readonly HttpClient _client;

    public FileCompareUploadLimitHttpTests(UploadLimitFactory factory) =>
        _client = factory.CreateClient();

    [Fact]
    public async Task Upload_暂存检测实际超限应返回Http和Json413()
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent([1]), "fileA", "a.docx");
        multipart.Add(new ByteArrayContent([1]), "fileB", "b.docx");

        var response = await _client.PostAsync("/api/file-compare/upload", multipart);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await response.ReadAsAsync<ApiResponse<JsonElement>>()).Code.Should().Be(413);
    }

    public sealed class UploadLimitFactory : ApiWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFileCompareTemporaryStorage>();
                services.AddSingleton<IFileCompareTemporaryStorage, AlwaysTooLargeStorage>();
            });
        }
    }

    private sealed class AlwaysTooLargeStorage : IFileCompareTemporaryStorage
    {
        public Task<TemporaryFileLease> StageUploadAsync(
            Stream content,
            long maxBytes,
            CancellationToken cancellationToken = default) =>
            throw new ApplicationServiceException(413, "单个比较文件大小不能超过50MB");
        public Task<TemporaryFileLease> CreateOutputAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task CleanupExpiredAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}

public sealed class FileComparePreviewSerializationLeaseTests :
    IClassFixture<FileComparePreviewSerializationLeaseTests.SerializationFactory>
{
    private readonly SerializationFactory _factory;
    private readonly HttpClient _client;

    public FileComparePreviewSerializationLeaseTests(SerializationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Preview_解析Lease应持有到真实HttpJson序列化完成()
    {
        var bytes = CreateWord("same");
        var (a, b) = await UploadAsync(bytes);
        var preview = _client.PostAsync(
            "/api/file-compare/preview",
            ApiClientJson.ToJsonContent(new { fileIdA = a, fileIdB = b }));
        var storage = _factory.Services.GetRequiredService<BlockingPreviewStorage>();
        try
        {
            await storage.SerializationStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

            using var scope = _factory.Services.CreateScope();
            var governor = scope.ServiceProvider.GetRequiredService<IResourceBudgetGovernor>();
            var competing = governor.AcquireAsync(ResourceWorkload.DocumentParsing).AsTask();
            await Task.Delay(100);
            competing.IsCompleted.Should().BeFalse(
                "预览结果仍在写入受限 JSON 响应，解析资源不得提前让给下一请求");

            storage.ContinueSerialization.TrySetResult();
            using var response = await preview;
            response.EnsureSuccessStatusCode();
            using var acquired = await competing.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            storage.ContinueSerialization.TrySetResult();
        }
    }

    private async Task<(int A, int B)> UploadAsync(byte[] bytes)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(bytes), "fileA", "a.docx");
        multipart.Add(new ByteArrayContent(bytes), "fileB", "b.docx");
        using var response = await _client.PostAsync("/api/file-compare/upload", multipart);
        response.EnsureSuccessStatusCode();
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return (
            json.Data.GetProperty("fileA").GetProperty("fileId").GetInt32(),
            json.Data.GetProperty("fileB").GetProperty("fileId").GetInt32());
    }

    private static byte[] CreateWord(string text)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(
                   stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(new Run(new Text(text)))));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    public sealed class SerializationFactory : ApiWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ResourceBudgets:MaxConcurrentDocumentParsers"] = "1"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFileCompareTemporaryStorage>();
                services.AddSingleton<FileCompareTemporaryStorage>();
                services.AddSingleton<BlockingPreviewStorage>();
                services.AddSingleton<IFileCompareTemporaryStorage>(
                    provider => provider.GetRequiredService<BlockingPreviewStorage>());
            });
        }
    }

    public sealed class BlockingPreviewStorage(FileCompareTemporaryStorage inner)
        : IFileCompareTemporaryStorage
    {
        public TaskCompletionSource SerializationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueSerialization { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TemporaryFileLease> StageUploadAsync(
            Stream content,
            long maxBytes,
            CancellationToken cancellationToken = default) =>
            inner.StageUploadAsync(content, maxBytes, cancellationToken);

        public Task<TemporaryFileLease> CreateOutputAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TemporaryFileLease>(
                new BlockingOutputLease(SerializationStarted, ContinueSerialization));

        public Task CleanupExpiredAsync(CancellationToken cancellationToken = default) =>
            inner.CleanupExpiredAsync(cancellationToken);

        private sealed class BlockingOutputLease(
            TaskCompletionSource started,
            TaskCompletionSource continueSerialization) : TemporaryFileLease
        {
            private readonly MemoryStream _content = new();
            public override long Length => _content.Length;
            public override string Sha256 => string.Empty;
            public override Stream OpenRead() => new MemoryStream(_content.ToArray());
            public override Stream OpenWrite() =>
                new BlockingWriteStream(_content, started, continueSerialization.Task);
            public override ValueTask DisposeAsync()
            {
                _content.Dispose();
                return ValueTask.CompletedTask;
            }
        }

        private sealed class BlockingWriteStream(
            Stream inner,
            TaskCompletionSource started,
            Task continueSerialization) : Stream
        {
            private int _blocked;
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => inner.Length;
            public override long Position
            {
                get => inner.Position;
                set => throw new NotSupportedException();
            }
            public override void Flush() => inner.Flush();
            public override Task FlushAsync(CancellationToken cancellationToken) =>
                inner.FlushAsync(cancellationToken);
            public override async ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                if (Interlocked.Exchange(ref _blocked, 1) == 0)
                {
                    started.TrySetResult();
                    await continueSerialization.WaitAsync(cancellationToken);
                }
                await inner.WriteAsync(buffer, cancellationToken);
            }
            public override void Write(byte[] buffer, int offset, int count) =>
                inner.Write(buffer, offset, count);
            public override int Read(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) =>
                throw new NotSupportedException();
            public override void SetLength(long value) =>
                throw new NotSupportedException();
        }
    }
}

public sealed class FileCompareSecondUploadFailureTests :
    IClassFixture<FileCompareSecondUploadFailureTests.SecondFailureFactory>
{
    private readonly SecondFailureFactory _factory;
    private readonly HttpClient _client;

    public FileCompareSecondUploadFailureTests(SecondFailureFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Upload_第二份暂存失败应立即释放第一份Lease()
    {
        var bytes = CreateWord("valid");
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(bytes), "fileA", "a.docx");
        multipart.Add(new ByteArrayContent(bytes), "fileB", "b.docx");

        var response = await _client.PostAsync("/api/file-compare/upload", multipart);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Services.GetRequiredService<FailSecondStorage>().FirstDisposed.Should().BeTrue();
    }

    private static byte[] CreateWord(string text)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(
                   stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(new Run(new Text(text)))));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    public sealed class SecondFailureFactory : ApiWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFileCompareTemporaryStorage>();
                services.AddSingleton<FileCompareTemporaryStorage>();
                services.AddSingleton<FailSecondStorage>();
                services.AddSingleton<IFileCompareTemporaryStorage>(
                    provider => provider.GetRequiredService<FailSecondStorage>());
            });
        }
    }

    public sealed class FailSecondStorage(FileCompareTemporaryStorage inner) : IFileCompareTemporaryStorage
    {
        private int _calls;
        public bool FirstDisposed { get; private set; }

        public async Task<TemporaryFileLease> StageUploadAsync(
            Stream content,
            long maxBytes,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 2)
                throw new ApplicationServiceException(400, "第二份文件暂存失败");
            var lease = await inner.StageUploadAsync(content, maxBytes, cancellationToken);
            return new TrackingLease(lease, () => FirstDisposed = true);
        }

        public Task<TemporaryFileLease> CreateOutputAsync(CancellationToken cancellationToken = default) =>
            inner.CreateOutputAsync(cancellationToken);
        public Task CleanupExpiredAsync(CancellationToken cancellationToken = default) =>
            inner.CleanupExpiredAsync(cancellationToken);

        private sealed class TrackingLease(TemporaryFileLease inner, Action disposed) : TemporaryFileLease
        {
            private int _disposed;
            public override long Length => inner.Length;
            public override string Sha256 => inner.Sha256;
            public override Stream OpenRead() => inner.OpenRead();
            public override Stream OpenWrite() => inner.OpenWrite();
            public override async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;
                await inner.DisposeAsync();
                disposed();
            }
        }
    }
}

public sealed class FileCompareLeaseCoverageTests :
    IClassFixture<FileCompareLeaseCoverageTests.SingleParserFactory>
{
    private readonly SingleParserFactory _factory;
    private readonly HttpClient _client;

    public FileCompareLeaseCoverageTests(SingleParserFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Preview_应用服务应等待完整解析Lease并传播等待取消()
    {
        var bytes = CreateWord("same");
        var ids = await UploadAsync(bytes);
        using var scope = _factory.Services.CreateScope();
        var governor = scope.ServiceProvider.GetRequiredService<IResourceBudgetGovernor>();
        var app = scope.ServiceProvider.GetRequiredService<IFileCompareAppService>();
        using var holder = await governor.AcquireAsync(ResourceWorkload.DocumentParsing);
        using var cancellation = new CancellationTokenSource();
        var preview = app.PreviewAsync(
            new SpecAccessContext { UserId = 1, CompanyId = 1, IsAll = true },
            new FileComparePreviewRequest { FileIdA = ids.A, FileIdB = ids.B },
            cancellation.Token);
        await Task.Delay(50);
        preview.IsCompleted.Should().BeFalse();

        cancellation.Cancel();
        Func<Task> wait = async () => await preview;
        await wait.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Upload_表数解析应等待DocumentParsing闸门并传播取消()
    {
        using var scope = _factory.Services.CreateScope();
        var governor = scope.ServiceProvider.GetRequiredService<IResourceBudgetGovernor>();
        using var holder = await governor.AcquireAsync(ResourceWorkload.DocumentParsing);
        using var cancellation = new CancellationTokenSource();
        var bytes = CreateWord("table-count");
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(bytes), "fileA", "a.docx");
        multipart.Add(new ByteArrayContent(bytes), "fileB", "b.docx");

        var upload = _client.PostAsync("/api/file-compare/upload", multipart, cancellation.Token);
        await Task.Delay(100);
        upload.IsCompleted.Should().BeFalse();
        cancellation.Cancel();

        Func<Task> wait = async () => await upload;
        await wait.Should().ThrowAsync<OperationCanceledException>();
    }

    private async Task<(int A, int B)> UploadAsync(byte[] bytes)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(bytes), "fileA", "a.docx");
        multipart.Add(new ByteArrayContent(bytes), "fileB", "b.docx");
        using var response = await _client.PostAsync("/api/file-compare/upload", multipart);
        response.EnsureSuccessStatusCode();
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        return (
            json.Data.GetProperty("fileA").GetProperty("fileId").GetInt32(),
            json.Data.GetProperty("fileB").GetProperty("fileId").GetInt32());
    }

    private static byte[] CreateWord(string text)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(
                   stream, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(new Run(new Text(text)))));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    public sealed class SingleParserFactory : ApiWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ResourceBudgets:MaxConcurrentDocumentParsers"] = "1"
                }));
        }
    }
}
