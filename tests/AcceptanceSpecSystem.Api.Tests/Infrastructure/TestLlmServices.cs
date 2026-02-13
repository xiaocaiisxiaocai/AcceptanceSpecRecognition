using System.Text.Json;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Api.Tests.Infrastructure;

public class TestLlmReviewService : ILlmReviewService
{
    private const string ReviewJson = "{\"score\":0.4,\"reason\":\"低分原因\",\"commentary\":\"对比关键字段\"}";

    public Task<LlmReviewResult?> ReviewAsync(LlmReviewRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LlmReviewResult?>(new LlmReviewResult
        {
            Score = 0.4,
            Reason = "低分原因",
            Commentary = "对比关键字段"
        });
    }

    public async IAsyncEnumerable<string> ReviewStreamAsync(
        LlmReviewRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return ReviewJson[..10];
        yield return ReviewJson[10..];
    }

    public bool TryParseReviewResult(string raw, out LlmReviewResult result)
    {
        result = null!;
        using var doc = JsonDocument.Parse(raw);
        var score = doc.RootElement.GetProperty("score").GetDouble();
        var reason = doc.RootElement.GetProperty("reason").GetString();
        var commentary = doc.RootElement.GetProperty("commentary").GetString();
        result = new LlmReviewResult
        {
            Score = score,
            Reason = reason,
            Commentary = commentary
        };
        return true;
    }
}

public class TestLlmSuggestionService : ILlmSuggestionService
{
    private const string SuggestJson = "{\"acceptance\":\"LLM-AC\",\"remark\":\"LLM-REM\",\"reason\":\"LLM-REASON\"}";

    public Task<LlmSuggestionResult?> GenerateSuggestionAsync(LlmSuggestionRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LlmSuggestionResult?>(new LlmSuggestionResult
        {
            Acceptance = "LLM-AC",
            Remark = "LLM-REM",
            Reason = "LLM-REASON"
        });
    }

    public async IAsyncEnumerable<string> GenerateSuggestionStreamAsync(
        LlmSuggestionRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return SuggestJson[..12];
        yield return SuggestJson[12..];
    }

    public bool TryParseSuggestionResult(string raw, out LlmSuggestionResult result)
    {
        result = null!;
        using var doc = JsonDocument.Parse(raw);
        result = new LlmSuggestionResult
        {
            Acceptance = doc.RootElement.GetProperty("acceptance").GetString(),
            Remark = doc.RootElement.GetProperty("remark").GetString(),
            Reason = doc.RootElement.GetProperty("reason").GetString()
        };
        return true;
    }
}
