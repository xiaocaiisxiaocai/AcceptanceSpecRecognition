using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public class EvidenceDrivenMatchingApiTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public EvidenceDrivenMatchingApiTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Preview_ShouldExposeManualReviewWithDeterministicNumericConflict()
    {
        var (customerId, processId) = await CreateScopeAsync("EvidenceApi");

        await CreateSpecAsync(customerId, processId, "尺寸要求", "宽度等于0.7cm", "RISKY");
        await CreateSpecAsync(customerId, processId, "尺寸要求", "宽度等于0.2cm", "SAFE");

        var previewResp = await PostSingleTableBatchPreviewAsync(
            "尺寸要求",
            "宽度小于0.5cm",
            customerId,
            processId,
            new
            {
                minScoreThreshold = 0.0,
                recallTopK = 5,
                ambiguityMargin = 0.01,
                enableLlmEquivalenceAdjudication = true
            });

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var bestMatch = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0].GetProperty("bestMatch");
        var item = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0];

        bestMatch.GetProperty("acceptance").GetString().Should().Be("RISKY");
        bestMatch.GetProperty("decision").GetString().Should().Be("manualReview");
        bestMatch.TryGetProperty("matchingStrategy", out _).Should().BeFalse();
        bestMatch.TryGetProperty("hasHardConflict", out _).Should().BeFalse();
        bestMatch.GetProperty("evidenceSummary").GetArrayLength().Should().Be(0);
        bestMatch.GetProperty("conflictSummary").GetArrayLength().Should().BeGreaterThan(0);
        bestMatch.GetProperty("issues").EnumerateArray().Should().Contain(issue =>
            issue.GetProperty("code").GetString() == "numeric_unit_conflict" &&
            issue.GetProperty("severity").GetString() == "hard_conflict");
        bestMatch.GetProperty("llmEquivalence").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("confidenceLevel").GetString().Should().Be("medium");
    }

    [Fact]
    public async Task Preview_WhenOnlyRuleConflictExists_ShouldReturnManualReviewWithLocalConflictSummary()
    {
        var (customerId, processId) = await CreateScopeAsync("EvidenceRejectApi");

        await CreateSpecAsync(customerId, processId, "尺寸要求", "宽度等于0.7cm", "RISKY");

        var previewResp = await PostSingleTableBatchPreviewAsync(
            "尺寸要求",
            "宽度小于0.5cm",
            customerId,
            processId,
            new
            {
                minScoreThreshold = 0.0,
                recallTopK = 5,
                ambiguityMargin = 0.01
            });

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var item = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0];
        var bestMatch = item.GetProperty("bestMatch");

        bestMatch.GetProperty("decision").GetString().Should().Be("manualReview");
        bestMatch.TryGetProperty("hasHardConflict", out _).Should().BeFalse();
        bestMatch.GetProperty("conflictSummary").GetArrayLength().Should().BeGreaterThan(0);
        bestMatch.GetProperty("issues").EnumerateArray().Should().Contain(issue =>
            issue.GetProperty("code").GetString() == "numeric_unit_conflict" &&
            issue.GetProperty("severity").GetString() == "hard_conflict");
        item.GetProperty("confidenceLevel").GetString().Should().Be("medium");
    }

    [Fact]
    public async Task Preview_WhenScoreBelowHighConfidenceThreshold_ShouldExposeManualReviewAndMediumConfidence()
    {
        var (customerId, processId) = await CreateScopeAsync("EvidenceThresholdApi");

        await CreateSpecAsync(
            customerId,
            processId,
            "设备安装需求",
            "设备供应商在到厂前提供设备的空压位置及流量要求",
            "NEAR");

        var previewResp = await PostSingleTableBatchPreviewAsync(
            "设备安装需求",
            "设备供应商在到厂前提供设备的空压位置大小及流量",
            customerId,
            processId,
            new
            {
                minScoreThreshold = 0.0,
                recallTopK = 5,
                ambiguityMargin = 0.01,
                highConfidenceThreshold = 0.95
            });

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var item = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0];
        var bestMatch = item.GetProperty("bestMatch");

        bestMatch.GetProperty("decision").GetString().Should().Be("manualReview");
        item.GetProperty("confidenceLevel").GetString().Should().Be("medium");
    }

    [Fact]
    public async Task Preview_WhenSpecificationExactlyMatchesWithoutKeywordTokens_ShouldExposeHighConfidence()
    {
        var (customerId, processId) = await CreateScopeAsync("ExactShortSpecApi");

        await CreateSpecAsync(customerId, processId, "设备交货时间", "<80天;", "OK");

        var previewResp = await PostSingleTableBatchPreviewAsync(
            "设备交货时间",
            "<80天;",
            customerId,
            processId,
            new
            {
                minScoreThreshold = 0.0,
                recallTopK = 3,
                highConfidenceThreshold = 0.98
            });

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var item = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0];
        var bestMatch = item.GetProperty("bestMatch");

        bestMatch.GetProperty("scoreDetails").TryGetProperty("KeywordOverlap", out _).Should().BeFalse();
        bestMatch.GetProperty("score").GetDouble().Should().Be(1.0);
        bestMatch.GetProperty("decision").GetString().Should().Be("autoApply");
        item.GetProperty("confidenceLevel").GetString().Should().Be("high");
    }

    [Fact]
    public async Task Preview_ShouldExposeDeterministicVoltageConflictIssue()
    {
        var (customerId, processId) = await CreateScopeAsync("IssueApi");

        await CreateSpecAsync(customerId, processId, "电压要求", "电压等于2.4V", "NG");

        var previewResp = await PostSingleTableBatchPreviewAsync(
            "电压要求",
            "电压等于24V",
            customerId,
            processId,
            new
            {
                minScoreThreshold = 0.0,
                recallTopK = 3,
                enableLlmEquivalenceAdjudication = true
            });

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var bestMatch = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0].GetProperty("bestMatch");
        var issues = bestMatch.GetProperty("issues");

        issues.EnumerateArray().Should().Contain(issue =>
            issue.GetProperty("code").GetString() == "numeric_unit_conflict" &&
            issue.GetProperty("severity").GetString() == "hard_conflict");
        bestMatch.GetProperty("llmEquivalence").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Preview_WhenVoltageAlternativesContainTypo_ShouldExposeLocalNumericConflictWithoutLlm()
    {
        var (customerId, processId) = await CreateScopeAsync("VoltageAlternativeIssueApi");

        await CreateSpecAsync(
            customerId,
            processId,
            "水/电/气",
            "电力规格要求: 380V三相/50HZ或220V/50HZ；气压需求≤6kg/cm3",
            "NG");

        var previewResp = await PostSingleTableBatchPreviewAsync(
            "水/电/气",
            "电力规格要求: 380V三相/50HZ或22V/50HZ；气压需求≤6kg/cm3",
            customerId,
            processId,
            new
            {
                minScoreThreshold = 0.0,
                recallTopK = 3,
                enableLlmEquivalenceAdjudication = true
            });

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var bestMatch = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0].GetProperty("bestMatch");
        var issues = bestMatch.GetProperty("issues");

        issues.EnumerateArray().Should().Contain(issue =>
            issue.GetProperty("code").GetString() == "numeric_unit_conflict" &&
            issue.GetProperty("severity").GetString() == "hard_conflict");
        bestMatch.GetProperty("llmEquivalence").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Preview_WhenRequestCarriesLegacyEntityResolutionFlag_ShouldIgnoreItAndNotExposeEntityIssues()
    {
        var (customerId, processId) = await CreateScopeAsync("EntityIssueApi");

        await CreateSpecAsync(customerId, processId, "设备要求", "BetaMotion 设备需安装防护罩", "NG");

        var previewResp = await PostSingleTableBatchPreviewAsync(
            "设备要求",
            "AlphaTech 设备需安装防护罩",
            customerId,
            processId,
            new
            {
                minScoreThreshold = 0.0,
                recallTopK = 3,
                useLlmEntityResolution = true
            });

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var bestMatch = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0].GetProperty("bestMatch");
        var issues = bestMatch.GetProperty("issues");

        issues.EnumerateArray().Should().NotContain(issue =>
            string.Equals(issue.GetProperty("code").GetString(), "entity_conflict", StringComparison.Ordinal) ||
            string.Equals(issue.GetProperty("code").GetString(), "entity_conflict_suspected", StringComparison.Ordinal) ||
            string.Equals(issue.GetProperty("code").GetString(), "entity_unknown", StringComparison.Ordinal));
        if (bestMatch.TryGetProperty("entities", out var entities))
        {
            entities.GetArrayLength().Should().Be(0);
        }
    }

    [Fact]
    public async Task Preview_WhenLlmEquivalenceReturnsEquivalent_ShouldExposeEquivalenceAndHighConfidence()
    {
        var (customerId, processId) = await CreateScopeAsync("EquivalenceApi");

        await CreateSpecAsync(customerId, processId, "安装要求", "最大不可拆部件约等于3200。", "OK");

        var previewResp = await PostSingleTableBatchPreviewAsync(
            "安装要求",
            "最大不可拆部件≈3200",
            customerId,
            processId,
            new
            {
                minScoreThreshold = 0.0,
                recallTopK = 3,
                highConfidenceThreshold = 0.98,
                enableLlmEquivalenceAdjudication = true
            });

        previewResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewJson = await previewResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var item = previewJson.Data.GetProperty("tables")[0].GetProperty("items")[0];
        var bestMatch = item.GetProperty("bestMatch");
        var llmEquivalence = bestMatch.GetProperty("llmEquivalence");
        var topCandidateEquivalence = bestMatch
            .GetProperty("topCandidates")[0]
            .GetProperty("llmEquivalence");

        bestMatch.GetProperty("decision").GetString().Should().Be("autoApply");
        item.GetProperty("confidenceLevel").GetString().Should().Be("high");
        llmEquivalence.GetProperty("verdict").GetString().Should().Be("equivalent");
        llmEquivalence.GetProperty("reasonType").GetString().Should().Be("equivalent_expression");
        llmEquivalence.GetProperty("reason").GetString().Should().Contain("同义表达");
        topCandidateEquivalence.GetProperty("verdict").GetString().Should().Be("equivalent");
    }

    [Fact]
    public void MatchConfigDto_ShouldNotExposeRemovedLlmEntityResolutionSettings()
    {
        var config = new MatchConfigDto();

        config.MinScoreThreshold.Should().Be(0.9);
        config.HighConfidenceThreshold.Should().Be(0.95);
        config.AmbiguityMargin.Should().Be(0.02);
        config.LlmParallelism.Should().Be(4);
        config.EnableDeterministicAutoApply.Should().BeTrue();
        config.LlmMaxCallsPerBatch.Should().Be(1000);
        typeof(MatchConfigDto).GetProperty("UseLlmEntityResolution").Should().BeNull();
        typeof(MatchConfigDto).GetProperty("LlmEntityResolutionTopCandidates").Should().BeNull();
        typeof(MatchConfigDto).GetProperty("LlmEntityPositiveConfidenceThreshold").Should().BeNull();
        typeof(MatchConfigDto).GetProperty("LlmEntityConflictReviewConfidenceThreshold").Should().BeNull();
        typeof(MatchConfigDto).GetProperty("LlmEntityConflictRejectConfidenceThreshold").Should().BeNull();
    }

    private async Task<(int customerId, int processId)> CreateScopeAsync(string prefix)
    {
        var customerId = (await (await _client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = $"{prefix}-C" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var processId = (await (await _client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = $"{prefix}-P" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        return (customerId, processId);
    }

    private async Task CreateSpecAsync(int customerId, int processId, string project, string specification, string acceptance)
    {
        var specResp = await _client.PostAsync(
            "/api/specs",
            ApiClientJson.ToJsonContent(new
            {
                customerId,
                processId,
                project,
                specification,
                acceptance,
                remark = "R"
            }));

        specResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> PostSingleTableBatchPreviewAsync(
        string project,
        string specification,
        int customerId,
        int processId,
        object config)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(CreateDocxBytes(new[]
        {
            new[] { "项目", "规格", "验收", "备注" },
            new[] { project, specification, "", "" }
        })), "file", $"evidence-preview-{Guid.NewGuid():N}.docx");

        var uploadResp = await _client.PostAsync("/api/documents/upload", multipart);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var uploadJson = await uploadResp.ReadAsAsync<ApiResponse<JsonElement>>();
        var fileId = uploadJson.Data.GetProperty("fileId").GetInt32();

        return await _client.PostAsync(
            "/api/matching/batch-preview",
            ApiClientJson.ToJsonContent(new
            {
                fileId,
                customerId,
                processId,
                config,
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
    }

    private static byte[] CreateDocxBytes(string[][] rows)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());

            var table = new Table();
            table.AppendChild(new TableProperties(new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));

            foreach (var row in rows)
            {
                var tableRow = new TableRow();
                foreach (var cell in row)
                {
                    tableRow.AppendChild(new TableCell(new Paragraph(new Run(new Text(cell ?? string.Empty)))));
                }

                table.AppendChild(tableRow);
            }

            mainPart.Document.Body!.Append(table);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }
}
