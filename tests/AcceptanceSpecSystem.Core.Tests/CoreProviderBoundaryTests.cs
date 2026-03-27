using AcceptanceSpecSystem.Core.AI.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Core.TextProcessing.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Services;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests;

public class CoreProviderBoundaryTests
{
    [Fact]
    public async Task SynonymService_ShouldCacheUsingUtcTimestamp()
    {
        var service = new SynonymService(new StubSynonymDataProvider(
        [
            new SynonymGroupModel(
            [
                new SynonymWordModel("治具", true),
                new SynonymWordModel("夹具", false)
            ])
        ]));

        var map = await service.GetWordToStandardMapAsync();

        map["治具"].Should().Be("治具");
        map["夹具"].Should().Be("治具");

        var cachedAtField = typeof(SynonymService)
            .GetField("_cachedAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        cachedAtField.Should().NotBeNull();
        ((DateTime)cachedAtField!.GetValue(service)!).Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task DefaultTextPreprocessingPipeline_ShouldCreateSessionFromProviderConfig()
    {
        var pipeline = new DefaultTextPreprocessingPipeline(
            new StubTextProcessingConfigProvider(new TextProcessingConfigModel
            {
                EnableChineseConversion = false,
                ConversionMode = ChineseConversionMode.None,
                EnableSynonym = true,
                EnableOkNgConversion = true,
                OkStandardFormat = "良",
                NgStandardFormat = "不良"
            }),
            new PassthroughChineseConversionService(),
            new OkNgConversionService(),
            new StubSynonymService(new Dictionary<string, string>
            {
                ["PASS"] = "OK"
            }));

        var session = await pipeline.CreateSessionAsync();

        session.Process("PASS NG").Should().Be("良 不良");
    }

    [Fact]
    public async Task AiServiceSelector_ShouldPrioritizeLocalCandidates_FromProvider()
    {
        var now = DateTime.UtcNow;
        var selector = new AiServiceSelector(new StubAiServiceConfigProvider(
        [
            new AiServiceConfigModel
            {
                Id = 1,
                Name = "OpenAI-Cloud",
                ServiceType = AiServiceType.OpenAI,
                Purpose = AiServicePurpose.Llm,
                Priority = 0,
                LlmModel = "gpt-4.1",
                CreatedAt = now.AddMinutes(-3)
            },
            new AiServiceConfigModel
            {
                Id = 2,
                Name = "LMStudio-Local",
                ServiceType = AiServiceType.LMStudio,
                Purpose = AiServicePurpose.Llm,
                Priority = 10,
                LlmModel = "qwen3",
                CreatedAt = now.AddMinutes(-2)
            },
            new AiServiceConfigModel
            {
                Id = 3,
                Name = "Ollama-Local",
                ServiceType = AiServiceType.Ollama,
                Purpose = AiServicePurpose.Llm,
                Priority = 20,
                LlmModel = "qwen3:32b",
                CreatedAt = now.AddMinutes(-1)
            }
        ]));

        var candidates = await selector.GetCandidatesAsync(AiServicePurpose.Llm);

        candidates.Select(item => item.Id).Should().Equal(2, 3, 1);
    }

    private sealed class StubSynonymDataProvider : ISynonymDataProvider
    {
        private readonly IReadOnlyList<SynonymGroupModel> _groups;

        public StubSynonymDataProvider(IReadOnlyList<SynonymGroupModel> groups)
        {
            _groups = groups;
        }

        public Task<IReadOnlyList<SynonymGroupModel>> GetAllGroupsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_groups);
        }
    }

    private sealed class StubTextProcessingConfigProvider : ITextProcessingConfigProvider
    {
        private readonly TextProcessingConfigModel _config;

        public StubTextProcessingConfigProvider(TextProcessingConfigModel config)
        {
            _config = config;
        }

        public Task<TextProcessingConfigModel> GetConfigAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_config);
        }
    }

    private sealed class StubSynonymService : ISynonymService
    {
        private readonly IReadOnlyDictionary<string, string> _map;

        public StubSynonymService(IReadOnlyDictionary<string, string> map)
        {
            _map = map;
        }

        public Task<IReadOnlyDictionary<string, string>> GetWordToStandardMapAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_map);
        }
    }

    private sealed class PassthroughChineseConversionService : IChineseConversionService
    {
        public string Convert(string text, ChineseConversionMode mode)
        {
            return text;
        }
    }

    private sealed class StubAiServiceConfigProvider : IAiServiceConfigProvider
    {
        private readonly IReadOnlyList<AiServiceConfigModel> _configs;

        public StubAiServiceConfigProvider(IReadOnlyList<AiServiceConfigModel> configs)
        {
            _configs = configs;
        }

        public Task<IReadOnlyList<AiServiceConfigModel>> GetByPurposeAsync(
            AiServicePurpose purpose,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_configs.Where(config => (config.Purpose & purpose) != AiServicePurpose.None).ToList() as IReadOnlyList<AiServiceConfigModel>);
        }
    }
}
