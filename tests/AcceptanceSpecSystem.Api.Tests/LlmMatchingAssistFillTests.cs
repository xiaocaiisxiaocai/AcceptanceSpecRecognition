using System.Net;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Data.Context;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

/// <summary>
/// LLM 复核辅助填充集成测试
/// </summary>
public class LlmMatchingAssistFillTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public LlmMatchingAssistFillTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ExecuteFill_WithoutPreviewContext_ShouldReject()
    {
        var (fileId, specId) = await PrepareSingleSpecFillAsync("RejectMissingPreviewContext");

        var execResp = await _client.PostAsync("/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                tables = new[]
                {
                    new
                    {
                        tableIndex = 0,
                        acceptanceColumnIndex = 2,
                        remarkColumnIndex = 3,
                        mappings = new[]
                        {
                            new
                            {
                                rowIndex = 1,
                                specId
                            }
                        }
                    }
                }
            }));

        execResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Code.Should().Be(400);
        execJson.Message.Should().Contain("项目列").And.Contain("规格列");
    }

    [Fact]
    public async Task BatchExecuteFill_WithoutPreviewContext_ShouldReject()
    {
        var (fileId, specId) = await PrepareSingleSpecFillAsync("RejectMissingBatchPreviewContext");

        var execResp = await _client.PostAsync("/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                tables = new[]
                {
                    new
                    {
                        tableIndex = 0,
                        acceptanceColumnIndex = 2,
                        remarkColumnIndex = 3,
                        mappings = new[]
                        {
                            new
                            {
                                rowIndex = 1,
                                specId
                            }
                        }
                    }
                }
            }));

        execResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Code.Should().Be(400);
        execJson.Message.Should().Contain("项目列").And.Contain("规格列");
    }

    [Fact]
    public async Task ExecuteFill_WithOverrideAcceptanceAndRemark_ShouldOnlyAffectExportedFile()
    {
        var setup = await PrepareScopedSingleSpecFillAsync("ApplyExportOverrides");

        var execResp = await _client.PostAsync("/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId = setup.FileId,
                customerId = setup.CustomerId,
                processId = setup.ProcessId,
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
                                specId = setup.SpecId,
                                overrideAcceptance = "UI-AC-OVERRIDE",
                                overrideRemark = "UI-REM-OVERRIDE"
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

        GetCellText(filledBytes, tableIndex: 0, rowIndex: 1, colIndex: 2)
            .Should().Be("UI-AC-OVERRIDE");
        GetCellText(filledBytes, tableIndex: 0, rowIndex: 1, colIndex: 3)
            .Should().Be("UI-REM-OVERRIDE");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var spec = await db.AcceptanceSpecs.FindAsync(setup.SpecId);
        spec.Should().NotBeNull();
        spec!.Acceptance.Should().Be("DB-AC-1");
        spec.Remark.Should().Be("DB-REM-1");
    }

    [Fact]
    public async Task ExecuteFill_WithManualFillWithoutSpecId_ShouldWriteOverridesToExportedFile()
    {
        var setup = await PrepareScopedSingleSpecFillAsync("ApplyManualFillWithoutSpec");

        var execResp = await _client.PostAsync("/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId = setup.FileId,
                customerId = setup.CustomerId,
                processId = setup.ProcessId,
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
                                manualFill = true,
                                overrideAcceptance = "MANUAL-AC",
                                overrideRemark = "MANUAL-REM"
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

        GetCellText(filledBytes, tableIndex: 0, rowIndex: 1, colIndex: 2)
            .Should().Be("MANUAL-AC");
        GetCellText(filledBytes, tableIndex: 0, rowIndex: 1, colIndex: 3)
            .Should().Be("MANUAL-REM");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var spec = await db.AcceptanceSpecs.FindAsync(setup.SpecId);
        spec.Should().NotBeNull();
        spec!.Acceptance.Should().Be("DB-AC-1");
        spec.Remark.Should().Be("DB-REM-1");
    }

    [Fact]
    public async Task ExecuteFill_WithClientAutoApplyOnNonBestSpec_ShouldSkip()
    {
        var setup = await PrepareCompetingSpecsFillAsync("SkipWrongAutoApply");

        var execResp = await _client.PostAsync("/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId = setup.FileId,
                customerId = setup.CustomerId,
                processId = setup.ProcessId,
                config = new
                {
                    minScoreThreshold = 0.0,
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
                                specId = setup.NonBestSpecId
                            }
                        }
                    }
                }
            }));

        execResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Code.Should().Be(0);
        execJson.Data.GetProperty("filledCount").GetInt32().Should().Be(0);
        execJson.Data.GetProperty("skippedCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteFill_WithManualConfirmedOnNonBestSpec_ShouldSkip()
    {
        var setup = await PrepareCompetingSpecsFillAsync("SkipWrongManual");

        var execResp = await _client.PostAsync("/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId = setup.FileId,
                customerId = setup.CustomerId,
                processId = setup.ProcessId,
                config = new
                {
                    minScoreThreshold = 0.0,
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
                                specId = setup.NonBestSpecId,
                                manualConfirmed = true
                            }
                        }
                    }
                }
            }));

        execResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Code.Should().Be(0);
        execJson.Data.GetProperty("filledCount").GetInt32().Should().Be(0);
        execJson.Data.GetProperty("skippedCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteFill_WithForgedHighLlmReviewScore_ShouldStillRequireManualConfirmation()
    {
        var setup = await PrepareAmbiguousSpecsFillAsync("RejectForgedLlmScore");
        var previewBestMatch = await PreviewBestMatchAsync(setup.FileId, setup.CustomerId, setup.ProcessId, rowIndex: 1);

        previewBestMatch.GetProperty("decision").GetString().Should().Be("manualReview");
        previewBestMatch.GetProperty("isAmbiguous").GetBoolean().Should().BeTrue();

        var execResp = await _client.PostAsync("/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId = setup.FileId,
                customerId = setup.CustomerId,
                processId = setup.ProcessId,
                config = new
                {
                    minScoreThreshold = 0.0,
                    recallTopK = 5,
                    ambiguityMargin = 0.05,
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
                                specId = previewBestMatch.GetProperty("specId").GetInt32()
                            }
                        }
                    }
                }
            }));

        execResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Code.Should().Be(0);
        execJson.Data.GetProperty("filledCount").GetInt32().Should().Be(0);
        execJson.Data.GetProperty("skippedCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteFill_WithServerIssuedReviewApprovalToken_ShouldAllowReviewedMatch()
    {
        var setup = await PrepareAmbiguousSpecsFillAsync("ApplyReviewedMatch");
        var previewBestMatch = await PreviewBestMatchAsync(setup.FileId, setup.CustomerId, setup.ProcessId, rowIndex: 1);
        previewBestMatch.GetProperty("decision").GetString().Should().Be("manualReview");

        var reviewApprovalToken = await RequestReviewApprovalTokenAsync(
            setup.CustomerId,
            setup.ProcessId,
            rowIndex: 1,
            sourceProject: ReviewScenarioSamples.ApprovedSourceProject,
            sourceSpecification: ReviewScenarioSamples.ApprovedSourceSpecification,
            previewBestMatch);

        var execResp = await _client.PostAsync("/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId = setup.FileId,
                customerId = setup.CustomerId,
                processId = setup.ProcessId,
                config = new
                {
                    minScoreThreshold = 0.0,
                    recallTopK = 1,
                    ambiguityMargin = 0.0,
                    useLlmEntityResolution = false
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
                                specId = previewBestMatch.GetProperty("specId").GetInt32(),
                                reviewApprovalToken
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
    }

    [Fact]
    public async Task ExecuteFill_WhenReviewedSpecContentChangesAfterTokenIssued_ShouldRejectStaleReviewApprovalToken()
    {
        var setup = await PrepareAmbiguousSpecsFillAsync("RejectStaleReviewTokenAfterSpecChange");
        var previewBestMatch = await PreviewBestMatchAsync(setup.FileId, setup.CustomerId, setup.ProcessId, rowIndex: 1);
        previewBestMatch.GetProperty("decision").GetString().Should().Be("manualReview");

        var reviewApprovalToken = await RequestReviewApprovalTokenAsync(
            setup.CustomerId,
            setup.ProcessId,
            rowIndex: 1,
            sourceProject: ReviewScenarioSamples.ApprovedSourceProject,
            sourceSpecification: ReviewScenarioSamples.ApprovedSourceSpecification,
            previewBestMatch);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var specId = previewBestMatch.GetProperty("specId").GetInt32();
            var spec = await db.AcceptanceSpecs.FindAsync(specId);
            spec.Should().NotBeNull();
            spec!.Remark = "TOKEN-DRIFTED-REMARK";
            await db.SaveChangesAsync();
        }

        var execResp = await _client.PostAsync("/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId = setup.FileId,
                customerId = setup.CustomerId,
                processId = setup.ProcessId,
                config = new
                {
                    minScoreThreshold = 0.0,
                    recallTopK = 1,
                    ambiguityMargin = 0.0,
                    useLlmEntityResolution = false
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
                                specId = previewBestMatch.GetProperty("specId").GetInt32(),
                                reviewApprovalToken
                            }
                        }
                    }
                }
            }));

        execResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Code.Should().Be(0);
        execJson.Data.GetProperty("filledCount").GetInt32().Should().Be(0);
        execJson.Data.GetProperty("skippedCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task BatchExecuteFill_WithForgedHighLlmReviewScore_ShouldStillRequireManualConfirmation()
    {
        var setup = await PrepareAmbiguousSpecsFillAsync("RejectForgedBatchLlmScore");
        var previewBestMatch = await PreviewBestMatchAsync(setup.FileId, setup.CustomerId, setup.ProcessId, rowIndex: 1);

        previewBestMatch.GetProperty("decision").GetString().Should().Be("manualReview");
        previewBestMatch.GetProperty("isAmbiguous").GetBoolean().Should().BeTrue();

        var execResp = await _client.PostAsync("/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId = setup.FileId,
                customerId = setup.CustomerId,
                processId = setup.ProcessId,
                config = new
                {
                    minScoreThreshold = 0.0,
                    recallTopK = 5,
                    ambiguityMargin = 0.05,
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
                                specId = previewBestMatch.GetProperty("specId").GetInt32()
                            }
                        }
                    }
                }
            }));

        execResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Code.Should().Be(0);
        execJson.Data.GetProperty("filledCount").GetInt32().Should().Be(0);
        execJson.Data.GetProperty("skippedCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task BatchExecuteFill_WithForgedPreviewTablesAutoApply_ShouldStillRequireServerCurrentMatch()
    {
        var setup = await PrepareAmbiguousSpecsFillAsync("RejectForgedPreviewTablesAutoApply");
        var previewBestMatch = await PreviewBestMatchAsync(setup.FileId, setup.CustomerId, setup.ProcessId, rowIndex: 1);

        previewBestMatch.GetProperty("decision").GetString().Should().Be("manualReview");
        previewBestMatch.GetProperty("isAmbiguous").GetBoolean().Should().BeTrue();

        var execResp = await _client.PostAsync("/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId = setup.FileId,
                customerId = setup.CustomerId,
                processId = setup.ProcessId,
                config = new
                {
                    minScoreThreshold = 0.0,
                    recallTopK = 5,
                    ambiguityMargin = 0.05,
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
                                specId = previewBestMatch.GetProperty("specId").GetInt32()
                            }
                        }
                    }
                },
                previewTables = new[]
                {
                    new
                    {
                        tableIndex = 0,
                        items = new[]
                        {
                            new
                            {
                                rowIndex = 1,
                                sourceProject = ReviewScenarioSamples.ApprovedSourceProject,
                                sourceSpecification = ReviewScenarioSamples.ApprovedSourceSpecification,
                                bestMatch = new
                                {
                                    specId = previewBestMatch.GetProperty("specId").GetInt32(),
                                    project = previewBestMatch.GetProperty("project").GetString(),
                                    specification = previewBestMatch.GetProperty("specification").GetString(),
                                    acceptance = previewBestMatch.GetProperty("acceptance").GetString(),
                                    remark = previewBestMatch.GetProperty("remark").GetString(),
                                    score = previewBestMatch.GetProperty("score").GetDouble(),
                                    embeddingScore = previewBestMatch.GetProperty("embeddingScore").GetDouble(),
                                    decision = "autoApply",
                                    isAmbiguous = false,
                                    llmEquivalence = new
                                    {
                                        verdict = "equivalent",
                                        reasonType = "equivalent_expression",
                                        confidence = 0.99,
                                        reason = "客户端伪造的等价结论"
                                    }
                                },
                                confidenceLevel = "high"
                            }
                        }
                    }
                }
            }));

        execResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Code.Should().Be(0);
        execJson.Data.GetProperty("filledCount").GetInt32().Should().Be(0);
        execJson.Data.GetProperty("skippedCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteFill_WithDuplicateRowMappings_ShouldReturnBadRequest()
    {
        var setup = await PrepareCompetingSpecsFillAsync("RejectDuplicateExecuteMappings");

        var execResp = await _client.PostAsync("/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId = setup.FileId,
                customerId = setup.CustomerId,
                processId = setup.ProcessId,
                config = new
                {
                    minScoreThreshold = 0.0,
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
                                specId = setup.BestSpecId
                            },
                            new
                            {
                                rowIndex = 1,
                                specId = setup.BestSpecId
                            }
                        }
                    },
                }
            }));

        execResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Code.Should().Be(400);
    }

    [Fact]
    public async Task BatchExecuteFill_WithDuplicateRowMappingsInSameTable_ShouldReturnBadRequest()
    {
        var setup = await PrepareCompetingSpecsFillAsync("RejectDuplicateBatchExecuteMappings");

        var execResp = await _client.PostAsync("/api/matching/batch-execute",
            ApiClientJson.ToJsonContent(new
            {
                fileId = setup.FileId,
                customerId = setup.CustomerId,
                processId = setup.ProcessId,
                config = new
                {
                    minScoreThreshold = 0.0,
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
                                specId = setup.BestSpecId
                            },
                            new
                            {
                                rowIndex = 1,
                                specId = setup.BestSpecId
                            }
                        }
                    }
                }
            }));

        execResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var execJson = await execResp.ReadAsAsync<ApiResponse<JsonElement>>();
        execJson.Code.Should().Be(400);
    }

    [Fact]
    public async Task LlmStream_WithDuplicateTableRowItems_ShouldReturnBadRequest()
    {
        var setup = await PrepareAmbiguousSpecsFillAsync("RejectDuplicateLlmStreamItems");
        var previewBestMatch = await PreviewBestMatchAsync(setup.FileId, setup.CustomerId, setup.ProcessId, rowIndex: 1);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matching/llm-stream")
        {
            Content = ApiClientJson.ToJsonContent(new
            {
                customerId = setup.CustomerId,
                processId = setup.ProcessId,
                items = new[]
                {
                    new
                    {
                        tableIndex = 0,
                        rowIndex = 1,
                        sourceProject = ReviewScenarioSamples.ApprovedSourceProject,
                        sourceSpecification = ReviewScenarioSamples.ApprovedSourceSpecification,
                        bestMatchSpecId = previewBestMatch.GetProperty("specId").GetInt32(),
                        bestMatchScore = previewBestMatch.GetProperty("score").GetDouble(),
                        scoreDetails = previewBestMatch.GetProperty("scoreDetails"),
                        decision = previewBestMatch.GetProperty("decision").GetString(),
                        llmEquivalenceVerdict = GetLlmEquivalenceVerdict(previewBestMatch),
                        isAmbiguous = previewBestMatch.GetProperty("isAmbiguous").GetBoolean(),
                        evidenceSummary = previewBestMatch.GetProperty("evidenceSummary"),
                        conflictSummary = previewBestMatch.GetProperty("conflictSummary")
                    },
                    new
                    {
                        tableIndex = 0,
                        rowIndex = 1,
                        sourceProject = ReviewScenarioSamples.ApprovedSourceProject,
                        sourceSpecification = ReviewScenarioSamples.ApprovedSourceSpecification,
                        bestMatchSpecId = previewBestMatch.GetProperty("specId").GetInt32(),
                        bestMatchScore = previewBestMatch.GetProperty("score").GetDouble(),
                        scoreDetails = previewBestMatch.GetProperty("scoreDetails"),
                        decision = previewBestMatch.GetProperty("decision").GetString(),
                        llmEquivalenceVerdict = GetLlmEquivalenceVerdict(previewBestMatch),
                        isAmbiguous = previewBestMatch.GetProperty("isAmbiguous").GetBoolean(),
                        evidenceSummary = previewBestMatch.GetProperty("evidenceSummary"),
                        conflictSummary = previewBestMatch.GetProperty("conflictSummary")
                    }
                }
            })
        };

        using var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        json.Code.Should().Be(400);
    }

    #region Helpers

    private async Task<(int FileId, int SpecId)> PrepareSingleSpecFillAsync(string prefix)
    {
        var docxBytes = CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" }
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

        var specResp = await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project = "P1",
            specification = "S1",
            acceptance = "DB-AC-1",
            remark = "DB-REM-1"
        }));
        var specId = (await specResp.ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        return (fileId, specId);
    }

    private async Task<(int FileId, int CustomerId, int ProcessId, int SpecId)> PrepareScopedSingleSpecFillAsync(string prefix)
    {
        var docxBytes = CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "P1", "S1", "", "" }
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

        var specResp = await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project = "P1",
            specification = "S1",
            acceptance = "DB-AC-1",
            remark = "DB-REM-1"
        }));
        var specId = (await specResp.ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        return (fileId, customerId, processId, specId);
    }

    private async Task<(int FileId, int CustomerId, int ProcessId, int BestSpecId, int NonBestSpecId)> PrepareCompetingSpecsFillAsync(string prefix)
    {
        var docxBytes = CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { "项目A", "规格A", "", "" }
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

        var bestSpecResp = await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project = "项目A",
            specification = "规格A",
            acceptance = "BEST-AC",
            remark = "BEST-REM"
        }));
        var bestSpecId = (await bestSpecResp.ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var nonBestSpecResp = await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project = "项目A",
            specification = "规格B",
            acceptance = "ALT-AC",
            remark = "ALT-REM"
        }));
        var nonBestSpecId = (await nonBestSpecResp.ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        return (fileId, customerId, processId, bestSpecId, nonBestSpecId);
    }

    private async Task<(int FileId, int CustomerId, int ProcessId)> PrepareAmbiguousSpecsFillAsync(string prefix)
    {
        var docxBytes = CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { ReviewScenarioSamples.ApprovedSourceProject, ReviewScenarioSamples.ApprovedSourceSpecification, "", "" }
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

        await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project = ReviewScenarioSamples.ApprovedBestProject,
            // 用连字符/空格变体制造“AI可判等价，但仍需靠歧义复核”的场景，
            // 避免被项目+规格精确直达短路，同时保留 review token 的放行路径。
            specification = ReviewScenarioSamples.ApprovedBestSpecification,
            acceptance = "验收版本-1",
            remark = "R1"
        }));

        await _client.PostAsync("/api/specs", ApiClientJson.ToJsonContent(new
        {
            customerId,
            processId,
            project = ReviewScenarioSamples.ApprovedAltProject,
            specification = ReviewScenarioSamples.ApprovedAltBestSpecification,
            acceptance = "验收版本-2",
            remark = "R2"
        }));

        return (fileId, customerId, processId);
    }

    private async Task<JsonElement> PreviewBestMatchAsync(int fileId, int customerId, int processId, int rowIndex)
    {
        var previewResp = await _client.PostAsync("/api/matching/batch-preview",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                customerId,
                processId,
                config = new
                {
                    minScoreThreshold = 0.0,
                    recallTopK = 5,
                    ambiguityMargin = 0.05
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

        var item = previewJson.Data.GetProperty("tables")[0].GetProperty("items")
            .EnumerateArray()
            .Single(entry => entry.GetProperty("rowIndex").GetInt32() == rowIndex);

        return item.GetProperty("bestMatch");
    }

    private async Task<string> RequestReviewApprovalTokenAsync(
        int customerId,
        int processId,
        int rowIndex,
        string sourceProject,
        string sourceSpecification,
        JsonElement previewBestMatch)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/matching/llm-stream")
        {
            Content = ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                items = new[]
                {
                    new
                    {
                        tableIndex = 0,
                        rowIndex,
                        sourceProject,
                        sourceSpecification,
                        bestMatchSpecId = previewBestMatch.GetProperty("specId").GetInt32(),
                        bestMatchScore = previewBestMatch.GetProperty("score").GetDouble(),
                        scoreDetails = previewBestMatch.GetProperty("scoreDetails"),
                        decision = previewBestMatch.GetProperty("decision").GetString(),
                        llmEquivalenceVerdict = GetLlmEquivalenceVerdict(previewBestMatch),
                        isAmbiguous = previewBestMatch.GetProperty("isAmbiguous").GetBoolean(),
                        evidenceSummary = previewBestMatch.GetProperty("evidenceSummary"),
                        conflictSummary = previewBestMatch.GetProperty("conflictSummary")
                    }
                }
            })
        };

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await ReadSseEventsAsync(response);
        var reviewDone = events.First(e => e.Event == "review.done").Data;
        reviewDone.GetProperty("decision").GetString().Should().Be("autoApply");
        reviewDone.TryGetProperty("reviewApprovalToken", out var tokenElement).Should().BeTrue();
        tokenElement.GetString().Should().NotBeNullOrWhiteSpace();
        return tokenElement.GetString()!;
    }

    private static string? GetLlmEquivalenceVerdict(JsonElement previewBestMatch)
    {
        if (!previewBestMatch.TryGetProperty("llmEquivalence", out var llmEquivalence) ||
            llmEquivalence.ValueKind != JsonValueKind.Object ||
            !llmEquivalence.TryGetProperty("verdict", out var verdict))
        {
            return null;
        }

        return verdict.GetString();
    }

    private static async Task<List<SseEvent>> ReadSseEventsAsync(HttpResponseMessage response)
    {
        var events = new List<SseEvent>();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        string? eventName = null;
        var dataBuilder = new StringBuilder();

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                eventName = line.Replace("event:", "", StringComparison.OrdinalIgnoreCase).Trim();
            }
            else if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                dataBuilder.Append(line.Replace("data:", "", StringComparison.OrdinalIgnoreCase).Trim());
            }
            else if (line.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(eventName) && dataBuilder.Length > 0)
                {
                    using var doc = JsonDocument.Parse(dataBuilder.ToString());
                    events.Add(new SseEvent(eventName!, doc.RootElement.Clone()));
                }

                eventName = null;
                dataBuilder.Clear();
            }
        }

        return events;
    }

    private record SseEvent(string Event, JsonElement Data);

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
        var allTables = body.Descendants<Table>().ToList();
        var table = allTables[tableIndex];
        var row = table.Elements<TableRow>().ToList()[rowIndex];
        var cell = row.Elements<TableCell>().ToList()[colIndex];
        return cell.InnerText ?? string.Empty;
    }

    #endregion
}
