using System.Net;
using System.Text.Json;
using System.Threading;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MatchingSource = AcceptanceSpecSystem.Core.Matching.Models.MatchSource;

namespace AcceptanceSpecSystem.Api.Tests;

/// <summary>
/// 预览与执行一致性回归测试。
/// </summary>
public class AutoApplyPreviewConsistencyTests : IClassFixture<AutoApplyPreviewConsistencyApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AutoApplyPreviewConsistencyTests(AutoApplyPreviewConsistencyApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task BatchExecute_WhenAutoApplyPreviewSelectionDrifts_ShouldStillApplyPreviewSelectedSpec()
    {
        var setup = await PrepareDriftScenarioAsync("AutoApplyPreviewConsistency");

        var previewResp = await _client.PostAsync("/api/matching/batch-preview",
            ApiClientJson.ToJsonContent(new
            {
                fileId = setup.FileId,
                customerId = setup.CustomerId,
                processId = setup.ProcessId,
                config = new
                {
                    minScoreThreshold = 0.0,
                    recallTopK = 2,
                    highConfidenceThreshold = 0.95
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

        var bestMatch = previewJson.Data
            .GetProperty("tables")[0]
            .GetProperty("items")[0]
            .GetProperty("bestMatch");

        bestMatch.GetProperty("specId").GetInt32().Should().Be(setup.PrimarySpecId);
        bestMatch.TryGetProperty("reviewApprovalToken", out var tokenElement).Should().BeTrue();
        var previewToken = tokenElement.GetString();
        previewToken.Should().NotBeNullOrWhiteSpace();

        var execResp = await _client.PostAsync("/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId = setup.FileId,
                customerId = setup.CustomerId,
                processId = setup.ProcessId,
                config = new
                {
                    minScoreThreshold = 0.0,
                    recallTopK = 2,
                    highConfidenceThreshold = 0.95
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
                                specId = setup.PrimarySpecId,
                                reviewApprovalToken = previewToken
                            }
                        }
                    }
                }
            }));

        execResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Code.Should().Be(0);
        execJson.Data.GetProperty("filledCount").GetInt32().Should().Be(1);
        execJson.Data.GetProperty("skippedCount").GetInt32().Should().Be(0);

        var taskId = execJson.Data.GetProperty("taskId").GetString();
        taskId.Should().NotBeNullOrWhiteSpace();

        var downloadResp = await _client.GetAsync($"/api/matching/download/{taskId}");
        downloadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var filledBytes = await downloadResp.Content.ReadAsByteArrayAsync();

        GetCellText(filledBytes, 0, 1, 2).Should().Be("PRIMARY-AC");
        GetCellText(filledBytes, 0, 1, 3).Should().Be("PRIMARY-REM");
    }

    private async Task<(int FileId, int CustomerId, int ProcessId, int PrimarySpecId)> PrepareDriftScenarioAsync(string prefix)
    {
        var docxBytes = CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "项目A", "规格A", string.Empty, string.Empty }
        });

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

        var primarySpecResp = await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project = "项目A",
            specification = "规格A",
            acceptance = "PRIMARY-AC",
            remark = "PRIMARY-REM"
        }));
        var primarySpecId = (await primarySpecResp.ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var secondarySpecResp = await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project = "项目A",
            specification = "规格A-漂移候选",
            acceptance = "SECONDARY-AC",
            remark = "SECONDARY-REM"
        }));
        secondarySpecResp.StatusCode.Should().Be(HttpStatusCode.OK);

        return (fileId, customerId, processId, primarySpecId);
    }

    private static byte[] CreateDocxBytes(string[][] rows)
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

    private static string GetCellText(byte[] docx, int tableIndex, int rowIndex, int colIndex)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        var table = body.Descendants<Table>().ToList()[tableIndex];
        var row = table.Elements<TableRow>().ToList()[rowIndex];
        var cell = row.Elements<TableCell>().ToList()[colIndex];
        return cell.InnerText ?? string.Empty;
    }
}

public sealed class AutoApplyPreviewConsistencyApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IMatchingService));
            services.AddSingleton<IMatchingService, DriftBetweenPreviewAndExecuteMatchingService>();
        });
    }
}

internal sealed class DriftBetweenPreviewAndExecuteMatchingService : IMatchingService
{
    private int _batchCallCount;

    public Task<List<MatchResult>> FindMatchesAsync(
        MatchingSource source,
        IEnumerable<MatchCandidate> candidates,
        MatchingConfig? config = null)
    {
        return BatchMatchAsync(new[] { source }, candidates, config)
            .ContinueWith(task => task.Result.Results);
    }

    public Task<BatchMatchResult> BatchMatchAsync(
        IEnumerable<MatchingSource> sources,
        IEnumerable<MatchCandidate> candidates,
        MatchingConfig? config = null,
        IProgress<BatchMatchProgress>? progress = null)
    {
        var sourceList = sources.ToList();
        var orderedCandidates = candidates.OrderBy(c => c.SpecId).ToList();
        var callIndex = Interlocked.Increment(ref _batchCallCount);
        var selected = callIndex == 1
            ? orderedCandidates.First()
            : orderedCandidates.Last();

        var results = sourceList.Select(source => new MatchResult
        {
            SourceText = source.CombinedText,
            MatchedText = selected.CombinedText,
            MatchedSpecId = selected.SpecId,
            MatchedProject = selected.Project,
            MatchedSpecification = selected.Specification,
            MatchedAcceptance = selected.Acceptance,
            MatchedRemark = selected.Remark,
            Score = 0.99,
            EmbeddingScore = 0.99,
            ScoreDetails = new Dictionary<string, double>
            {
                ["Embedding"] = 0.99,
                ["ProjectMatch"] = 1,
                ["SpecificationText"] = 1
            },
            TopCandidates = orderedCandidates.Select((candidate, index) => new MatchCandidateSnapshot
            {
                Rank = index + 1,
                SpecId = candidate.SpecId,
                Project = candidate.Project,
                Specification = candidate.Specification,
                Acceptance = candidate.Acceptance,
                Remark = candidate.Remark,
                Score = candidate.SpecId == selected.SpecId ? 0.99 : 0.97,
                EmbeddingScore = candidate.SpecId == selected.SpecId ? 0.99 : 0.97
            }).ToList(),
            RecalledCandidateCount = orderedCandidates.Count,
            LlmEquivalence = new LlmEquivalenceAdjudicationResult
            {
                Verdict = LlmEquivalenceVerdict.Equivalent,
                ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
                Confidence = 0.99,
                Reason = "测试桩：预览与执行故意漂移"
            },
            Decision = MatchDecision.AutoApply,
            HighConfidenceThreshold = config?.HighConfidenceThreshold ?? MatchingThresholds.DefaultHighConfidenceScore
        }).ToList();

        return Task.FromResult(new BatchMatchResult
        {
            Results = results
        });
    }
}
