using AcceptanceSpecSystem.Core.AI.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests;

public class CoreProviderBoundaryTests
{
    [Fact]
    public async Task AiServiceSelector_ShouldSortCandidatesByPriorityThenUpdatedTime()
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

        candidates.Select(item => item.Id).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task AiServiceSelector_ShouldMovePreferredCandidateToFront()
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
                Name = "Moonshot",
                ServiceType = AiServiceType.CustomOpenAICompatible,
                Purpose = AiServicePurpose.Llm,
                Priority = 1,
                LlmModel = "kimi-k2-turbo-preview",
                CreatedAt = now.AddMinutes(-2)
            }
        ]));

        var candidates = await selector.GetCandidatesAsync(AiServicePurpose.Llm, preferredId: 2);

        candidates.Select(item => item.Id).Should().Equal(2, 1);
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
