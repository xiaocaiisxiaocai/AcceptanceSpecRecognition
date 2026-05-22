using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

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
    private readonly SpecEmbeddingCacheService _specEmbeddingCacheService;
    private readonly ILogger<SpecSemanticSearchService> _logger;

    public SpecSemanticSearchService(
        IUnitOfWork unitOfWork,
        IEmbeddingService embeddingService,
        SpecEmbeddingCacheService specEmbeddingCacheService,
        ILogger<SpecSemanticSearchService> logger)
    {
        _unitOfWork = unitOfWork;
        _embeddingService = embeddingService;
        _specEmbeddingCacheService = specEmbeddingCacheService;
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

        // 在数据库层面应用数据范围 + 过滤条件，避免全量加载后在内存中过滤
        // 注意：先在裸 Query 上过滤，再 Attach Include，避免 IIncludableQueryable 类型退化
        var baseQuery = SpecDataScopeHelper.ApplyScopeToQuery(
            _unitOfWork.AcceptanceSpecs.Query(),
            scope);

        if (request.CustomerId.HasValue)
            baseQuery = baseQuery.Where(s => s.CustomerId == request.CustomerId.Value);

        if (request.ProcessId.HasValue)
            baseQuery = baseQuery.Where(s => s.ProcessId == request.ProcessId.Value);
        else if (request.ProcessIdIsNull == true)
            baseQuery = baseQuery.Where(s => s.ProcessId == null);

        if (request.MachineModelId.HasValue)
            baseQuery = baseQuery.Where(s => s.MachineModelId == request.MachineModelId.Value);
        else if (request.MachineModelIdIsNull == true)
            baseQuery = baseQuery.Where(s => s.MachineModelId == null);

        var filteredSpecs = await baseQuery
            .Include(s => s.Customer)
            .Include(s => s.Process)
            .Include(s => s.MachineModel)
            .ToListAsync();

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

        var embeddingModel = await _specEmbeddingCacheService.ResolveEmbeddingModelNameAsync(
            request.EmbeddingServiceId,
            cancellationToken);
        response.EmbeddingModel = embeddingModel;

        var candidates = filteredSpecs
            .Select(spec => new SpecSemanticCandidate
            {
                Spec = spec,
                SearchText = BuildSearchText(spec)
            })
            .ToList();

        var cachedEmbeddings = await _specEmbeddingCacheService.GetOrCreateForSpecsAsync(
            filteredSpecs,
            EmbeddingCacheUsages.SemanticSearch,
            request.EmbeddingServiceId,
            cancellationToken);
        var embeddingLookup = cachedEmbeddings.ToDictionary(item => item.SpecId);
        foreach (var candidate in candidates)
        {
            if (embeddingLookup.TryGetValue(candidate.Spec.Id, out var embedding))
            {
                candidate.Embedding = embedding.Embedding;
            }
        }

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

    private sealed class SpecSemanticCandidate
    {
        public required AcceptanceSpec Spec { get; init; }

        public required string SearchText { get; init; }

        public float[]? Embedding { get; set; }
    }
}
