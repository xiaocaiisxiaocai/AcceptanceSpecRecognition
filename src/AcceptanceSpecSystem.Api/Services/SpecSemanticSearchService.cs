using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 验收规格语义搜索服务
/// </summary>
public sealed class SpecSemanticSearchService
{
    private const int DefaultTopK = 5;
    private const int MaxTopK = 20;
    private const int MaxQueryCount = 30;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmbeddingService _embeddingService;
    private readonly AiServiceSelector _aiServiceSelector;
    private readonly ILogger<SpecSemanticSearchService> _logger;

    public SpecSemanticSearchService(
        IUnitOfWork unitOfWork,
        IEmbeddingService embeddingService,
        AiServiceSelector aiServiceSelector,
        ILogger<SpecSemanticSearchService> logger)
    {
        _unitOfWork = unitOfWork;
        _embeddingService = embeddingService;
        _aiServiceSelector = aiServiceSelector;
        _logger = logger;
    }

    public async Task<SpecSemanticSearchResponse> SearchAsync(
        SpecSemanticSearchRequest request,
        DataScopeResult scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(scope);

        var queries = NormalizeQueries(request.Queries);
        if (queries.Count == 0)
            throw new ArgumentException("请至少输入一条搜索内容");

        if (queries.Count > MaxQueryCount)
            throw new ArgumentException($"单次最多支持 {MaxQueryCount} 条搜索内容");

        var topK = request.TopK <= 0 ? DefaultTopK : Math.Min(request.TopK, MaxTopK);
        var minScore = Math.Clamp(request.MinScore, 0, 1);

        var allSpecs = await _unitOfWork.AcceptanceSpecs.GetAllWithCustomerAndProcessAsync();
        var scopedSpecs = SpecDataScopeHelper.ApplyScope(allSpecs, scope);
        var filteredSpecs = ApplyFilters(
            scopedSpecs,
            request.CustomerId,
            request.ProcessId,
            request.MachineModelId,
            request.ProcessIdIsNull,
            request.MachineModelIdIsNull);

        var response = new SpecSemanticSearchResponse
        {
            QueryCount = queries.Count,
            CandidateCount = filteredSpecs.Count
        };

        if (filteredSpecs.Count == 0)
        {
            response.Groups = queries
                .Select((queryText, index) => new SpecSemanticSearchGroupDto
                {
                    QueryIndex = index,
                    QueryText = queryText,
                    TotalHits = 0,
                    Items = []
                })
                .ToList();
            return response;
        }

        var embeddingModel = await ResolveEmbeddingModelNameAsync(request.EmbeddingServiceId);
        response.EmbeddingModel = embeddingModel;

        var candidates = filteredSpecs
            .Select(spec => new SpecSemanticCandidate
            {
                Spec = spec,
                SearchText = BuildSearchText(spec)
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(embeddingModel))
        {
            await LoadCachedEmbeddingsAsync(candidates, embeddingModel);
        }

        await EnsureCandidateEmbeddingsAsync(
            candidates,
            request.EmbeddingServiceId,
            embeddingModel,
            cancellationToken);

        var queryEmbeddings = await _embeddingService.GenerateEmbeddingsAsync(
            queries,
            request.EmbeddingServiceId,
            cancellationToken);

        response.Groups = queries
            .Select((queryText, index) =>
            {
                var queryEmbedding = index < queryEmbeddings.Count
                    ? queryEmbeddings[index]
                    : Array.Empty<float>();

                var scoredItems = candidates
                    .Select(candidate => new
                    {
                        Candidate = candidate,
                        Score = _embeddingService.ComputeSimilarity(
                            queryEmbedding,
                            candidate.Embedding ?? Array.Empty<float>())
                    })
                    .Where(item => item.Score >= minScore)
                    .OrderByDescending(item => item.Score)
                    .ThenByDescending(item => !string.IsNullOrWhiteSpace(item.Candidate.Spec.Acceptance))
                    .ThenByDescending(item => !string.IsNullOrWhiteSpace(item.Candidate.Spec.Remark))
                    .ThenByDescending(item => item.Candidate.Spec.ImportedAt)
                    .ThenByDescending(item => item.Candidate.Spec.Id)
                    .ToList();

                return new SpecSemanticSearchGroupDto
                {
                    QueryIndex = index,
                    QueryText = queryText,
                    TotalHits = scoredItems.Count,
                    Items = scoredItems
                        .Take(topK)
                        .Select(item => MapToItemDto(item.Candidate.Spec, item.Score))
                        .ToList()
                };
            })
            .ToList();

        _logger.LogInformation(
            "验收规格语义搜索完成: queries={QueryCount}, candidates={CandidateCount}, model={Model}",
            queries.Count,
            candidates.Count,
            response.EmbeddingModel ?? "N/A");

        return response;
    }

    private async Task<string?> ResolveEmbeddingModelNameAsync(int? embeddingServiceId)
    {
        var configs = await _aiServiceSelector.GetCandidatesAsync(AiServicePurpose.Embedding, embeddingServiceId);
        return configs.FirstOrDefault()?.EmbeddingModel?.Trim();
    }

    private async Task LoadCachedEmbeddingsAsync(
        List<SpecSemanticCandidate> candidates,
        string embeddingModel)
    {
        var caches = await _unitOfWork.EmbeddingCaches.GetBySpecIdsAndModelAsync(
            candidates.Select(candidate => candidate.Spec.Id),
            embeddingModel);

        var cacheLookup = caches.ToDictionary(cache => cache.SpecId);
        foreach (var candidate in candidates)
        {
            if (cacheLookup.TryGetValue(candidate.Spec.Id, out var cache))
            {
                candidate.Cache = cache;
                candidate.Embedding = DeserializeVector(cache.Vector);
            }
        }
    }

    private async Task EnsureCandidateEmbeddingsAsync(
        List<SpecSemanticCandidate> candidates,
        int? embeddingServiceId,
        string? embeddingModel,
        CancellationToken cancellationToken)
    {
        var missingCandidates = candidates
            .Where(candidate => candidate.Embedding == null || candidate.Embedding.Length == 0)
            .ToList();

        if (missingCandidates.Count == 0)
            return;

        var embeddings = await _embeddingService.GenerateEmbeddingsAsync(
            missingCandidates.Select(candidate => candidate.SearchText),
            embeddingServiceId,
            cancellationToken);

        var hasCacheMutation = false;
        for (var index = 0; index < missingCandidates.Count; index++)
        {
            var embedding = index < embeddings.Count ? embeddings[index] : Array.Empty<float>();
            missingCandidates[index].Embedding = embedding;

            if (!string.IsNullOrWhiteSpace(embeddingModel) && embedding.Length > 0)
            {
                if (missingCandidates[index].Cache != null)
                {
                    missingCandidates[index].Cache!.Vector = SerializeVector(embedding);
                    missingCandidates[index].Cache!.CreatedAt = DateTime.Now;
                    _unitOfWork.EmbeddingCaches.Update(missingCandidates[index].Cache!);
                }
                else
                {
                    await _unitOfWork.EmbeddingCaches.AddAsync(new EmbeddingCache
                    {
                        SpecId = missingCandidates[index].Spec.Id,
                        ModelName = embeddingModel,
                        Vector = SerializeVector(embedding),
                        CreatedAt = DateTime.Now
                    });
                }

                hasCacheMutation = true;
            }
        }

        if (hasCacheMutation)
        {
            await _unitOfWork.SaveChangesAsync();
        }
    }

    private static List<string> NormalizeQueries(IEnumerable<string>? queries)
    {
        if (queries == null)
            return [];

        return queries
            .Select(query => (query ?? string.Empty).Trim())
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .ToList();
    }

    private static IReadOnlyList<AcceptanceSpec> ApplyFilters(
        IEnumerable<AcceptanceSpec> specs,
        int? customerId,
        int? processId,
        int? machineModelId,
        bool? processIdIsNull,
        bool? machineModelIdIsNull)
    {
        var query = specs;

        if (processId.HasValue)
        {
            query = query.Where(spec => spec.ProcessId == processId.Value);
        }
        else if (processIdIsNull == true)
        {
            query = query.Where(spec => spec.ProcessId == null);
        }

        if (machineModelId.HasValue)
        {
            query = query.Where(spec => spec.MachineModelId == machineModelId.Value);
        }
        else if (machineModelIdIsNull == true)
        {
            query = query.Where(spec => spec.MachineModelId == null);
        }

        if (customerId.HasValue)
        {
            query = query.Where(spec => spec.CustomerId == customerId.Value);
        }

        return query.ToList();
    }

    private static string BuildSearchText(AcceptanceSpec spec)
    {
        return string.Join(
            "\n",
            new[]
            {
                spec.Project?.Trim(),
                spec.Specification?.Trim(),
                spec.Acceptance?.Trim(),
                spec.Remark?.Trim()
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static SpecSemanticSearchItemDto MapToItemDto(AcceptanceSpec spec, double score)
    {
        return new SpecSemanticSearchItemDto
        {
            Id = spec.Id,
            CustomerId = spec.CustomerId,
            ProcessId = spec.ProcessId,
            MachineModelId = spec.MachineModelId,
            ProcessName = spec.Process?.Name ?? string.Empty,
            MachineModelName = spec.MachineModel?.Name ?? string.Empty,
            CustomerName = spec.Customer?.Name ?? string.Empty,
            Project = spec.Project,
            Specification = spec.Specification,
            Acceptance = spec.Acceptance,
            Remark = spec.Remark,
            ImportedAt = spec.ImportedAt,
            OwnerOrgUnitId = spec.OwnerOrgUnitId,
            CreatedByUserId = spec.CreatedByUserId,
            Score = score
        };
    }

    private static byte[] SerializeVector(float[] vector)
    {
        if (vector.Length == 0)
            return Array.Empty<byte>();

        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] DeserializeVector(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0 || bytes.Length % sizeof(float) != 0)
            return Array.Empty<float>();

        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }

    private sealed class SpecSemanticCandidate
    {
        public required AcceptanceSpec Spec { get; init; }

        public required string SearchText { get; init; }

        public EmbeddingCache? Cache { get; set; }

        public float[]? Embedding { get; set; }
    }
}
