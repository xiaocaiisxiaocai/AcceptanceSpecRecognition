using System.Text.Json;
using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.Matching.Services;

namespace AcceptanceSpecSystem.Api.Tests.Infrastructure;

public class TestLlmReviewService : ILlmReviewService
{
    private const string LowScoreReviewJson = "{\"score\":40,\"reason\":\"低分原因\",\"commentary\":\"对比关键字段\"}";
    private const string HighScoreReviewJson = "{\"score\":95,\"reason\":\"结构化证据支持自动采用\",\"commentary\":\"项目与规格可视为等价表达\"}";

    public Task<LlmReviewResult?> ReviewAsync(LlmReviewRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LlmReviewResult?>(CreateResult(request));
    }

    public async IAsyncEnumerable<string> ReviewStreamAsync(
        LlmReviewRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var json = ShouldApprove(request) ? HighScoreReviewJson : LowScoreReviewJson;
        yield return json[..10];
        yield return json[10..];
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

    private static bool ShouldApprove(LlmReviewRequest request)
    {
        return string.Equals(request.SourceProject, ReviewScenarioSamples.ApprovedSourceProject, StringComparison.Ordinal) &&
               string.Equals(request.SourceSpecification, ReviewScenarioSamples.ApprovedSourceSpecification, StringComparison.Ordinal) &&
               string.Equals(request.BestMatchProject, ReviewScenarioSamples.ApprovedBestProject, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(request.BestMatchSpecification, ReviewScenarioSamples.ApprovedBestSpecification, StringComparison.OrdinalIgnoreCase);
    }

    private static LlmReviewResult CreateResult(LlmReviewRequest request)
    {
        if (ShouldApprove(request))
        {
            return new LlmReviewResult
            {
                Score = 95,
                Reason = "结构化证据支持自动采用",
                Commentary = "项目与规格可视为等价表达"
            };
        }

        return new LlmReviewResult
        {
            Score = 40,
            Reason = "低分原因",
            Commentary = "对比关键字段"
        };
    }
}

public class TestLlmEquivalenceAdjudicationService : ILlmEquivalenceAdjudicationService
{
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled);

    public Task<LlmEquivalenceAdjudicationResult?> AdjudicateAsync(
        LlmEquivalenceAdjudicationRequest request,
        CancellationToken cancellationToken = default)
    {
        var sourceProject = request.SourceProject.Trim();
        var candidateProject = request.CandidateProject.Trim();
        var sourceSpecification = request.SourceSpecification.Trim();
        var candidateSpecification = request.CandidateSpecification.Trim();

        LlmEquivalenceAdjudicationResult result =
            (sourceProject, sourceSpecification, candidateProject, candidateSpecification) switch
            {
                (ReviewScenarioSamples.ApprovedSourceProject,
                    ReviewScenarioSamples.ApprovedSourceSpecification,
                    ReviewScenarioSamples.ApprovedBestProject,
                    ReviewScenarioSamples.ApprovedBestSpecification) => new LlmEquivalenceAdjudicationResult
                {
                    Verdict = LlmEquivalenceVerdict.Equivalent,
                    ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
                    Confidence = 0.93,
                    Reason = "Dock-Bay 与 Dock Bay 仅是命名格式差异，可视为同一表达"
                },
                ("安装要求", "最大不可拆部件≈3200", "安装要求", "最大不可拆部件约等于3200。") => new LlmEquivalenceAdjudicationResult
                {
                    Verdict = LlmEquivalenceVerdict.Equivalent,
                    ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
                    Confidence = 0.92,
                    Reason = "≈ 与 约等于属于同义表达"
                },
                ("安装要求", "最大不可拆部件≈3200", "安装要求", "最大不可拆部件约为3200") => new LlmEquivalenceAdjudicationResult
                {
                    Verdict = LlmEquivalenceVerdict.Uncertain,
                    ReasonType = LlmEquivalenceReasonType.Uncertain,
                    Confidence = 0.45,
                    Reason = "上下文不足，无法确认是否完全等价"
                },
                _ when TextEqualsForFormatOnly(sourceProject, candidateProject) &&
                       TextEqualsForFormatOnly(sourceSpecification, candidateSpecification) => new LlmEquivalenceAdjudicationResult
                {
                    Verdict = LlmEquivalenceVerdict.Equivalent,
                    ReasonType = LlmEquivalenceReasonType.FormatOnly,
                    Confidence = 0.99,
                    Reason = "源项与候选项在规范化后文本一致"
                },
                _ => new LlmEquivalenceAdjudicationResult
                {
                    Verdict = LlmEquivalenceVerdict.Different,
                    ReasonType = LlmEquivalenceReasonType.SemanticDifference,
                    Confidence = 0.88,
                    Reason = "测试环境中未配置该等价关系"
                }
            };

        return Task.FromResult<LlmEquivalenceAdjudicationResult?>(result);
    }

    private static bool TextEqualsForFormatOnly(string left, string right)
    {
        return string.Equals(
            NormalizeForFormatOnly(left),
            NormalizeForFormatOnly(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeForFormatOnly(string text)
    {
        return WhitespaceRegex.Replace(
                text
                    .Replace("\u00A0", " ", StringComparison.Ordinal)
                    .Replace("\u200B", string.Empty, StringComparison.Ordinal)
                    .Replace("\uFEFF", string.Empty, StringComparison.Ordinal)
                    .Trim(),
                " ")
            .Replace("（", "(", StringComparison.Ordinal)
            .Replace("）", ")", StringComparison.Ordinal);
    }

    public bool TryParseAdjudicationResult(string raw, out LlmEquivalenceAdjudicationResult result)
    {
        result = null!;
        using var doc = JsonDocument.Parse(raw);
        var verdictText = doc.RootElement.GetProperty("verdict").GetString();
        var reasonTypeText = doc.RootElement.GetProperty("reasonType").GetString();
        var confidence = doc.RootElement.GetProperty("confidence").GetDouble();
        var reason = doc.RootElement.TryGetProperty("reason", out var reasonElement)
            ? reasonElement.GetString()
            : null;

        result = new LlmEquivalenceAdjudicationResult
        {
            Verdict = verdictText?.ToLowerInvariant() switch
            {
                "equivalent" => LlmEquivalenceVerdict.Equivalent,
                "different" => LlmEquivalenceVerdict.Different,
                _ => LlmEquivalenceVerdict.Uncertain
            },
            ReasonType = reasonTypeText?.ToLowerInvariant() switch
            {
                "format_only" => LlmEquivalenceReasonType.FormatOnly,
                "punctuation_only" => LlmEquivalenceReasonType.PunctuationOnly,
                "equivalent_expression" => LlmEquivalenceReasonType.EquivalentExpression,
                "symbol_equivalent" => LlmEquivalenceReasonType.SymbolEquivalent,
                "semantic_difference" => LlmEquivalenceReasonType.SemanticDifference,
                "symbol_conflict" => LlmEquivalenceReasonType.SymbolConflict,
                _ => LlmEquivalenceReasonType.Uncertain
            },
            Confidence = confidence,
            Reason = reason
        };
        return true;
    }
}

public class TestLlmCandidateRerankService : ILlmCandidateRerankService
{
    public Task<LlmCandidateRerankResult?> RerankAsync(
        LlmCandidateRerankRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<LlmCandidateRerankResult?>(new LlmCandidateRerankResult
        {
            SelectedSpecId = request.CurrentTopCandidateSpecId,
            Reason = "测试环境默认沿用本地 Top1",
            Confidence = 0.8
        });
    }

    public bool TryParseRerankResult(string raw, out LlmCandidateRerankResult result)
    {
        result = null!;
        using var doc = JsonDocument.Parse(raw);
        result = new LlmCandidateRerankResult
        {
            SelectedSpecId = doc.RootElement.GetProperty("selectedSpecId").GetInt32(),
            Reason = doc.RootElement.TryGetProperty("reason", out var reasonElement)
                ? reasonElement.GetString()
                : null,
            Confidence = doc.RootElement.GetProperty("confidence").GetDouble()
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

