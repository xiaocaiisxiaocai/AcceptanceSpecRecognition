using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

public class AiServiceConfigRepositoryTests : TestBase
{
    private readonly AiServiceConfigRepository _repository;

    public AiServiceConfigRepositoryTests()
    {
        _repository = new AiServiceConfigRepository(Context);
    }

    [Fact]
    public async Task GetByPurposeAsync_ShouldReturnEnabledExplicitAndLegacySinglePurposeConfigs()
    {
        Context.AiServiceConfigs.AddRange(
            new AiServiceConfig
            {
                Name = "llm-explicit",
                ServiceType = AiServiceType.OpenAI,
                Purpose = AiServicePurpose.Llm,
                LlmModel = "gpt-4"
            },
            new AiServiceConfig
            {
                Name = "llm-legacy",
                ServiceType = AiServiceType.CustomOpenAICompatible,
                Purpose = AiServicePurpose.None,
                LlmModel = "legacy-llm"
            },
            new AiServiceConfig
            {
                Name = "embedding-explicit",
                ServiceType = AiServiceType.Ollama,
                Purpose = AiServicePurpose.Embedding,
                EmbeddingModel = "bge"
            },
            new AiServiceConfig
            {
                Name = "disabled-llm",
                ServiceType = AiServiceType.OpenAI,
                Purpose = AiServicePurpose.Llm,
                LlmModel = "disabled",
                IsDisabled = true
            },
            new AiServiceConfig
            {
                Name = "legacy-dual",
                ServiceType = AiServiceType.OpenAI,
                Purpose = AiServicePurpose.None,
                LlmModel = "dual-llm",
                EmbeddingModel = "dual-embedding"
            });
        await Context.SaveChangesAsync();

        var llmConfigs = await _repository.GetByPurposeAsync(AiServicePurpose.Llm);
        var embeddingConfigs = await _repository.GetByPurposeAsync(AiServicePurpose.Embedding);

        llmConfigs.Select(item => item.Name)
            .Should()
            .BeEquivalentTo(["llm-explicit", "llm-legacy"]);
        embeddingConfigs.Select(item => item.Name)
            .Should()
            .BeEquivalentTo(["embedding-explicit"]);
    }
}
