using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.Matching.Services;
using AcceptanceSpecSystem.Data.Context;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DataAiServiceConfig = AcceptanceSpecSystem.Data.Entities.AiServiceConfig;
using DataAiServicePurpose = AcceptanceSpecSystem.Data.Entities.AiServicePurpose;
using DataAiServiceType = AcceptanceSpecSystem.Data.Entities.AiServiceType;

namespace AcceptanceSpecSystem.Api.Tests;

[Collection(RealAiIntegrationCollection.Name)]
public class RealAiExternalCallIntegrationTests
{
    private readonly RealAiIntegrationFixture _fixture;

    public RealAiExternalCallIntegrationTests(RealAiIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [RealAiFact]
    public async Task RealEmbedding_WhenComparingBrandAliasAndConflict_ShouldPreferAlias()
    {
        var context = await _fixture.GetContextAsync();

        var similarity = await context.MeasureBrandAliasSimilarityAsync();

        similarity.Dimension.Should().BeGreaterThan(0);
        similarity.Alias.Should().BeGreaterThan(similarity.Conflict);
    }

    [RealAiFact]
    public async Task RealMatchingPipeline_WhenBrandAlias_ShouldAutoApply()
    {
        var context = await _fixture.GetContextAsync();

        var result = await context.MatchBrandAliasAsync();

        result.Decision.Should().Be(AcceptanceSpecSystem.Core.Matching.Models.MatchDecision.AutoApply);
        result.LlmEquivalence.Should().NotBeNull();
        result.LlmEquivalence!.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
    }

    [RealAiFact]
    public async Task RealMatchingPipeline_WhenBrandConflict_ShouldRequireManualReview()
    {
        var context = await _fixture.GetContextAsync();

        var result = await context.MatchBrandConflictAsync();

        result.Decision.Should().Be(AcceptanceSpecSystem.Core.Matching.Models.MatchDecision.ManualReview);
        result.LlmEquivalence.Should().NotBeNull();
        result.LlmEquivalence!.Verdict.Should().Be(LlmEquivalenceVerdict.Different);
    }

    [RealAiFact]
    public async Task RealLlmAdjudication_WhenUnitEquivalent_ShouldReturnEquivalent()
    {
        var context = await _fixture.GetContextAsync();

        var result = await context.AdjudicateUnitEquivalenceAsync();

        result.Should().NotBeNull();
        result!.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [RealAiFact]
    public async Task RealLlmAdjudication_WhenDirectionOpposite_ShouldReturnDifferent()
    {
        var context = await _fixture.GetContextAsync();

        var result = await context.AdjudicateDirectionConflictAsync();

        result.Should().NotBeNull();
        result!.Verdict.Should().Be(LlmEquivalenceVerdict.Different);
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [RealAiFact]
    public async Task RealLlmAdjudication_WhenMillisecondTextFormatsEquivalent_ShouldReturnEquivalent()
    {
        var context = await _fixture.GetContextAsync();

        var result = await context.AdjudicateMillisecondTextFormatEquivalenceAsync();

        result.Should().NotBeNull();
        result!.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [RealAiFact]
    public async Task RealLlmAdjudication_WhenPowerUnitEquivalent_ShouldReturnEquivalent()
    {
        var context = await _fixture.GetContextAsync();

        var result = await context.AdjudicatePowerUnitEquivalenceAsync();

        result.Should().NotBeNull();
        result!.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [RealAiFact]
    public async Task RealLlmAdjudication_WhenLengthUnitEquivalent_ShouldReturnEquivalent()
    {
        var context = await _fixture.GetContextAsync();

        var result = await context.AdjudicateLengthUnitEquivalenceAsync();

        result.Should().NotBeNull();
        result!.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [RealAiFact]
    public async Task RealLlmAdjudication_WhenRotationDirectionOpposite_ShouldReturnDifferent()
    {
        var context = await _fixture.GetContextAsync();

        var result = await context.AdjudicateRotationDirectionConflictAsync();

        result.Should().NotBeNull();
        result!.Verdict.Should().Be(LlmEquivalenceVerdict.Different);
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [RealAiFact]
    public async Task RealLlmAdjudication_WhenFrequencyUnitEquivalent_ShouldReturnEquivalent()
    {
        var context = await _fixture.GetContextAsync();

        var result = await context.AdjudicateFrequencyUnitEquivalenceAsync();

        result.Should().NotBeNull();
        result!.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [RealAiFact]
    public async Task RealLlmAdjudication_WhenWeightUnitEquivalent_ShouldReturnEquivalent()
    {
        var context = await _fixture.GetContextAsync();

        var result = await context.AdjudicateWeightUnitEquivalenceAsync();

        result.Should().NotBeNull();
        result!.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [RealAiFact]
    public async Task RealLlmAdjudication_WhenDistanceUnitEquivalent_ShouldReturnEquivalent()
    {
        var context = await _fixture.GetContextAsync();

        var result = await context.AdjudicateDistanceUnitEquivalenceAsync();

        result.Should().NotBeNull();
        result!.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [RealAiFact]
    public async Task RealLlmAdjudication_WhenCurrentUnitEquivalent_ShouldReturnEquivalent()
    {
        var context = await _fixture.GetContextAsync();

        var result = await context.AdjudicateCurrentUnitEquivalenceAsync();

        result.Should().NotBeNull();
        result!.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [RealAiFact]
    public async Task RealLlmAdjudication_WhenPressureUnitEquivalent_ShouldReturnEquivalent()
    {
        var context = await _fixture.GetContextAsync();

        var result = await context.AdjudicatePressureUnitEquivalenceAsync();

        result.Should().NotBeNull();
        result!.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [RealAiFact]
    public async Task RealLlmAdjudication_WhenTemperatureTextEquivalent_ShouldReturnEquivalent()
    {
        var context = await _fixture.GetContextAsync();

        var result = await context.AdjudicateTemperatureTextEquivalenceAsync();

        result.Should().NotBeNull();
        result!.Verdict.Should().Be(LlmEquivalenceVerdict.Equivalent);
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [RealAiFact]
    public async Task RealLlmAdjudication_WhenClockwiseDirectionOpposite_ShouldReturnDifferent()
    {
        var context = await _fixture.GetContextAsync();

        var result = await context.AdjudicateClockwiseDirectionConflictAsync();

        result.Should().NotBeNull();
        result!.Verdict.Should().Be(LlmEquivalenceVerdict.Different);
        result.Confidence.Should().BeGreaterThan(0.5);
    }

    [RealAiFact]
    public async Task RealLlmAdjudication_WhenSwitchContactOpposite_ShouldReturnDifferent()
    {
        var context = await _fixture.GetContextAsync();

        var result = await context.AdjudicateSwitchContactConflictAsync();

        result.Should().NotBeNull();
        result!.Verdict.Should().Be(LlmEquivalenceVerdict.Different);
        result.Confidence.Should().BeGreaterThan(0.5);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RealAiIntegrationCollection : ICollectionFixture<RealAiIntegrationFixture>
{
    public const string Name = "RealAiIntegration";
}

public sealed class RealAiFactAttribute : FactAttribute
{
    private const string EnableEnvName = "ACCEPTANCE_SPEC_REAL_AI";

    public RealAiFactAttribute()
    {
        if (!IsEnabled())
        {
            Skip = $"默认跳过真实外部调用测试。设置环境变量 {EnableEnvName}=1 后再运行。";
        }
    }

    private static bool IsEnabled()
    {
        var value = Environment.GetEnvironmentVariable(EnableEnvName)?.Trim();
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class RealAiIntegrationFixture : IAsyncLifetime, IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private RealAiIntegrationContext? _context;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task<RealAiIntegrationContext> GetContextAsync()
    {
        if (_context != null)
        {
            return _context;
        }

        await _lock.WaitAsync();
        try
        {
            if (_context == null)
            {
                _context = await RealAiIntegrationContext.CreateAsync();
            }

            return _context;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DisposeAsync()
    {
        _lock.Dispose();

        if (_context != null)
        {
            await _context.DisposeAsync();
        }
    }

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());
}

public sealed class RealAiIntegrationContext : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SemanticKernelServiceFactory _factory;
    private readonly SemanticKernelEmbeddingService _embeddingService;
    private readonly LlmMatchingAssistService _llmAssistService;
    private readonly SemanticKernelMatchingService _matchingService;
    private readonly AiServiceConfigModel _llmConfig;
    private readonly AiServiceConfigModel _embeddingConfig;

    private RealAiIntegrationContext(
        ServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        SemanticKernelServiceFactory factory,
        SemanticKernelEmbeddingService embeddingService,
        LlmMatchingAssistService llmAssistService,
        SemanticKernelMatchingService matchingService,
        AiServiceConfigModel llmConfig,
        AiServiceConfigModel embeddingConfig)
    {
        _serviceProvider = serviceProvider;
        _loggerFactory = loggerFactory;
        _factory = factory;
        _embeddingService = embeddingService;
        _llmAssistService = llmAssistService;
        _matchingService = matchingService;
        _llmConfig = llmConfig;
        _embeddingConfig = embeddingConfig;
    }

    public static async Task<RealAiIntegrationContext> CreateAsync()
    {
        var repoRoot = FindRepoRoot();
        var runtimeSettings = RuntimeSettings.Load(repoRoot);
        var llmEntity = await LoadConfigAsync(runtimeSettings, AiServicePurpose.Llm);
        var embeddingEntity = await LoadConfigAsync(runtimeSettings, AiServicePurpose.Embedding);

        var llmConfig = ToCoreModel(llmEntity);
        var embeddingConfig = ToCoreModel(embeddingEntity);

        var serviceProvider = new ServiceCollection()
            .AddOptions()
            .Configure<AiEndpointSecurityOptions>(_ => { })
            .AddSingleton<IAiDnsResolver, AiDnsResolver>()
            .AddSingleton<IAiEndpointAccessPolicy, AiEndpointAccessPolicy>()
            .AddSingleton<IAiSocketFactory, AiSocketFactory>()
            .AddSingleton<IAiSocketConnectOperation, AiSocketConnectOperation>()
            .AddSingleton<IAiSocketConnector, AiSocketConnector>()
            .AddSingleton<ISafeAiHttpClientFactory, SafeAiHttpMessageHandlerFactory>()
            .BuildServiceProvider();

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        var factory = new SemanticKernelServiceFactory(
            loggerFactory,
            serviceProvider.GetRequiredService<ISafeAiHttpClientFactory>(),
            Microsoft.Extensions.Options.Options.Create(new SemanticKernelOptions
            {
                AzureOpenAIApiVersion = runtimeSettings.AzureOpenAiApiVersion
            }));

        var selector = new AiServiceSelector(new StaticAiServiceConfigProvider([llmConfig, embeddingConfig]));
        var promptProvider = new InMemoryPromptTemplateProvider();
        var embeddingService = new SemanticKernelEmbeddingService(
            selector,
            factory,
            loggerFactory.CreateLogger<SemanticKernelEmbeddingService>());
        var llmAssistService = new LlmMatchingAssistService(
            promptProvider,
            selector,
            factory,
            loggerFactory.CreateLogger<LlmMatchingAssistService>());
        var matchingService = new SemanticKernelMatchingService(
            embeddingService,
            loggerFactory.CreateLogger<SemanticKernelMatchingService>(),
            llmEquivalenceAdjudicationService: llmAssistService);

        return new RealAiIntegrationContext(
            serviceProvider,
            loggerFactory,
            factory,
            embeddingService,
            llmAssistService,
            matchingService,
            llmConfig,
            embeddingConfig);
    }

    public async Task<RealEmbeddingSimilarityResult> MeasureBrandAliasSimilarityAsync()
    {
        var texts = new[]
        {
            "品牌要求 供应商品牌 Panasonic",
            "品牌要求 供应商品牌 松下",
            "品牌要求 供应商品牌 Mitsubishi"
        };

        var embeddings = await _embeddingService.GenerateEmbeddingsAsync(
            texts,
            _embeddingConfig.Id);

        var alias = _embeddingService.ComputeSimilarity(embeddings[0], embeddings[1]);
        var conflict = _embeddingService.ComputeSimilarity(embeddings[0], embeddings[2]);
        return new RealEmbeddingSimilarityResult(alias, conflict, embeddings[0].Length);
    }

    public async Task<MatchResult> MatchBrandAliasAsync()
    {
        return await MatchSingleAsync(
            new MatchSource
            {
                Project = "品牌要求",
                Specification = "供应商品牌 Panasonic"
            },
            new MatchCandidate
            {
                SpecId = 1001,
                Project = "品牌要求",
                Specification = "供应商品牌 松下",
                Acceptance = "品牌一致即可"
            });
    }

    public async Task<MatchResult> MatchBrandConflictAsync()
    {
        return await MatchSingleAsync(
            new MatchSource
            {
                Project = "品牌要求",
                Specification = "供应商品牌 Panasonic"
            },
            new MatchCandidate
            {
                SpecId = 1002,
                Project = "品牌要求",
                Specification = "供应商品牌 Mitsubishi",
                Acceptance = "品牌一致即可"
            });
    }

    public Task<LlmEquivalenceAdjudicationResult?> AdjudicateUnitEquivalenceAsync()
    {
        return AdjudicateAsync(
            "芯片工艺",
            "线宽等于0.13μm",
            "芯片工艺",
            "线宽等于130nm");
    }

    public Task<LlmEquivalenceAdjudicationResult?> AdjudicateDirectionConflictAsync()
    {
        return AdjudicateAsync(
            "设备设计要求",
            "收板机生产载位对接AGV,安全光栅有效范围离地最低处为360mm",
            "设备设计要求",
            "放板机生产载位对接AGV,安全光栅有效范围离地最低处为360mm");
    }

    public Task<LlmEquivalenceAdjudicationResult?> AdjudicateMillisecondTextFormatEquivalenceAsync()
    {
        return AdjudicateAsync(
            "节拍要求",
            "动作响应时间不超过1000ms",
            "节拍要求",
            "动作响应时间不超过1秒");
    }

    public Task<LlmEquivalenceAdjudicationResult?> AdjudicatePowerUnitEquivalenceAsync()
    {
        return AdjudicateAsync(
            "电机参数",
            "电机额定功率为1kW",
            "电机参数",
            "电机额定功率为1000W");
    }

    public Task<LlmEquivalenceAdjudicationResult?> AdjudicateLengthUnitEquivalenceAsync()
    {
        return AdjudicateAsync(
            "装配尺寸",
            "定位销直径为10mm",
            "装配尺寸",
            "定位销直径为1cm");
    }

    public Task<LlmEquivalenceAdjudicationResult?> AdjudicateRotationDirectionConflictAsync()
    {
        return AdjudicateAsync(
            "驱动控制",
            "电机启动后应正转运行",
            "驱动控制",
            "电机启动后应反转运行");
    }

    public Task<LlmEquivalenceAdjudicationResult?> AdjudicateFrequencyUnitEquivalenceAsync()
    {
        return AdjudicateAsync(
            "振动参数",
            "设备运行频率保持在1000Hz",
            "振动参数",
            "设备运行频率保持在1kHz");
    }

    public Task<LlmEquivalenceAdjudicationResult?> AdjudicateWeightUnitEquivalenceAsync()
    {
        return AdjudicateAsync(
            "搬运能力",
            "单次载重不低于500g",
            "搬运能力",
            "单次载重不低于0.5kg");
    }

    public Task<LlmEquivalenceAdjudicationResult?> AdjudicateDistanceUnitEquivalenceAsync()
    {
        return AdjudicateAsync(
            "行程范围",
            "X轴有效行程为2500mm",
            "行程范围",
            "X轴有效行程为2.5m");
    }

    public Task<LlmEquivalenceAdjudicationResult?> AdjudicateCurrentUnitEquivalenceAsync()
    {
        return AdjudicateAsync(
            "电气参数",
            "额定电流不超过0.5A",
            "电气参数",
            "额定电流不超过500mA");
    }

    public Task<LlmEquivalenceAdjudicationResult?> AdjudicatePressureUnitEquivalenceAsync()
    {
        return AdjudicateAsync(
            "气源参数",
            "工作压力保持在0.1MPa",
            "气源参数",
            "工作压力保持在100kPa");
    }

    public Task<LlmEquivalenceAdjudicationResult?> AdjudicateTemperatureTextEquivalenceAsync()
    {
        return AdjudicateAsync(
            "环境要求",
            "运行环境温度保持在25℃",
            "环境要求",
            "运行环境温度保持在25摄氏度");
    }

    public Task<LlmEquivalenceAdjudicationResult?> AdjudicateClockwiseDirectionConflictAsync()
    {
        return AdjudicateAsync(
            "旋转机构",
            "转盘需保持顺时针旋转",
            "旋转机构",
            "转盘需保持逆时针旋转");
    }

    public Task<LlmEquivalenceAdjudicationResult?> AdjudicateSwitchContactConflictAsync()
    {
        return AdjudicateAsync(
            "安全回路",
            "急停触点类型应为常开",
            "安全回路",
            "急停触点类型应为常闭");
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        _loggerFactory.Dispose();
        _serviceProvider.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<MatchResult> MatchSingleAsync(MatchSource source, MatchCandidate candidate)
    {
        var batch = await _matchingService.BatchMatchAsync(
            [source],
            [candidate],
            new MatchingConfig
            {
                EmbeddingServiceId = _embeddingConfig.Id,
                LlmServiceId = _llmConfig.Id,
                MinScoreThreshold = 0.0,
                RecallTopK = 1,
                HighConfidenceThreshold = 0.98
            });

        batch.Results.Should().ContainSingle();
        return batch.Results[0];
    }

    private Task<LlmEquivalenceAdjudicationResult?> AdjudicateAsync(
        string sourceProject,
        string sourceSpecification,
        string candidateProject,
        string candidateSpecification)
    {
        return _llmAssistService.AdjudicateAsync(new LlmEquivalenceAdjudicationRequest
        {
            SourceProject = sourceProject,
            SourceSpecification = sourceSpecification,
            CandidateProject = candidateProject,
            CandidateSpecification = candidateSpecification,
            CurrentDecision = "manualReview",
            ScoreDetails = new Dictionary<string, double>(),
            EvidenceSummary = [],
            ConflictSummary = [],
            LlmServiceId = _llmConfig.Id
        });
    }

    private static async Task<DataAiServiceConfig> LoadConfigAsync(RuntimeSettings settings, AiServicePurpose purpose)
    {
        var configIdEnvName = purpose == AiServicePurpose.Llm
            ? "ACCEPTANCE_SPEC_REAL_LLM_CONFIG_ID"
            : "ACCEPTANCE_SPEC_REAL_EMBEDDING_CONFIG_ID";
        var configNameEnvName = purpose == AiServicePurpose.Llm
            ? "ACCEPTANCE_SPEC_REAL_LLM_CONFIG_NAME"
            : "ACCEPTANCE_SPEC_REAL_EMBEDDING_CONFIG_NAME";

        var candidates = await TryLoadConfigsAsync(settings.PrimaryDataProtectionKeysPath);
        var selected = SelectConfig(
            candidates,
            purpose,
            Environment.GetEnvironmentVariable(configIdEnvName),
            Environment.GetEnvironmentVariable(configNameEnvName));

        if (selected != null && !LooksEncrypted(selected.ApiKey))
        {
            return selected;
        }

        if (string.Equals(
                settings.PrimaryDataProtectionKeysPath,
                settings.FallbackDataProtectionKeysPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw BuildConfigNotFoundException(purpose, configIdEnvName, configNameEnvName, candidates);
        }

        candidates = await TryLoadConfigsAsync(settings.FallbackDataProtectionKeysPath);
        selected = SelectConfig(
            candidates,
            purpose,
            Environment.GetEnvironmentVariable(configIdEnvName),
            Environment.GetEnvironmentVariable(configNameEnvName));

        if (selected != null && !LooksEncrypted(selected.ApiKey))
        {
            return selected;
        }

        throw BuildConfigNotFoundException(purpose, configIdEnvName, configNameEnvName, candidates);
    }

    private static async Task<List<DataAiServiceConfig>> TryLoadConfigsAsync(string keysPath)
    {
        var provider = DataProtectionProvider.Create(
            new DirectoryInfo(keysPath),
            builder => builder.SetApplicationName("AcceptanceSpecSystem"));
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(RuntimeSettings.Current.ConnectionString, ServerVersion.AutoDetect(RuntimeSettings.Current.ConnectionString))
            .Options;

        await using var db = new AppDbContext(options, provider);
        return await db.AiServiceConfigs
            .AsNoTracking()
            .OrderBy(item => item.Priority)
            .ThenByDescending(item => item.UpdatedAt ?? item.CreatedAt)
            .ToListAsync();
    }

    private static DataAiServiceConfig? SelectConfig(
        IReadOnlyList<DataAiServiceConfig> configs,
        AiServicePurpose purpose,
        string? idValue,
        string? nameValue)
    {
        if (int.TryParse(idValue, out var id))
        {
            return configs.FirstOrDefault(item =>
                item.Id == id &&
                MatchesPurpose(item, purpose));
        }

        if (!string.IsNullOrWhiteSpace(nameValue))
        {
            return configs.FirstOrDefault(item =>
                string.Equals(item.Name, nameValue.Trim(), StringComparison.OrdinalIgnoreCase) &&
                MatchesPurpose(item, purpose));
        }

        return configs.FirstOrDefault(item => MatchesPurpose(item, purpose));
    }

    private static bool MatchesPurpose(DataAiServiceConfig item, AiServicePurpose purpose)
    {
        var effectivePurpose = item.GetEffectivePurpose();
        return purpose switch
        {
            AiServicePurpose.Llm => effectivePurpose.HasFlag(DataAiServicePurpose.Llm) && item.HasLlmModel(),
            AiServicePurpose.Embedding => effectivePurpose.HasFlag(DataAiServicePurpose.Embedding) && item.HasEmbeddingModel(),
            _ => false
        };
    }

    private static bool LooksEncrypted(string? apiKey)
    {
        return !string.IsNullOrWhiteSpace(apiKey) &&
               apiKey.StartsWith("CfDJ", StringComparison.Ordinal);
    }

    private static InvalidOperationException BuildConfigNotFoundException(
        AiServicePurpose purpose,
        string configIdEnvName,
        string configNameEnvName,
        IReadOnlyList<DataAiServiceConfig> candidates)
    {
        var available = string.Join(
            ", ",
            candidates.Select(item =>
                $"#{item.Id}:{item.Name}:{item.GetEffectivePurpose()}"));

        var label = purpose == AiServicePurpose.Llm ? "LLM" : "Embedding";
        return new InvalidOperationException(
            $"未找到可用的真实 {label} 配置。可通过 {configIdEnvName} 或 {configNameEnvName} 指定。当前库内配置: {available}");
    }

    private static AiServiceConfigModel ToCoreModel(DataAiServiceConfig entity)
    {
        var effectivePurpose = entity.GetEffectivePurpose();
        return new AiServiceConfigModel
        {
            Id = entity.Id,
            Name = entity.Name,
            ServiceType = entity.ServiceType switch
            {
                DataAiServiceType.OpenAI => AcceptanceSpecSystem.Core.AI.Models.AiServiceType.OpenAI,
                DataAiServiceType.AzureOpenAI => AcceptanceSpecSystem.Core.AI.Models.AiServiceType.AzureOpenAI,
                DataAiServiceType.Ollama => AcceptanceSpecSystem.Core.AI.Models.AiServiceType.Ollama,
                DataAiServiceType.LMStudio => AcceptanceSpecSystem.Core.AI.Models.AiServiceType.LMStudio,
                _ => AcceptanceSpecSystem.Core.AI.Models.AiServiceType.CustomOpenAICompatible
            },
            Purpose = effectivePurpose switch
            {
                DataAiServicePurpose.Llm => AiServicePurpose.Llm,
                DataAiServicePurpose.Embedding => AiServicePurpose.Embedding,
                DataAiServicePurpose.Llm | DataAiServicePurpose.Embedding => AiServicePurpose.Llm | AiServicePurpose.Embedding,
                _ => AiServicePurpose.None
            },
            Priority = entity.Priority,
            ApiKey = entity.ApiKey,
            Endpoint = entity.Endpoint,
            EmbeddingModel = entity.HasEmbeddingModel() ? entity.EmbeddingModel : null,
            LlmModel = entity.HasLlmModel() ? entity.LlmModel : null,
            DisableThinking = entity.DisableThinking,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    private static string FindRepoRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(start);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "AcceptanceSpecSystem.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new InvalidOperationException("未找到仓库根目录，无法定位开发环境配置文件。");
    }

    private sealed class StaticAiServiceConfigProvider : IAiServiceConfigProvider
    {
        private readonly IReadOnlyList<AiServiceConfigModel> _configs;

        public StaticAiServiceConfigProvider(IReadOnlyList<AiServiceConfigModel> configs)
        {
            _configs = configs;
        }

        public Task<IReadOnlyList<AiServiceConfigModel>> GetByPurposeAsync(
            AiServicePurpose purpose,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<AiServiceConfigModel> matched = _configs
                .Where(item => item.Purpose.HasFlag(purpose))
                .ToList();
            return Task.FromResult(matched);
        }
    }

    private sealed class InMemoryPromptTemplateProvider : IPromptTemplateProvider
    {
        private readonly Dictionary<string, PromptTemplateModel> _templates = new(StringComparer.Ordinal);
        private int _nextId = 1;

        public Task<PromptTemplateModel> GetOrCreateSystemAsync(
            PromptTemplateScene scene,
            string name,
            string displayName,
            string defaultContent,
            CancellationToken cancellationToken = default)
        {
            var key = $"{(int)scene}:{name}";
            if (!_templates.TryGetValue(key, out var template))
            {
                template = new PromptTemplateModel
                {
                    Id = _nextId++,
                    Content = defaultContent
                };
                _templates[key] = template;
            }

            return Task.FromResult(template);
        }

        public Task SaveContentAsync(
            int id,
            string content,
            CancellationToken cancellationToken = default)
        {
            var template = _templates.Values.FirstOrDefault(item => item.Id == id);
            if (template != null)
            {
                template.Content = content;
            }

            return Task.CompletedTask;
        }
    }
}

public sealed record RealEmbeddingSimilarityResult(double Alias, double Conflict, int Dimension);

public sealed class RuntimeSettings
{
    private RuntimeSettings(
        string connectionString,
        string azureOpenAiApiVersion,
        string primaryDataProtectionKeysPath,
        string fallbackDataProtectionKeysPath)
    {
        ConnectionString = connectionString;
        AzureOpenAiApiVersion = azureOpenAiApiVersion;
        PrimaryDataProtectionKeysPath = primaryDataProtectionKeysPath;
        FallbackDataProtectionKeysPath = fallbackDataProtectionKeysPath;
    }

    public static RuntimeSettings Current { get; private set; } = null!;

    public string ConnectionString { get; }

    public string AzureOpenAiApiVersion { get; }

    public string PrimaryDataProtectionKeysPath { get; }

    public string FallbackDataProtectionKeysPath { get; }

    public static RuntimeSettings Load(string repoRoot)
    {
        var apiRoot = Path.Combine(repoRoot, "src", "AcceptanceSpecSystem.Api");
        var baseJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(apiRoot, "appsettings.json")));
        var developmentJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(apiRoot, "appsettings.Development.json")));

        var connectionString =
            ReadString(developmentJson.RootElement, "ConnectionStrings", "DefaultConnection") ??
            ReadString(baseJson.RootElement, "ConnectionStrings", "DefaultConnection") ??
            throw new InvalidOperationException("未读取到开发环境数据库连接串。");

        var keysPath = ReadString(baseJson.RootElement, "DataProtection", "KeysPath") ?? ".\\data-protection-keys";
        var azureVersion =
            ReadString(baseJson.RootElement, "SemanticKernel", "AzureOpenAIApiVersion") ??
            new SemanticKernelOptions().AzureOpenAIApiVersion;

        var primaryKeysPath = Path.GetFullPath(Path.Combine(apiRoot, keysPath));
        var fallbackKeysPath = Path.GetFullPath(Path.Combine(repoRoot, "data-protection-keys"));

        Current = new RuntimeSettings(
            connectionString,
            azureVersion,
            primaryKeysPath,
            fallbackKeysPath);
        return Current;
    }

    private static string? ReadString(JsonElement root, string section, string key)
    {
        if (!root.TryGetProperty(section, out var sectionElement) ||
            !sectionElement.TryGetProperty(key, out var valueElement))
        {
            return null;
        }

        return valueElement.ValueKind == JsonValueKind.String
            ? valueElement.GetString()
            : null;
    }
}
