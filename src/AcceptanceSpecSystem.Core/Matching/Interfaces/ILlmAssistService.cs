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
