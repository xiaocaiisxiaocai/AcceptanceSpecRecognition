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

public interface ILlmSuggestionService
{
    Task<LlmSuggestionResult?> GenerateSuggestionAsync(LlmSuggestionRequest request, CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> GenerateSuggestionStreamAsync(
        LlmSuggestionRequest request,
        CancellationToken cancellationToken = default);

    bool TryParseSuggestionResult(string raw, out LlmSuggestionResult result);
}

public interface ILlmEntityResolutionService
{
    Task<LlmEntityResolutionResult?> ResolveAsync(
        LlmEntityResolutionRequest request,
        CancellationToken cancellationToken = default);

    bool TryParseEntityResolutionResult(string raw, out LlmEntityResolutionResult result);
}

public interface ILlmEquivalenceAdjudicationService
{
    Task<LlmEquivalenceAdjudicationResult?> AdjudicateAsync(
        LlmEquivalenceAdjudicationRequest request,
        CancellationToken cancellationToken = default);

    bool TryParseAdjudicationResult(string raw, out LlmEquivalenceAdjudicationResult result);
}
