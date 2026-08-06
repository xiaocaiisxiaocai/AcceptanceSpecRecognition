using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Application.Options;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using CoreAiServicePurpose = AcceptanceSpecSystem.Core.AI.Models.AiServicePurpose;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class AiServiceReadinessApiTests
{
    [Fact]
    public async Task Selection_ShouldChooseHighestPriorityAvailableServiceWithoutSecrets()
    {
        await using var factory = new DeterministicReadinessApiWebApplicationFactory();
        var client = factory.CreateClient();
        var lowPriorityId = await SeedConfigAsync(factory, AiServicePurpose.Llm, priority: 20, "low");
        var highPriorityId = await SeedConfigAsync(factory, AiServicePurpose.Llm, priority: 1, "high");
        var registry = factory.Services.GetRequiredService<AiServiceReadinessRegistry>();
        registry.ReportAvailable(lowPriorityId, CoreAiServicePurpose.Llm);
        registry.ReportAvailable(highPriorityId, CoreAiServicePurpose.Llm);

        using var response = await client.GetAsync("/api/ai-services/selection?purpose=llm");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonSerializer.Deserialize<ApiResponse<JsonElement>>(
            raw,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        body.Data.GetProperty("status").GetString().Should().Be("available");
        body.Data.GetProperty("serviceId").GetInt32().Should().Be(highPriorityId);
        body.Data.GetProperty("model").GetString().Should().Be("llm-high");
        raw.ToLowerInvariant().Should().NotContain("endpoint")
            .And.NotContain("apikey")
            .And.NotContain("selection-secret");
    }

    [Fact]
    public async Task Selection_ShouldKeepLlmAndEmbeddingReadinessIndependent()
    {
        await using var factory = new DeterministicReadinessApiWebApplicationFactory();
        var client = factory.CreateClient();
        var llmId = await SeedConfigAsync(factory, AiServicePurpose.Llm, 0, "llm-only");
        var embeddingId = await SeedConfigAsync(factory, AiServicePurpose.Embedding, 0, "embedding-only");
        var registry = factory.Services.GetRequiredService<AiServiceReadinessRegistry>();
        registry.ReportUnavailable(llmId, CoreAiServicePurpose.Llm, "raw secret should be discarded");
        registry.ReportAvailable(embeddingId, CoreAiServicePurpose.Embedding);

        var llm = await ReadSelectionAsync(client, "llm");
        var embedding = await ReadSelectionAsync(client, "embedding");

        llm.GetProperty("status").GetString().Should().Be("unavailable");
        llm.GetProperty("message").GetString().Should().NotContain("raw secret");
        embedding.GetProperty("status").GetString().Should().Be("available");
        embedding.GetProperty("serviceId").GetInt32().Should().Be(embeddingId);
    }

    [Fact]
    public async Task Selection_ShouldReturnUnavailableWithoutDisablingConfigAndRecoverAfterSuccess()
    {
        await using var factory = new DeterministicReadinessApiWebApplicationFactory();
        var client = factory.CreateClient();
        var configId = await SeedConfigAsync(factory, AiServicePurpose.Llm, 0, "recover");
        var registry = factory.Services.GetRequiredService<AiServiceReadinessRegistry>();
        registry.ReportUnavailable(configId, CoreAiServicePurpose.Llm);

        (await ReadSelectionAsync(client, "llm")).GetProperty("status").GetString().Should().Be("unavailable");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.AiServiceConfigs.AsNoTracking().SingleAsync(item => item.Id == configId))
                .IsDisabled.Should().BeFalse();
        }

        registry.ReportAvailable(configId, CoreAiServicePurpose.Llm);
        var recovered = await ReadSelectionAsync(client, "llm");
        recovered.GetProperty("status").GetString().Should().Be("available");
        recovered.GetProperty("checkedAt").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task Selection_ShouldReturnCheckingForUnknownCandidateAndConfigChangeShouldInvalidateCache()
    {
        await using var factory = new DeterministicReadinessApiWebApplicationFactory();
        var client = factory.CreateClient();
        var configId = await SeedConfigAsync(factory, AiServicePurpose.Llm, 0, "checking");
        var registry = factory.Services.GetRequiredService<AiServiceReadinessRegistry>();

        var checking = await ReadSelectionAsync(client, "llm");
        checking.GetProperty("status").GetString().Should().Be("checking");

        registry.ReportAvailable(configId, CoreAiServicePurpose.Llm);
        using var scope = factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IAiServiceConfigurationAppService>();
        await configuration.UpdateAsync(configId, new UpdateAiServiceRequest
        {
            Name = "checking-updated",
            ServiceType = AiServiceType.OpenAI,
            Purpose = AiServicePurpose.Llm,
            Priority = 0,
            Endpoint = "https://example.invalid/v1",
            LlmModel = "llm-updated",
            ApiKey = null,
            RowVersion = 0
        });

        registry.GetSnapshot(configId, CoreAiServicePurpose.Llm).State
            .Should().Be(AiServiceReadinessState.Unknown);
    }

    [Fact]
    public async Task PreloadPreferred_ShouldOnlyScheduleHighestPriorityCandidate()
    {
        await using var factory = new DeterministicReadinessApiWebApplicationFactory();
        var lowPriorityId = await SeedConfigAsync(factory, AiServicePurpose.Llm, priority: 20, "preload-low");
        var highPriorityId = await SeedConfigAsync(factory, AiServicePurpose.Llm, priority: 1, "preload-high");
        using var scope = factory.Services.CreateScope();
        var selection = scope.ServiceProvider.GetRequiredService<IAiServiceSelectionAppService>();
        var registry = factory.Services.GetRequiredService<AiServiceReadinessRegistry>();

        var result = await selection.PreloadPreferredAsync(AiServicePurpose.Llm);

        result.Status.Should().Be("checking");
        result.ServiceId.Should().Be(highPriorityId);
        registry.GetSnapshot(highPriorityId, CoreAiServicePurpose.Llm).State
            .Should().Be(AiServiceReadinessState.Checking);
        registry.GetSnapshot(lowPriorityId, CoreAiServicePurpose.Llm).State
            .Should().Be(AiServiceReadinessState.Unknown,
                "启动预热不应把所有候选模型一起装入显存");
    }

    [Fact]
    public async Task Health_ShouldExposeCachedAiDegradationWithoutFailingCoreHealth()
    {
        await using var factory = new DeterministicReadinessApiWebApplicationFactory();
        var client = factory.CreateClient();
        var registry = factory.Services.GetRequiredService<AiServiceReadinessRegistry>();
        registry.ReportUnavailable(987, CoreAiServicePurpose.Llm);

        using var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
        var aiData = json.RootElement.GetProperty("components").GetProperty("aiConfig").GetProperty("data");
        aiData.GetProperty("runtimeStatus").GetString().Should().Be("degraded");
        aiData.GetProperty("llm").GetString().Should().Be("unavailable");
    }

    [Fact]
    public void Registry_ShouldExpireEntriesAfterShortTtl()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-21T00:00:00Z"));
        var registry = new AiServiceReadinessRegistry(
            clock,
            Microsoft.Extensions.Options.Options.Create(new AiServiceReadinessOptions
            {
                StatusTtlSeconds = 2,
                ProbeTimeoutSeconds = 1
            }));
        registry.ReportAvailable(1, CoreAiServicePurpose.Llm);

        registry.GetSnapshot(1, CoreAiServicePurpose.Llm).State.Should().Be(AiServiceReadinessState.Available);
        clock.Advance(TimeSpan.FromSeconds(3));
        registry.GetSnapshot(1, CoreAiServicePurpose.Llm).State.Should().Be(AiServiceReadinessState.Unknown);
    }

    [Fact]
    public void Registry_ShouldIgnoreResultFromGenerationBeforeConfigurationInvalidation()
    {
        var registry = new AiServiceReadinessRegistry(
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new AiServiceReadinessOptions()));
        registry.TryMarkChecking(42, CoreAiServicePurpose.Llm, out var staleGeneration)
            .Should().BeTrue();

        registry.Invalidate(42);
        registry.ReportAvailableIfCurrent(42, CoreAiServicePurpose.Llm, staleGeneration);

        registry.GetSnapshot(42, CoreAiServicePurpose.Llm).State
            .Should().Be(AiServiceReadinessState.Unknown);
    }

    [Fact]
    public void Registry_ShouldAllowExplicitAttemptUntilServiceIsKnownUnavailable()
    {
        var registry = new AiServiceReadinessRegistry(
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new AiServiceReadinessOptions()));

        registry.CanAttempt(42, CoreAiServicePurpose.Llm).Should().BeTrue();
        registry.TryMarkChecking(42, CoreAiServicePurpose.Llm, out _).Should().BeTrue();
        registry.CanAttempt(42, CoreAiServicePurpose.Llm).Should().BeTrue();

        registry.ReportUnavailable(42, CoreAiServicePurpose.Llm);

        registry.CanAttempt(42, CoreAiServicePurpose.Llm).Should().BeFalse();
    }

    private static async Task<JsonElement> ReadSelectionAsync(HttpClient client, string purpose)
    {
        using var response = await client.GetAsync($"/api/ai-services/selection?purpose={purpose}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.ReadAsAsync<ApiResponse<JsonElement>>()).Data;
    }

    private static async Task<int> SeedConfigAsync(
        ApiWebApplicationFactory factory,
        AiServicePurpose purpose,
        int priority,
        string suffix)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = new AiServiceConfig
        {
            Name = $"readiness-{suffix}-{Guid.NewGuid():N}",
            ServiceType = AiServiceType.OpenAI,
            Purpose = purpose,
            Priority = priority,
            Endpoint = "https://example.invalid/v1",
            ApiKey = "selection-secret",
            LlmModel = purpose == AiServicePurpose.Llm ? $"llm-{suffix}" : null,
            EmbeddingModel = purpose == AiServicePurpose.Embedding ? $"embedding-{suffix}" : null,
            CreatedAt = DateTime.UtcNow
        };
        db.AiServiceConfigs.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public MutableTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}

public sealed class DeterministicReadinessApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAiServiceReadinessProbeScheduler>();
            services.AddSingleton<IAiServiceReadinessProbeScheduler, NoopReadinessProbeScheduler>();
        });
    }

    private sealed class NoopReadinessProbeScheduler : IAiServiceReadinessProbeScheduler
    {
        public void RequestProbe(
            AiServiceProbeConfig config,
            CoreAiServicePurpose purpose,
            long generation)
        {
        }
    }
}
