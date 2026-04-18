using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Core.Matching.Interfaces;

public interface ILlmReviewService
{
    Task<LlmReviewResult?> ReviewAsync(LlmReviewRequest request, CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> ReviewStreamAsync(
        LlmReviewRequest request,
        CancellationToken cancellationToken = default);

    bool TryParseReviewResult(string raw, out LlmReviewResult result);
}

public interface ILlmEquivalenceAdjudicationService
{
    Task<LlmEquivalenceAdjudicationResult?> AdjudicateAsync(
        LlmEquivalenceAdjudicationRequest request,
        CancellationToken cancellationToken = default);

    bool TryParseAdjudicationResult(string raw, out LlmEquivalenceAdjudicationResult result);
}

public interface ILlmCandidateRerankService
{
    Task<LlmCandidateRerankResult?> RerankAsync(
        LlmCandidateRerankRequest request,
        CancellationToken cancellationToken = default);

    bool TryParseRerankResult(string raw, out LlmCandidateRerankResult result);
}
