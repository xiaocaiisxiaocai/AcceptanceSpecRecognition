using AcceptanceSpecSystem.Core.AI.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Data.Repositories;
using Entities = AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Providers;

public sealed class AiServiceConfigProvider : IAiServiceConfigProvider
{
    private readonly IAiServiceConfigRepository _repository;

    public AiServiceConfigProvider(IAiServiceConfigRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<AiServiceConfigModel>> GetByPurposeAsync(
        AiServicePurpose purpose,
        CancellationToken cancellationToken = default)
    {
        var entities = await _repository.GetByPurposeAsync(AiServiceConfigMapper.ToDataPurpose(purpose));
        return entities.Select(AiServiceConfigMapper.ToCoreModel).ToList();
    }
}

public sealed class PromptTemplateProvider : IPromptTemplateProvider
{
    private readonly IPromptTemplateRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public PromptTemplateProvider(IPromptTemplateRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<PromptTemplateModel> GetOrCreateSystemAsync(
        PromptTemplateScene scene,
        string name,
        string displayName,
        string defaultContent,
        CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetOrCreateSystemAsync(
            PromptTemplateMapper.ToDataScene(scene),
            name,
            displayName,
            defaultContent);
        await _unitOfWork.SaveChangesAsync();

        return new PromptTemplateModel
        {
            Id = entity.Id,
            Content = entity.Content
        };
    }

    public async Task SaveContentAsync(
        int id,
        string content,
        CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByIdAsync(id)
            ?? throw new InvalidOperationException($"Prompt 模板不存在: {id}");

        entity.Content = content;
        entity.UpdatedAt = DateTime.UtcNow;
        _repository.Update(entity);
        await _unitOfWork.SaveChangesAsync();
    }
}

internal static class AiServiceConfigMapper
{
    public static AiServiceConfigModel ToCoreModel(Entities.AiServiceConfig entity)
    {
        return new AiServiceConfigModel
        {
            Id = entity.Id,
            Name = entity.Name,
            ServiceType = entity.ServiceType switch
            {
                Entities.AiServiceType.OpenAI => AiServiceType.OpenAI,
                Entities.AiServiceType.AzureOpenAI => AiServiceType.AzureOpenAI,
                Entities.AiServiceType.Ollama => AiServiceType.Ollama,
                Entities.AiServiceType.LMStudio => AiServiceType.LMStudio,
                _ => AiServiceType.CustomOpenAICompatible
            },
            Purpose = ToCorePurpose(entity.Purpose),
            Priority = entity.Priority,
            ApiKey = entity.ApiKey,
            Endpoint = entity.Endpoint,
            EmbeddingModel = entity.EmbeddingModel,
            LlmModel = entity.LlmModel,
            DisableThinking = entity.DisableThinking,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    public static Entities.AiServicePurpose ToDataPurpose(AiServicePurpose purpose)
    {
        var mapped = Entities.AiServicePurpose.None;
        if (purpose.HasFlag(AiServicePurpose.Llm))
            mapped |= Entities.AiServicePurpose.Llm;
        if (purpose.HasFlag(AiServicePurpose.Embedding))
            mapped |= Entities.AiServicePurpose.Embedding;
        return mapped;
    }

    private static AiServicePurpose ToCorePurpose(Entities.AiServicePurpose purpose)
    {
        var mapped = AiServicePurpose.None;
        if (purpose.HasFlag(Entities.AiServicePurpose.Llm))
            mapped |= AiServicePurpose.Llm;
        if (purpose.HasFlag(Entities.AiServicePurpose.Embedding))
            mapped |= AiServicePurpose.Embedding;
        return mapped;
    }
}

internal static class PromptTemplateMapper
{
    public static Entities.PromptTemplateScene ToDataScene(PromptTemplateScene scene)
    {
        return scene switch
        {
            PromptTemplateScene.MatchingReview => Entities.PromptTemplateScene.MatchingReview,
            PromptTemplateScene.ImportDuplicateReview => Entities.PromptTemplateScene.ImportDuplicateReview,
            PromptTemplateScene.MatchingEquivalenceAdjudication => Entities.PromptTemplateScene.MatchingEquivalenceAdjudication,
            PromptTemplateScene.MatchingCandidateRerank => Entities.PromptTemplateScene.MatchingCandidateRerank,
            _ => Entities.PromptTemplateScene.Unknown
        };
    }
}
