using System.Security.Cryptography;
using System.Text;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using CoreAiServicePurpose = AcceptanceSpecSystem.Core.AI.Models.AiServicePurpose;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 规格向量缓存服务：按用途和文本指纹隔离缓存，保留实时懒生成兜底。
/// </summary>
public sealed class SpecEmbeddingCacheService : IEmbeddingCacheWarmupExecutor
{
    private const int EmbeddingGenerationBatchSize = 200;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmbeddingService _embeddingService;
    private readonly IAiServiceSelector _aiServiceSelector;
    private readonly ILogger<SpecEmbeddingCacheService> _logger;

    public SpecEmbeddingCacheService(
        IUnitOfWork unitOfWork,
        IEmbeddingService embeddingService,
        IAiServiceSelector aiServiceSelector,
        ILogger<SpecEmbeddingCacheService> logger)
    {
        _unitOfWork = unitOfWork;
        _embeddingService = embeddingService;
        _aiServiceSelector = aiServiceSelector;
        _logger = logger;
    }

    public async Task<string?> ResolveEmbeddingModelNameAsync(
        int? embeddingServiceId,
        CancellationToken cancellationToken = default)
    {
        var configs = await _aiServiceSelector.GetCandidatesAsync(
            CoreAiServicePurpose.Embedding,
            embeddingServiceId,
            cancellationToken);
        return configs.FirstOrDefault()?.EmbeddingModel?.Trim();
    }

