using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Memory;

namespace AcceptanceSpecSystem.Api.Tests;

public class MatchingPreviewProgressTests : IClassFixture<DelayedPreviewProgressApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly DelayedPreviewProgressApiWebApplicationFactory _factory;

    public MatchingPreviewProgressTests(DelayedPreviewProgressApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public void ProgressTracker_ShouldIsolateOwnerRejectDuplicateAndRedactFailure()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var tracker = new AcceptanceSpecSystem.Application.Services.BatchPreviewProgressTracker(cache);
        var owner = new AcceptanceSpecSystem.Application.Services.MatchingUserContext(1, 10);
        var otherUser = new AcceptanceSpecSystem.Application.Services.MatchingUserContext(2, 10);
        var otherCompany = new AcceptanceSpecSystem.Application.Services.MatchingUserContext(1, 20);
        const string requestId = "shared-request-id";

        tracker.TryStart(owner, requestId, 2).Should().BeTrue();
        tracker.TryStart(owner, requestId, 2).Should().BeFalse();
        tracker.GetSnapshot(otherUser, requestId).Should().BeNull();
        tracker.GetSnapshot(otherCompany, requestId).Should().BeNull();

        tracker.Fail(owner, requestId, "Server=mysql; Password=secret; C:\\private\\dump.sql");

        var snapshot = tracker.GetSnapshot(owner, requestId);
        snapshot.Should().NotBeNull();
        snapshot!.Status.Should().Be("failed");
        snapshot.DetailText.Should().Be("匹配预览失败，请稍后重试");
        snapshot.DetailText.Should().NotContain("mysql").And.NotContain("secret").And.NotContain("private");
    }

    [Fact]
    public async Task BatchPreviewProgress_ShouldExposeRunningAndCompletedStages()
    {
        var customerId = (await (await _client.PostAsync(
                "/api/customers",
                ApiClientJson.ToJsonContent(new { name = $"PreviewProgress-C-{Guid.NewGuid():N}" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        var processId = (await (await _client.PostAsync(
                "/api/processes",
                ApiClientJson.ToJsonContent(new { name = $"PreviewProgress-P-{Guid.NewGuid():N}" })))
            .ReadAsAsync<ApiResponse<JsonElement>>()).Data.GetProperty("id").GetInt32();

        for (var index = 1; index <= 3; index++)
        {
            var specResponse = await _client.PostAsync(
                "/api/specs",
                ApiClientJson.ToJsonContent(new
                {
                    customerId,
                    processId,
                    project = $"项目{index}",
                    specification = $"规格{index}-候选",
                    acceptance = $"验收{index}",
                    remark = $"备注{index}"
                }));
            specResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var requestId = Guid.NewGuid().ToString("N");
        var previewTask = BatchPreviewTestHelper.PostWithRequestIdAsync(
            _client,
            customerId,
            processId,
            new
            {
                minScoreThreshold = 0.0,
                recallTopK = 1,
                llmParallelism = 1
            },
            requestId,
            ("项目1", "规格1-源"),
            ("项目2", "规格2-源"),
            ("项目3", "规格3-源"));

        using var runningResponse = await WaitForProgressResponseAsync(requestId);
        runningResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var runningJson = await runningResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        runningJson.Code.Should().Be(0);
        runningJson.Data.GetProperty("requestId").GetString().Should().Be(requestId);
        // 预览可能在第一次轮询前完成；进度端点的契约是返回当前有效快照。
        runningJson.Data.GetProperty("status").GetString().Should().BeOneOf("running", "completed");
        runningJson.Data.GetProperty("stage").GetString().Should().NotBeNullOrWhiteSpace();
        runningJson.Data.GetProperty("stageText").GetString().Should().NotBeNullOrWhiteSpace();

        using var crossOwnerRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/matching/batch-preview-progress/{requestId}");
        crossOwnerRequest.Headers.Add("X-Test-User-Id", "2");
        using var crossOwnerResponse = await _client.SendAsync(crossOwnerRequest);
        crossOwnerResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var crossCompanyRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/matching/batch-preview-progress/{requestId}");
        crossCompanyRequest.Headers.Add("X-Test-Company-Id", "2");
        using var crossCompanyResponse = await _client.SendAsync(crossCompanyRequest);
        crossCompanyResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var previewResponse = await previewTask;
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var completedResponse = await _client.GetAsync($"/api/matching/batch-preview-progress/{requestId}");
        completedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var completedJson = await completedResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        completedJson.Code.Should().Be(0);
        completedJson.Data.GetProperty("status").GetString().Should().Be("completed");
        completedJson.Data.GetProperty("progressPercent").GetDouble().Should().BeGreaterThanOrEqualTo(100);
        completedJson.Data.GetProperty("completedItems").GetInt32().Should().Be(3);
        completedJson.Data.GetProperty("totalItems").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task BatchPreviewProgress_WhenFailed_ShouldReturnOnlySafeErrorText()
    {
        var requestId = Guid.NewGuid().ToString("N");
        using var scope = _factory.Services.CreateScope();
        var tracker = scope.ServiceProvider.GetRequiredService<AcceptanceSpecSystem.Application.Services.BatchPreviewProgressTracker>();
        var owner = new AcceptanceSpecSystem.Application.Services.MatchingUserContext(1, 1);
        tracker.TryStart(owner, requestId, 1).Should().BeTrue();
        tracker.Fail(owner, requestId, "Server=mysql; Password=secret; C:\\private\\dump.sql");

        using var response = await _client.GetAsync($"/api/matching/batch-preview-progress/{requestId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        var detailText = json.Data.GetProperty("detailText").GetString();
        detailText.Should().Be("匹配预览失败，请稍后重试");
        detailText.Should().NotContain("mysql").And.NotContain("secret").And.NotContain("private");
    }

    private async Task<HttpResponseMessage> WaitForProgressResponseAsync(string requestId)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var response = await _client.GetAsync($"/api/matching/batch-preview-progress/{requestId}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return response;
            }

            response.Dispose();
            await Task.Delay(50);
        }

        return await _client.GetAsync($"/api/matching/batch-preview-progress/{requestId}");
    }
}

public sealed class DelayedPreviewProgressApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(ILlmEquivalenceAdjudicationService));
            services.AddScoped<ILlmEquivalenceAdjudicationService, DelayedPreviewProgressEquivalenceService>();
        });
    }
}

internal sealed class DelayedPreviewProgressEquivalenceService : ILlmEquivalenceAdjudicationService
{
    public async Task<LlmEquivalenceAdjudicationResult?> AdjudicateAsync(
        LlmEquivalenceAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(250, cancellationToken);
        return new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Confidence = 0.99,
            Reason = "测试替身：延迟返回，便于观察 preview 进度"
        };
    }

    public bool TryParseAdjudicationResult(string raw, out LlmEquivalenceAdjudicationResult result)
    {
        throw new NotSupportedException();
    }
}
