using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Application.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
