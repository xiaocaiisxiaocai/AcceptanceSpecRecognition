using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Core.Matching.Interfaces;

public interface IMatchingKnowledgeProvider
{
    Task<MatchingKnowledge> GetKnowledgeAsync(CancellationToken cancellationToken = default);
}
