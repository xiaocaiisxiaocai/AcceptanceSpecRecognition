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

public class TestEmbeddingService : IEmbeddingService
{
    public bool IsAvailable => true;

    public Task<float[]> GenerateEmbeddingAsync(string text, int? serviceId = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CreateVector(text));
    }

    public Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, int? serviceId = null, CancellationToken cancellationToken = default)
    {
        var vectors = texts.Select(CreateVector).ToList();
        return Task.FromResult(vectors);
    }

    public double ComputeSimilarity(float[] embedding1, float[] embedding2)
    {
        if (embedding1.Length == 0 || embedding2.Length == 0 || embedding1.Length != embedding2.Length)
            return 0;

        double dot = 0;
        double norm1 = 0;
        double norm2 = 0;
        for (var i = 0; i < embedding1.Length; i++)
        {
            dot += embedding1[i] * embedding2[i];
            norm1 += embedding1[i] * embedding1[i];
            norm2 += embedding2[i] * embedding2[i];
        }

        if (norm1 <= 0 || norm2 <= 0)
            return 0;

        var score = dot / (Math.Sqrt(norm1) * Math.Sqrt(norm2));
        return Math.Clamp(score, 0, 1);
    }

    private static float[] CreateVector(string text)
    {
        var value = text ?? string.Empty;
        var vector = new float[16];

        for (var i = 0; i < value.Length; i++)
        {
            var bucket = i % vector.Length;
            vector[bucket] += value[i];
        }

        var norm = (float)Math.Sqrt(vector.Sum(v => v * v));
        if (norm <= 0)
            return vector;

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= norm;
        }

        return vector;
    }
}
