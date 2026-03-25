using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 导入阶段疑似重复识别服务
/// </summary>
public sealed class ImportDuplicateDetectionService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ILlmReviewService _llmReviewService;
    private readonly ILogger<ImportDuplicateDetectionService> _logger;

    public ImportDuplicateDetectionService(
        IEmbeddingService embeddingService,
        ILlmReviewService llmReviewService,
        ILogger<ImportDuplicateDetectionService> logger)
    {
        _embeddingService = embeddingService;
        _llmReviewService = llmReviewService;
        _logger = logger;
    }

    public async Task<ImportDuplicateDetectionSession> CreateSessionAsync(
        IReadOnlyCollection<AcceptanceSpec> existingSpecs,
        ImportDuplicateCheckOptions? options,
        CancellationToken cancellationToken = default)
    {
        var normalizedOptions = NormalizeOptions(options);
        if (!normalizedOptions.EnableSemanticDuplicateCheck || existingSpecs.Count == 0)
        {
            return ImportDuplicateDetectionSession.Disabled(normalizedOptions);
        }

        var candidates = existingSpecs
            .Where(spec => spec.Id > 0)
            .Select(spec => new CandidateState
            {
                Spec = spec,
                SearchText = BuildSearchText(spec.Project, spec.Specification)
            })
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.SearchText))
            .ToList();

        if (candidates.Count == 0)
        {
            return ImportDuplicateDetectionSession.Disabled(normalizedOptions);
        }

        var embeddings = await _embeddingService.GenerateEmbeddingsAsync(
            candidates.Select(candidate => candidate.SearchText),
            normalizedOptions.EmbeddingServiceId,
            cancellationToken);

        for (var index = 0; index < candidates.Count; index++)
        {
            candidates[index].Embedding = index < embeddings.Count
                ? embeddings[index]
                : Array.Empty<float>();
        }

        _logger.LogInformation(
            "导入疑似重复识别候选集已准备完成: count={Count}, topK={TopK}, minScore={MinScore}, llm={UseLlm}",
            candidates.Count,
            normalizedOptions.SemanticTopK,
            normalizedOptions.SemanticMinScore,
            normalizedOptions.EnableLlmDuplicateReview);

        return new ImportDuplicateDetectionSession(
            normalizedOptions,
            candidates,
            _embeddingService,
            _llmReviewService,
            _logger);
    }

    private static ImportDuplicateCheckOptions NormalizeOptions(ImportDuplicateCheckOptions? source)
    {
        var current = source ?? new ImportDuplicateCheckOptions();
        return new ImportDuplicateCheckOptions
        {
            EnableSemanticDuplicateCheck = current.EnableSemanticDuplicateCheck,
            EmbeddingServiceId = current.EmbeddingServiceId,
            SemanticTopK = Math.Clamp(current.SemanticTopK <= 0 ? 3 : current.SemanticTopK, 1, 10),
            SemanticMinScore = Math.Clamp(current.SemanticMinScore, 0, 1),
            EnableLlmDuplicateReview = current.EnableLlmDuplicateReview,
            LlmServiceId = current.LlmServiceId,
            LlmPassScore = Math.Clamp(current.LlmPassScore, 0, 1),
            HighConfidenceThreshold = Math.Clamp(current.HighConfidenceThreshold <= 0 ? 0.95 : current.HighConfidenceThreshold, 0, 1)
        };
    }

    internal static string BuildSearchText(string? project, string? specification)
    {
        return string.Join(
            "\n",
            new[]
            {
                project?.Trim(),
                specification?.Trim()
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    internal sealed class CandidateState
    {
        public required AcceptanceSpec Spec { get; init; }

        public string SearchText { get; set; } = string.Empty;

        public float[] Embedding { get; set; } = Array.Empty<float>();
    }
}

/// <summary>
/// 导入阶段疑似重复识别会话
/// </summary>
public sealed class ImportDuplicateDetectionSession
{
    private readonly ImportDuplicateCheckOptions _options;
    private readonly List<ImportDuplicateDetectionService.CandidateState> _candidates;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILlmReviewService _llmReviewService;
    private readonly ILogger _logger;
    private readonly Dictionary<string, float[]> _queryEmbeddingCache = new(StringComparer.Ordinal);

    internal ImportDuplicateDetectionSession(
        ImportDuplicateCheckOptions options,
        List<ImportDuplicateDetectionService.CandidateState> candidates,
        IEmbeddingService embeddingService,
        ILlmReviewService llmReviewService,
        ILogger logger)
    {
        _options = options;
        _candidates = candidates;
        _embeddingService = embeddingService;
        _llmReviewService = llmReviewService;
        _logger = logger;
    }

    public bool IsEnabled => _options.EnableSemanticDuplicateCheck && _candidates.Count > 0;

    public ImportDuplicateCheckOptions Options => _options;

    public static ImportDuplicateDetectionSession Disabled(ImportDuplicateCheckOptions options)
    {
        return new ImportDuplicateDetectionSession(
            options,
            [],
            DisabledEmbeddingService.Instance,
            DisabledLlmReviewService.Instance,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
    }

    public async Task<ImportSemanticDuplicateMatch?> DetectAsync(
        string project,
        string specification,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return null;
        }

        var queryText = ImportDuplicateDetectionService.BuildSearchText(project, specification);
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return null;
        }

        if (!_queryEmbeddingCache.TryGetValue(queryText, out var queryEmbedding))
        {
            queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(
                queryText,
                _options.EmbeddingServiceId,
                cancellationToken);
            _queryEmbeddingCache[queryText] = queryEmbedding;
        }

        var recalled = _candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = _embeddingService.ComputeSimilarity(queryEmbedding, candidate.Embedding)
            })
            .Where(item => item.Score >= _options.SemanticMinScore)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Candidate.Spec.ImportedAt)
            .ThenByDescending(item => item.Candidate.Spec.Id)
            .Take(_options.SemanticTopK)
            .ToList();

        if (recalled.Count == 0)
        {
            return null;
        }

        if (!_options.EnableLlmDuplicateReview)
        {
            var best = recalled[0];
            return new ImportSemanticDuplicateMatch
            {
                ExistingSpec = best.Candidate.Spec,
                EmbeddingScore = best.Score,
                FinalScore = best.Score,
                IsHighConfidence = best.Score >= _options.HighConfidenceThreshold
            };
        }

        ImportSemanticDuplicateMatch? bestPassingMatch = null;
        foreach (var item in recalled)
        {
            var review = await _llmReviewService.ReviewAsync(
                new LlmReviewRequest
                {
                    SourceProject = project,
                    SourceSpecification = specification,
                    BestMatchProject = item.Candidate.Spec.Project,
                    BestMatchSpecification = item.Candidate.Spec.Specification,
                    BestMatchAcceptance = item.Candidate.Spec.Acceptance,
                    BestMatchRemark = item.Candidate.Spec.Remark,
                    BaseScore = item.Score * 100,
                    ScoreDetails = new Dictionary<string, double>
                    {
                        ["Embedding"] = item.Score * 100
                    },
                    LlmServiceId = _options.LlmServiceId,
                    ReviewScene = LlmReviewScene.ImportDuplicateReview
                },
                cancellationToken);

            if (review == null)
            {
                continue;
            }

            var llmScore = NormalizeReviewScore(review.Score);
            if (llmScore < _options.LlmPassScore)
            {
                continue;
            }

            var candidateMatch = new ImportSemanticDuplicateMatch
            {
                ExistingSpec = item.Candidate.Spec,
                EmbeddingScore = item.Score,
                LlmScore = llmScore,
                FinalScore = llmScore,
                ReviewReason = review.Reason,
                ReviewCommentary = review.Commentary,
                IsHighConfidence = llmScore >= _options.HighConfidenceThreshold
            };

            if (bestPassingMatch == null ||
                candidateMatch.FinalScore > bestPassingMatch.FinalScore ||
                (Math.Abs(candidateMatch.FinalScore - bestPassingMatch.FinalScore) < 0.0001 &&
                 candidateMatch.EmbeddingScore > bestPassingMatch.EmbeddingScore))
            {
                bestPassingMatch = candidateMatch;
            }
        }

        if (bestPassingMatch != null)
        {
            _logger.LogDebug(
                "导入疑似重复识别命中语义候选: specId={SpecId}, embedding={EmbeddingScore:F4}, final={FinalScore:F4}",
                bestPassingMatch.ExistingSpec.Id,
                bestPassingMatch.EmbeddingScore,
                bestPassingMatch.FinalScore);
        }

        return bestPassingMatch;
    }

    public async Task RefreshCandidateAsync(AcceptanceSpec spec, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || spec.Id <= 0)
        {
            return;
        }

        var candidate = _candidates.FirstOrDefault(item => item.Spec.Id == spec.Id);
        if (candidate == null)
        {
            return;
        }

        candidate.SearchText = ImportDuplicateDetectionService.BuildSearchText(spec.Project, spec.Specification);
        if (string.IsNullOrWhiteSpace(candidate.SearchText))
        {
            candidate.Embedding = Array.Empty<float>();
            return;
        }

        if (_queryEmbeddingCache.TryGetValue(candidate.SearchText, out var cachedEmbedding))
        {
            candidate.Embedding = cachedEmbedding;
            return;
        }

        candidate.Embedding = await _embeddingService.GenerateEmbeddingAsync(
            candidate.SearchText,
            _options.EmbeddingServiceId,
            cancellationToken);
    }

    private static double NormalizeReviewScore(double rawScore)
    {
        if (rawScore <= 0)
        {
            return 0;
        }

        return rawScore > 1 ? Math.Clamp(rawScore / 100d, 0, 1) : Math.Clamp(rawScore, 0, 1);
    }

    private sealed class DisabledEmbeddingService : IEmbeddingService
    {
        public static DisabledEmbeddingService Instance { get; } = new();

        public bool IsAvailable => false;

        public Task<float[]> GenerateEmbeddingAsync(string text, int? serviceId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Array.Empty<float>());

        public Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, int? serviceId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<float[]>());

        public double ComputeSimilarity(float[] embedding1, float[] embedding2) => 0;
    }

    private sealed class DisabledLlmReviewService : ILlmReviewService
    {
        public static DisabledLlmReviewService Instance { get; } = new();

        public Task<LlmReviewResult?> ReviewAsync(LlmReviewRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult<LlmReviewResult?>(null);

        public async IAsyncEnumerable<string> ReviewStreamAsync(
            LlmReviewRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public bool TryParseReviewResult(string raw, out LlmReviewResult result)
        {
            result = null!;
            return false;
        }
    }
}

/// <summary>
/// 语义命中结果
/// </summary>
public sealed class ImportSemanticDuplicateMatch
{
    public required AcceptanceSpec ExistingSpec { get; init; }

    public double EmbeddingScore { get; init; }

    public double? LlmScore { get; init; }

    public double FinalScore { get; init; }

    public bool IsHighConfidence { get; init; }

    public string? ReviewReason { get; init; }

    public string? ReviewCommentary { get; init; }
}