    public async Task HydrateMatchingCandidatesAsync(
        IReadOnlyCollection<MatchCandidate> candidates,
        int? embeddingServiceId,
        MatchingMode matchingMode = MatchingMode.ProjectSpecification,
        CancellationToken cancellationToken = default)
    {
        // 仅规格模式下源向量只用规格文本，候选缓存必须使用同一语料并独立隔离，
        // 否则会拿"项目+规格"向量与"纯规格"源向量比相似度，得分系统性失真。
        var specificationOnly = matchingMode == MatchingMode.SpecificationOnly;
        var targets = candidates
            .Where(candidate => candidate.SpecId > 0)
            .Select(candidate => new CacheTarget(
                candidate.SpecId,
                specificationOnly ? candidate.Specification : candidate.CombinedText,
                embedding => candidate.Embedding = embedding))
            .Where(target => !string.IsNullOrWhiteSpace(target.Text))
            .ToList();

        await HydrateTargetsAsync(
            targets,
            specificationOnly ? EmbeddingCacheUsages.MatchingSpecificationOnly : EmbeddingCacheUsages.Matching,
            embeddingServiceId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<SpecEmbeddingResult>> GetOrCreateForSpecsAsync(
        IReadOnlyCollection<AcceptanceSpec> specs,
        string usage,
        int? embeddingServiceId,
        CancellationToken cancellationToken = default)
    {
        var results = specs
            .Where(spec => spec.Id > 0)
            .Select(spec => new SpecEmbeddingResult(
                spec.Id,
                BuildText(spec, usage),
                Array.Empty<float>()))
            .Where(result => !string.IsNullOrWhiteSpace(result.Text))
            .ToList();

        var resultLookup = results.ToDictionary(result => result.SpecId);
        var targets = results
            .Select(result => new CacheTarget(
                result.SpecId,
                result.Text,
                embedding =>
                {
                    if (resultLookup.TryGetValue(result.SpecId, out var current))
                    {
                        resultLookup[result.SpecId] = current with { Embedding = embedding };
                    }
                }))
            .ToList();

        await HydrateTargetsAsync(targets, usage, embeddingServiceId, cancellationToken);

        return resultLookup.Values
            .OrderBy(result => result.SpecId)
            .ToList();
    }

    public async Task RemoveSpecCachesAsync(int specId)
    {
        var caches = await _unitOfWork.EmbeddingCaches.GetBySpecIdAsync(specId);
        if (caches.Count > 0)
        {
            _unitOfWork.EmbeddingCaches.RemoveRange(caches);
        }
    }

    public async Task RemoveSpecCachesAsync(IEnumerable<int> specIds)
    {
        foreach (var specId in specIds.Where(id => id > 0).Distinct())
        {
            await RemoveSpecCachesAsync(specId);
        }
    }

    public async Task WarmupAsync(
        int batchSize,
        int maxItemsPerRun,
        CancellationToken cancellationToken)
    {
        var embeddingModel = await ResolveEmbeddingModelNameAsync(null, cancellationToken);
        if (string.IsNullOrWhiteSpace(embeddingModel))
        {
            _logger.LogInformation("向量缓存预热跳过：未找到可用 Embedding 模型");
            return;
        }

        var maxItems = Math.Max(1, maxItemsPerRun);
        var scanBatchSize = Math.Max(Math.Min(maxItems, 5000), Math.Max(1, batchSize));
        var candidates = new List<AcceptanceSpec>();
        var lastScannedId = 0;

        while (candidates.Count < maxItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var specs = await _unitOfWork.AcceptanceSpecs
                .Query(asNoTracking: true)
                .Where(spec => spec.Id > lastScannedId)
                .OrderBy(spec => spec.Id)
                .Select(spec => new
                {
                    spec.Id,
                    spec.Project,
                    spec.Specification
                })
                .Take(scanBatchSize)
                .ToListAsync(cancellationToken);

            if (specs.Count == 0)
            {
                break;
            }

            lastScannedId = specs[^1].Id;

            // 分步查询缓存，避免 Pomelo 在旧 MySQL 兼容级别下为导航集合 FirstOrDefault 生成 ROW_NUMBER 窗口函数。
            var caches = await _unitOfWork.EmbeddingCaches.GetBySpecIdsAndModelAndUsageAsync(
                specs.Select(spec => spec.Id),
                embeddingModel,
                EmbeddingCacheUsages.Matching);
            var cacheLookup = caches
                .GroupBy(cache => cache.SpecId)
                .ToDictionary(group => group.Key, group => group.OrderBy(cache => cache.Id).First());

            foreach (var spec in specs)
            {
                var text = BuildMatchingText(spec.Project, spec.Specification);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var hasValidCache = cacheLookup.TryGetValue(spec.Id, out var cache) &&
                                    cache.TextHash == ComputeTextHash(text) &&
                                    cache.Vector.Length > 0;
                if (hasValidCache)
                {
                    continue;
                }

                candidates.Add(new AcceptanceSpec
                {
                    Id = spec.Id,
                    Project = spec.Project,
                    Specification = spec.Specification
                });

                if (candidates.Count >= maxItems)
                {
                    break;
                }
            }
        }

        foreach (var batch in candidates.Chunk(Math.Max(1, batchSize)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await GetOrCreateForSpecsAsync(
                batch,
                EmbeddingCacheUsages.Matching,
                embeddingServiceId: null,
                cancellationToken);
        }

        _logger.LogInformation("向量缓存预热处理完成：候选 {Count} 条", candidates.Count);
    }

    private async Task HydrateTargetsAsync(
        IReadOnlyCollection<CacheTarget> targets,
        string usage,
        int? embeddingServiceId,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0)
        {
            return;
        }

        var embeddingModel = await ResolveEmbeddingModelNameAsync(embeddingServiceId, cancellationToken);
        IReadOnlyList<EmbeddingCache> caches = [];
        var targetLookup = targets
            .GroupBy(target => target.SpecId)
            .ToDictionary(group => group.Key, group => group.First());

        if (!string.IsNullOrWhiteSpace(embeddingModel))
        {
            caches = await _unitOfWork.EmbeddingCaches.GetBySpecIdsAndModelAndUsageAsync(
                targetLookup.Keys,
                embeddingModel,
                usage);

            var cacheLookup = caches.ToDictionary(cache => cache.SpecId);
            foreach (var target in targets)
            {
                if (!cacheLookup.TryGetValue(target.SpecId, out var cache))
                {
                    continue;
                }

                if (cache.TextHash != ComputeTextHash(target.Text))
                {
                    continue;
                }

                var cachedEmbedding = DeserializeVector(cache.Vector);
                if (cachedEmbedding.Length > 0)
                {
                    target.SetEmbedding(cachedEmbedding);
                }
            }
        }

        var missingTargets = targets
            .Where(target => target.GetEmbeddingLength() == 0)
            .ToList();
        if (missingTargets.Count == 0)
        {
            _logger.LogDebug("规格 Embedding 全部命中缓存：usage={Usage}", usage);
            return;
        }

        var newEmbeddings = await GenerateEmbeddingsInBatchesAsync(
            missingTargets.Select(target => target.Text),
            embeddingServiceId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(embeddingModel))
        {
            ApplyGeneratedEmbeddings(missingTargets, newEmbeddings);
            return;
        }

        var existingCacheLookup = caches.ToDictionary(cache => cache.SpecId);
        var hasMutation = false;
        for (var index = 0; index < missingTargets.Count && index < newEmbeddings.Count; index++)
        {
            var embedding = newEmbeddings[index];
            if (embedding.Length == 0)
            {
                continue;
            }

            var target = missingTargets[index];
            target.SetEmbedding(embedding);
            var textHash = ComputeTextHash(target.Text);

            if (existingCacheLookup.TryGetValue(target.SpecId, out var existingCache))
            {
                existingCache.TextHash = textHash;
                existingCache.Vector = SerializeVector(embedding);
                existingCache.CreatedAt = DateTime.UtcNow;
                _unitOfWork.EmbeddingCaches.Update(existingCache);
            }
            else
            {
                await _unitOfWork.EmbeddingCaches.AddAsync(new EmbeddingCache
                {
                    SpecId = target.SpecId,
                    ModelName = embeddingModel,
                    Usage = usage,
                    TextHash = textHash,
                    Vector = SerializeVector(embedding),
                    CreatedAt = DateTime.UtcNow
                });
            }

            hasMutation = true;
        }

        if (hasMutation)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 懒生成的 Embedding 缓存立即独立落库，避免后续匹配/导入流程重复生成同一批向量。
            await _unitOfWork.SaveChangesAsync();
        }

        _logger.LogInformation(
            "规格 Embedding 缓存处理完成：usage={Usage}, hit={Hit}, generated={Generated}",
            usage,
            targets.Count - missingTargets.Count,
            missingTargets.Count);
    }

    private async Task<List<float[]>> GenerateEmbeddingsInBatchesAsync(
        IEnumerable<string> texts,
        int? embeddingServiceId,
        CancellationToken cancellationToken)
    {
        var vectors = new List<float[]>();
        foreach (var batch in texts.Chunk(EmbeddingGenerationBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchVectors = await _embeddingService.GenerateEmbeddingsAsync(
                batch,
                embeddingServiceId,
                cancellationToken);
            vectors.AddRange(batchVectors);
        }

        return vectors;
    }

    private static void ApplyGeneratedEmbeddings(
        IReadOnlyList<CacheTarget> targets,
        IReadOnlyList<float[]> embeddings)
    {
        for (var index = 0; index < targets.Count && index < embeddings.Count; index++)
        {
            targets[index].SetEmbedding(embeddings[index]);
        }
    }

    private static string BuildText(AcceptanceSpec spec, string usage)
    {
        return usage switch
        {
            EmbeddingCacheUsages.SemanticSearch => BuildSemanticSearchText(spec),
            EmbeddingCacheUsages.ImportDuplicateDetection => BuildImportDuplicateText(spec.Project, spec.Specification),
            EmbeddingCacheUsages.MatchingSpecificationOnly => spec.Specification?.Trim() ?? string.Empty,
            _ => BuildMatchingText(spec.Project, spec.Specification)
        };
    }

    private static string BuildMatchingText(string? project, string? specification)
    {
        return $"{project?.Trim()} {specification?.Trim()}".Trim();
    }

    private static string BuildImportDuplicateText(string? project, string? specification)
    {
        return string.Join(
            "\n",
            new[] { project?.Trim(), specification?.Trim() }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string BuildSemanticSearchText(AcceptanceSpec spec)
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

    private static string ComputeTextHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static byte[] SerializeVector(float[] vector)
    {
        if (vector.Length == 0)
        {
            return [];
        }

        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] DeserializeVector(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0 || bytes.Length % sizeof(float) != 0)
        {
            return [];
        }

        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }

    private sealed class CacheTarget
    {
        private float[] _embedding = [];

        public CacheTarget(int specId, string text, Action<float[]> setEmbedding)
        {
            SpecId = specId;
            Text = text;
            SetEmbedding = embedding =>
            {
                _embedding = embedding;
                setEmbedding(embedding);
            };
        }

        public int SpecId { get; }

        public string Text { get; }

        public Action<float[]> SetEmbedding { get; }

        public int GetEmbeddingLength() => _embedding.Length;
    }
}

public sealed record SpecEmbeddingResult(int SpecId, string Text, float[] Embedding);
