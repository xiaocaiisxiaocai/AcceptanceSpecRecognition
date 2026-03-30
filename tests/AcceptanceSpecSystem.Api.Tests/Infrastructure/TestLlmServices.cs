using System.Text.Json;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.Matching.Services;

namespace AcceptanceSpecSystem.Api.Tests.Infrastructure;

public class TestLlmReviewService : ILlmReviewService
{
    private const string ReviewJson = "{\"score\":40,\"reason\":\"低分原因\",\"commentary\":\"对比关键字段\"}";

    public Task<LlmReviewResult?> ReviewAsync(LlmReviewRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LlmReviewResult?>(new LlmReviewResult
        {
            Score = 40,
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

public class TestLlmEntityResolutionService : ILlmEntityResolutionService
{
    public Task<LlmEntityResolutionResult?> ResolveAsync(
        LlmEntityResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var source = request.SourceEntity.Trim();
        var candidate = request.CandidateEntity.Trim();
        var sourceKey = source.ToLowerInvariant();
        var candidateKey = candidate.ToLowerInvariant();

        LlmEntityResolutionResult result = (sourceKey, candidateKey) switch
        {
            ("panasonic", "松下") or ("松下", "panasonic") => new LlmEntityResolutionResult
            {
                Relation = LlmEntityRelation.AliasSame,
                Confidence = 0.95,
                NormalizedEntity = "松下",
                Reason = "Panasonic 与 松下是同一品牌的中英文名称"
            },
            ("alphatech", "阿尔法科技") or ("阿尔法科技", "alphatech") => new LlmEntityResolutionResult
            {
                Relation = LlmEntityRelation.AliasSame,
                Confidence = 0.93,
                NormalizedEntity = "阿尔法科技",
                Reason = "AlphaTech 与 阿尔法科技可视为同一品牌的英文名与中文名"
            },
            ("alphatech", "betamotion") or ("betamotion", "alphatech") => new LlmEntityResolutionResult
            {
                Relation = LlmEntityRelation.Conflict,
                Confidence = 0.95,
                Reason = "AlphaTech 与 BetaMotion 为不同品牌"
            },
            ("xjtech", "新境科技") or ("新境科技", "xjtech") => new LlmEntityResolutionResult
            {
                Relation = LlmEntityRelation.Unknown,
                Confidence = 0.55,
                Reason = "缺少足够证据确认两者是否为同一品牌"
            },
            _ when string.Equals(source, candidate, StringComparison.OrdinalIgnoreCase) => new LlmEntityResolutionResult
            {
                Relation = LlmEntityRelation.Same,
                Confidence = 0.99,
                NormalizedEntity = source,
                Reason = "实体名称一致"
            },
            _ => new LlmEntityResolutionResult
            {
                Relation = LlmEntityRelation.Unknown,
                Confidence = 0.5,
                Reason = "测试环境中未配置该实体关系"
            }
        };

        return Task.FromResult<LlmEntityResolutionResult?>(result);
    }

    public bool TryParseEntityResolutionResult(string raw, out LlmEntityResolutionResult result)
    {
        result = null!;
        using var doc = JsonDocument.Parse(raw);
        var relationText = doc.RootElement.GetProperty("relation").GetString();
        var confidence = doc.RootElement.GetProperty("confidence").GetDouble();
        var normalizedEntity = doc.RootElement.TryGetProperty("normalizedEntity", out var normalized)
            ? normalized.GetString()
            : null;
        var reason = doc.RootElement.TryGetProperty("reason", out var reasonElement)
            ? reasonElement.GetString()
            : null;

        result = new LlmEntityResolutionResult
        {
            Relation = relationText?.ToLowerInvariant() switch
            {
                "same" => LlmEntityRelation.Same,
                "alias_same" => LlmEntityRelation.AliasSame,
                "conflict" => LlmEntityRelation.Conflict,
                _ => LlmEntityRelation.Unknown
            },
            Confidence = confidence,
            NormalizedEntity = normalizedEntity,
            Reason = reason
        };
        return true;
    }
}

public class TestMatchingKnowledgeDraftAiService : IMatchingKnowledgeDraftAiService
{
    public Task<IReadOnlyList<MatchingKnowledgeDraftCandidate>> GenerateAsync(
        MatchingKnowledgeDraftAiRequest request,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MatchingKnowledgeDraftCandidate> result = request.Category switch
        {
            MatchingKnowledgeDraftGenerationService.CategoryEntityAliases =>
            [
                new MatchingKnowledgeDraftCandidate
                {
                    Key = "Panasonic品牌",
                    Value = "松下",
                    EvidenceSnippet = "Panasonic 品牌",
                    Reason = "命中品牌中英文对应关系"
                },
                new MatchingKnowledgeDraftCandidate
                {
                    Key = "ABB",
                    Value = "ABB",
                    EvidenceSnippet = "ABB 控制柜",
                    Reason = "命中品牌原文"
                }
            ],
            MatchingKnowledgeDraftGenerationService.CategoryUnitAliases =>
            [
                new MatchingKnowledgeDraftCandidate
                {
                    Key = "公分",
                    Value = "cm",
                    EvidenceSnippet = "尺寸 10 公分",
                    Reason = "命中常见长度单位别名"
                }
            ],
            MatchingKnowledgeDraftGenerationService.CategoryFieldAliases =>
            [
                new MatchingKnowledgeDraftCandidate
                {
                    Key = "宽尺寸",
                    Value = "宽度",
                    EvidenceSnippet = "宽尺寸 200mm",
                    Reason = "命中字段别名"
                }
            ],
            MatchingKnowledgeDraftGenerationService.CategoryConflictPairs =>
            [
                new MatchingKnowledgeDraftCandidate
                {
                    Key = "正转",
                    Value = "反转",
                    EvidenceSnippet = "支持正转/反转",
                    Reason = "命中明确互斥的方向词"
                }
            ],
            _ => []
        };

        return Task.FromResult(result);
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

/// <summary>
/// 测试用文本相似度服务（复用 Levenshtein 实现）
/// </summary>
public class TestTextSimilarityService : ITextSimilarityService
{
    private readonly TextSimilarityService _inner = new();

    public double ComputeSimilarity(string text1, string text2)
    {
        return _inner.ComputeSimilarity(text1, text2);
    }
}
