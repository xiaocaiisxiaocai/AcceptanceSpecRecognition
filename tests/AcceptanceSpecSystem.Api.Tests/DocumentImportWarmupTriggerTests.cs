using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcceptanceSpecSystem.Api.Tests;

public class DocumentImportWarmupTriggerTests : IClassFixture<DocumentImportWarmupTriggerTests.WarmupTriggerFactory>
{
    private readonly WarmupTriggerFactory _factory;
    private readonly HttpClient _client;

    public DocumentImportWarmupTriggerTests(WarmupTriggerFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Import_WhenRowsAreCommitted_ShouldOnlySubmitWarmupTrigger()
    {
        var customerResponse = await _client.PostAsync(
            "/api/customers",
            ApiClientJson.ToJsonContent(new { name = $"导入预热客户-{Guid.NewGuid():N}" }));
        customerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var customerBody = await customerResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var customerId = customerBody.Data.GetProperty("id").GetInt32();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("验收表");
        worksheet.Cell(1, 1).Value = "项目";
        worksheet.Cell(1, 2).Value = "规格";
        worksheet.Cell(1, 3).Value = "验收";
        worksheet.Cell(2, 1).Value = "外观";
        worksheet.Cell(2, 2).Value = "不得有划伤";
        worksheet.Cell(2, 3).Value = "OK";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var fileId = await SmartConfigRecognizeTestFiles.UploadExcelAsync(
            _client,
            stream.ToArray(),
            $"import-warmup-{Guid.NewGuid():N}.xlsx");

        var importResponse = await _client.PostAsync(
            "/api/documents/excel/import",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                sheetIndex = 0,
                customerId,
                headerRowStart = 1,
                headerRowCount = 1,
                dataStartRow = 2,
                projectColumn = 1,
                specificationColumn = 2,
                acceptanceColumn = 3,
                cleanupSourceFile = false
            }));
        var responseText = await importResponse.Content.ReadAsStringAsync();

        importResponse.StatusCode.Should().Be(HttpStatusCode.OK, responseText);
        _factory.Trigger.RequestCount.Should().Be(1);
    }

    public sealed class WarmupTriggerFactory : ApiWebApplicationFactory
    {
        public RecordingWarmupTrigger Trigger { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmbeddingCacheWarmupTrigger>();
                services.AddSingleton<IEmbeddingCacheWarmupTrigger>(Trigger);
            });
        }
    }

    public sealed class RecordingWarmupTrigger : IEmbeddingCacheWarmupTrigger
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        public bool Request()
        {
            Interlocked.Increment(ref _requestCount);
            return true;
        }

        public async ValueTask WaitAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
