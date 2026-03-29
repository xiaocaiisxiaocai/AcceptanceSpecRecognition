using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 从受控配置中提供匹配知识
/// </summary>
public sealed class ConfigurationMatchingKnowledgeProvider : IMatchingKnowledgeProvider
{
    private readonly IMatchingKnowledgeConfigRepository _repository;
    private readonly MatchingKnowledgeOptions _defaultOptions;

    public ConfigurationMatchingKnowledgeProvider(
        IMatchingKnowledgeConfigRepository repository,
        IOptions<MatchingKnowledgeOptions> options)
    {
        _repository = repository;
        _defaultOptions = options.Value ?? new MatchingKnowledgeOptions();
    }

    public async Task<MatchingKnowledge> GetKnowledgeAsync(CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetConfigAsync();
        var view = MatchingKnowledgeComposition.BuildView(entity, _defaultOptions);
        return MatchingKnowledgeComposition.ToDomainModel(view.Effective);
    }
}
