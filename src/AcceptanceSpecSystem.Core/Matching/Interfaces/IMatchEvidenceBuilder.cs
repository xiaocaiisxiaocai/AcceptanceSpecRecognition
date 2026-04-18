using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Core.Matching.Interfaces;

/// <summary>
/// 匹配证据构建器
/// </summary>
public interface IMatchEvidenceBuilder
{
    MatchEvidence Build(MatchSource source, MatchCandidate candidate);
}
