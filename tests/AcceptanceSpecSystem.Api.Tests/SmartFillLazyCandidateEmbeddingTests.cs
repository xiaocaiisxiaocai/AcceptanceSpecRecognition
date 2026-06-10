using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcceptanceSpecSystem.Api.Tests;

/// <summary>
/// 智能填充候选向量延迟加载回归测试。
/// </summary>
public sealed class SmartFillLazyCandidateEmbeddingTests : IClassFixture<LazyCandidateEmbeddingApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SmartFillLazyCandidateEmbeddingTests(LazyCandidateEmbeddingApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task BatchPreview_WhenAllRowsExactMatch_ShouldNotRequireCandidateEmbeddingHydration()
    {
        var setup = await PrepareExactMatchScenarioAsync("LazyPreviewExact");

        var previewResp = await _client.PostAsync("/api/matching/batch-preview",
            ApiClientJson.ToJsonContent(new
            {
                fileId = setup.FileId,
                customerId = setup.CustomerId,
                processId = setup.ProcessId,
                config = new
                {
                    minScoreThreshold = 0.0,
                    highConfidenceThreshold = 0.95,
                    exactMatchOnly = false
                },
                tables = new[]
                {
                    new
                    {
                        tableIndex = 0,
                        projectColumnIndex = 0,
                        specificationColumnIndex = 1,
                        acceptanceColumnIndex = 2,
                        remarkColumnIndex = 3
                    }
                }
            }));

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        previewJson.Code.Should().Be(0);

        var bestMatch = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0].GetProperty("bestMatch");
        bestMatch.GetProperty("specId").GetInt32().Should().Be(setup.SpecId);
        bestMatch.GetProperty("decision").GetString().Should().Be("autoApply");
    }

    [Fact]
    public async Task BatchExecute_WhenExactMatchNeedsCurrentValidation_ShouldNotRequireCandidateEmbeddingHydration()
    {
        var setup = await PrepareExactMatchScenarioAsync("LazyExecuteExact");

        var executeResp = await _client.PostAsync("/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId = setup.FileId,
                customerId = setup.CustomerId,
                processId = setup.ProcessId,
                config = new
                {
                    minScoreThreshold = 0.0,
                    highConfidenceThreshold = 0.95,
                    exactMatchOnly = false
                },
                tables = new[]
                {
                    new
                    {
                        tableIndex = 0,
                        projectColumnIndex = 0,
                        specificationColumnIndex = 1,
                        acceptanceColumnIndex = 2,
                        remarkColumnIndex = 3,
                        mappings = new[]
                        {
                            new
                            {
                                rowIndex = 1,
                                specId = setup.SpecId
                            }
                        }
                    }
                }
            }));

        executeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var executeJson = await executeResp.ReadAsAsync<ApiResponse<JsonElement>>();
        executeJson.Code.Should().Be(0);
        executeJson.Data.GetProperty("filledCount").GetInt32().Should().Be(1);
        executeJson.Data.GetProperty("skippedCount").GetInt32().Should().Be(0);
    }

    private async Task<(int FileId, int CustomerId, int ProcessId, int SpecId)> PrepareExactMatchScenarioAsync(string prefix)
    {
        var docxBytes = CreateDocxBytes(
            ["项目", "规格", "验收", "备注"],
            ["LAZY-PROJ", "LAZY-SPEC", "", ""]);

        var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(docxBytes), "file", $"{prefix}.docx");
        var uploadResp = await _client.PostAsync("/api/documents/upload", multipart);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var fileId = uploadJson.Data.GetProperty("fileId").GetInt32();

        var customerId = (await (await _client.PostAsync("/api/customers",
            ApiClientJson.ToJsonContent(new { name = $"{prefix}-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();
        var processId = (await (await _client.PostAsync("/api/processes",
            ApiClientJson.ToJsonContent(new { name = $"{prefix}-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var specResp = await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project = "LAZY-PROJ",
            specification = "LAZY-SPEC",
            acceptance = "LAZY-AC",
            remark = "LAZY-REM"
        }));
        specResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var specJson = await specResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var specId = specJson.Data.GetProperty("id").GetInt32();

        return (fileId, customerId, processId, specId);
    }

    private static byte[] CreateDocxBytes(params string[][] rows)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());

            var table = new Table();
            table.AppendChild(new TableProperties(new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 }
            )));

            foreach (var row in rows)
            {
                var tr = new TableRow();
                foreach (var cell in row)
                {
                    tr.AppendChild(new TableCell(new Paragraph(new Run(new Text(cell ?? string.Empty)))
                    {
                        ParagraphProperties = new ParagraphProperties()
                    }));
                }
                table.AppendChild(tr);
            }

            main.Document.Body!.Append(table);
            main.Document.Save();
        }

        return ms.ToArray();
    }
}

public sealed class LazyCandidateEmbeddingApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IEmbeddingService));
            services.AddScoped<IEmbeddingService, ThrowingEmbeddingService>();
        });
    }
}

internal sealed class ThrowingEmbeddingService : IEmbeddingService
{
    public bool IsAvailable => true;

    public Task<float[]> GenerateEmbeddingAsync(
        string text,
        int? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("精确命中路径不应调用单条 Embedding");
    }

    public Task<List<float[]>> GenerateEmbeddingsAsync(
        IEnumerable<string> texts,
        int? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("精确命中路径不应批量生成 Embedding");
    }

    public double ComputeSimilarity(float[] embedding1, float[] embedding2)
    {
        throw new InvalidOperationException("精确命中路径不应计算向量相似度");
    }
}
