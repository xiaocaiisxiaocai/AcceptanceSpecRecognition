using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 从受控配置中提供匹配知识
/// </summary>
public sealed class ConfigurationMatchingKnowledgeProvider : IMatchingKnowledgeProvider
{
    private readonly IMatchingKnowledgeConfigRepository _repository;

    public ConfigurationMatchingKnowledgeProvider(IMatchingKnowledgeConfigRepository repository)
    {
        _repository = repository;
    }

    public async Task<MatchingKnowledge> GetKnowledgeAsync(CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetConfigAsync();
        return MatchingKnowledgeComposition.ToDomainModel(MatchingKnowledgeComposition.ToDto(entity));
    }
}
