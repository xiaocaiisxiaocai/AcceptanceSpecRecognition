using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcceptanceSpecSystem.Api.Tests;

public class EmbeddingCacheWarmupApiTests : IClassFixture<EmbeddingCacheWarmupApiTests.WarmupApiFactory>
{
    private readonly HttpClient _client;
    private readonly RecordingWarmupExecutor _executor;

    public EmbeddingCacheWarmupApiTests(WarmupApiFactory factory)
    {
        _client = factory.CreateClient();
        _executor = factory.Executor;
    }

    [Fact]
    public async Task Get_ShouldReturnOptionsAndCurrentState()
    {
        await _client.PutAsync(
            "/api/embedding-cache-warmup/options",
            ApiClientJson.ToJsonContent(new
            {
                enabled = false,
                runOnStartup = false,
                runAtLocalTime = "",
                intervalHours = 24,
                batchSize = 100,
                maxItemsPerRun = 1000
            }));

        var response = await _client.GetAsync("/api/embedding-cache-warmup");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();

        result.Code.Should().Be(0);
        result.Data.GetProperty("options").GetProperty("enabled").GetBoolean().Should().BeFalse();
        result.Data.GetProperty("options").GetProperty("batchSize").GetInt32().Should().Be(100);
        result.Data.GetProperty("status").GetProperty("isRunning").GetBoolean().Should().BeFalse();
        result.Data.TryGetProperty("lastResult", out _).Should().BeTrue();
    }

    [Fact]
    public async Task PutOptions_ShouldUpdateInMemoryOptionsForSubsequentReads()
    {
        var response = await _client.PutAsync(
            "/api/embedding-cache-warmup/options",
            ApiClientJson.ToJsonContent(new
            {
                enabled = true,
                runOnStartup = true,
                runAtLocalTime = "03:30",
                intervalHours = 6,
                batchSize = 25,
                maxItemsPerRun = 250
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await _client.GetAsync("/api/embedding-cache-warmup");
        var result = await getResponse.ReadAsAsync<ApiResponse<JsonElement>>();

        var options = result.Data.GetProperty("options");
        options.GetProperty("enabled").GetBoolean().Should().BeTrue();
        options.GetProperty("runOnStartup").GetBoolean().Should().BeTrue();
        options.GetProperty("runAtLocalTime").GetString().Should().Be("03:30");
        options.GetProperty("intervalHours").GetInt32().Should().Be(6);
        options.GetProperty("batchSize").GetInt32().Should().Be(25);
        options.GetProperty("maxItemsPerRun").GetInt32().Should().Be(250);
    }

    [Fact]
    public async Task Run_ShouldUseCurrentBatchOptionsAndRecordSuccess()
    {
        await _client.PutAsync(
            "/api/embedding-cache-warmup/options",
            ApiClientJson.ToJsonContent(new
            {
                enabled = false,
                runOnStartup = false,
                runAtLocalTime = "",
                intervalHours = 24,
                batchSize = 12,
                maxItemsPerRun = 34
            }));

        var response = await _client.PostAsync("/api/embedding-cache-warmup/run", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var runResult = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        runResult.Code.Should().Be(0);
        runResult.Data.GetProperty("options").GetProperty("batchSize").GetInt32().Should().Be(12);
        runResult.Data.GetProperty("status").GetProperty("lastSucceeded").GetBoolean().Should().BeTrue();
        runResult.Data.GetProperty("lastResult").GetProperty("succeeded").GetBoolean().Should().BeTrue();

        (await _executor.WaitForCallsAsync(1, TimeSpan.FromSeconds(2))).Should().BeTrue();
        _executor.BatchSize.Should().Be(12);
        _executor.MaxItemsPerRun.Should().Be(34);

        var statusResponse = await _client.GetAsync("/api/embedding-cache-warmup");
        var result = await statusResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var status = result.Data.GetProperty("status");
        status.GetProperty("isRunning").GetBoolean().Should().BeFalse();
        status.GetProperty("lastSucceeded").GetBoolean().Should().BeTrue();
        status.GetProperty("lastBatchSize").GetInt32().Should().Be(12);
        status.GetProperty("lastMaxItemsPerRun").GetInt32().Should().Be(34);
        result.Data.GetProperty("lastResult").GetProperty("succeeded").GetBoolean().Should().BeTrue();
    }

    public sealed class WarmupApiFactory : ApiWebApplicationFactory
    {
        public RecordingWarmupExecutor Executor { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmbeddingCacheWarmupExecutor>();
                services.AddSingleton<IEmbeddingCacheWarmupExecutor>(Executor);
            });
        }
    }

    public sealed class RecordingWarmupExecutor : IEmbeddingCacheWarmupExecutor
    {
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }
        public int BatchSize { get; private set; }
        public int MaxItemsPerRun { get; private set; }

        public Task WarmupAsync(int batchSize, int maxItemsPerRun, CancellationToken cancellationToken)
        {
            Calls++;
            BatchSize = batchSize;
            MaxItemsPerRun = maxItemsPerRun;
            _called.TrySetResult();
            return Task.CompletedTask;
        }

        public async Task<bool> WaitForCallsAsync(int expectedCalls, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);

            while (!cts.IsCancellationRequested)
            {
                if (Calls >= expectedCalls)
                    return true;

                try
                {
                    await Task.WhenAny(_called.Task, Task.Delay(20, cts.Token));
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            return Calls >= expectedCalls;
        }
    }
}
