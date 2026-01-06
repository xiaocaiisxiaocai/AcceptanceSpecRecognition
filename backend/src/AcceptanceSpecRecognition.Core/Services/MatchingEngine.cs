using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AcceptanceSpecRecognition.Core.Services;

/// <summary>
/// 匹配引擎实现 - 简化版，只返回最佳匹配
/// </summary>
public class MatchingEngine : IMatchingEngine
{
    private readonly ITextPreprocessor _preprocessor;
    private readonly IEmbeddingService _embeddingService;
    private readonly IKeywordHighlighter _keywordHighlighter;
    private readonly ILLMService _llmService;
    private readonly IConfigManager _configManager;
    private readonly IJsonStorageService _storage;
    private readonly ILogger<MatchingEngine> _logger;

    private List<HistoryRecord>? _historyRecords;
    private readonly string _historyPath = "./data/history_records.json";
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;

    public MatchingEngine(
        ITextPreprocessor preprocessor,
        IEmbeddingService embeddingService,
        IKeywordHighlighter keywordHighlighter,
        ILLMService llmService,
        IConfigManager configManager,
        IJsonStorageService storage,
        ICacheService cacheService,
        ILogger<MatchingEngine> logger,
        ILoggerFactory loggerFactory)
    {
        _preprocessor = preprocessor;
        _embeddingService = embeddingService;
        _keywordHighlighter = keywordHighlighter;
        _llmService = llmService;
        _configManager = configManager;
        _storage = storage;
        _logger = logger;
    }

    /// <summary>
    /// 确保历史记录已加载（延迟初始化）
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (_isInitialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (!_isInitialized)
            {
                _historyRecords = await LoadHistoryRecords();
                _isInitialized = true;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<List<HistoryRecord>> LoadHistoryRecords()
    {
        var store = await _storage.ReadAsync<HistoryRecordStore>(_historyPath);
        return store?.Records ?? new List<HistoryRecord>();
    }

    /// <summary>
    /// 单条匹配 - 只返回最佳匹配结果
    /// </summary>
    public async Task<MatchResult> MatchAsync(MatchQuery query)
    {
        var stopwatch = Stopwatch.StartNew();
        await EnsureInitializedAsync();
        var config = _configManager.GetAll();
        var enableLLM = config.Matching.EnableLLM;

        try
        {
            // 1. 预处理查询文本
            var queryText = $"{query.Project} {query.TechnicalSpec}".Trim();
            var preprocessed = _preprocessor.Preprocess(queryText);

            // 2. 生成查询向量
            var queryVector = await _embeddingService.EmbedAsync(preprocessed.Normalized);

            // 3. 计算与所有历史记录的相似度，找出最佳匹配
            MatchCandidate? bestMatch = null;
            float bestScore = 0;

            foreach (var record in _historyRecords!)
            {
                if (record.Embedding == null || record.Embedding.Length == 0)
                {
                    continue;
                }

                var similarity = _embeddingService.CosineSimilarity(queryVector, record.Embedding);

                if (similarity > bestScore)
                {
                    bestScore = similarity;
                    bestMatch = new MatchCandidate
                    {
                        Record = record,
                        SimilarityScore = similarity,
                        HighlightedActualSpec = _keywordHighlighter.Highlight(record.ActualSpec).Html,
                        HighlightedRemark = _keywordHighlighter.Highlight(record.Remark).Html,
                        Explanation = new MatchExplanation
                        {
                            EmbeddingSimilarity = similarity,
                            MatchedSynonyms = new List<string>(),
                            PreprocessingSteps = preprocessed.Corrections.Select(c => new PreprocessingStep
                            {
                                Type = c.Type,
                                Before = c.Original,
                                After = c.Corrected
                            }).ToList()
                        }
                    };
                }
            }

            // 4. LLM 增强（可选）
            var usedLLM = false;
            var llmConfirmed = false;
            if (enableLLM && bestMatch != null)
            {
                var (adjustedScore, confirmed) = await EnhanceWithLLMAsync(queryText, bestMatch, bestScore, config);
                bestScore = adjustedScore;
                bestMatch.SimilarityScore = bestScore;
                usedLLM = true;
                llmConfirmed = confirmed;
            }

            // 5. 判断置信度 - LLM 确认的匹配直接设为 Success
            ConfidenceLevel confidence;
            if (llmConfirmed)
            {
                // LLM 确认了语义等价，置信度设为 Success
                confidence = ConfidenceLevel.Success;
            }
            else
            {
                // 普通阈值判断
                confidence = bestScore >= config.Matching.MatchSuccessThreshold
                    ? ConfidenceLevel.Success
                    : ConfidenceLevel.Low;
            }

            stopwatch.Stop();

            return new MatchResult
            {
                Query = query,
                BestMatch = bestMatch,
                SimilarityScore = bestScore,
                Confidence = confidence,
                MatchMode = usedLLM ? "LLM+Embedding" : "Embedding",
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "匹配过程发生错误");
            throw;
        }
    }

    /// <summary>
    /// 使用 LLM 增强匹配结果（统一分析：一次调用完成冲突检测 + 语义等价分析）
    /// </summary>
    private async Task<(float adjustedScore, bool llmConfirmed)> EnhanceWithLLMAsync(string queryText, MatchCandidate bestMatch, float embeddingScore, SystemConfig config)
    {
        try
        {
            var candidateText = $"{bestMatch.Record.Project} {bestMatch.Record.TechnicalSpec}";

            // 统一分析：一次 LLM 调用完成冲突检测 + 语义等价分析
            var result = await _llmService.AnalyzeUnifiedAsync(queryText, candidateText);

            // 有冲突时降低分数
            if (result.HasConflict)
            {
                var adjustedScore = embeddingScore * result.ScoreAdjustmentFactor;
                if (bestMatch.Explanation != null)
                {
                    bestMatch.Explanation.LLMReasoning = $"[冲突检测] {result.ConflictDescription}";
                    bestMatch.Explanation.LLMAdjustedScore = adjustedScore;
                }
                _logger.LogInformation("检测到冲突: {Description}, 分数从 {Original} 降至 {Adjusted}",
                    result.ConflictDescription, embeddingScore, adjustedScore);
                return (adjustedScore, false);
            }

            // 语义等价时提升分数
            if (result.IsEquivalent && result.Confidence >= 0.7f)
            {
                var adjustedScore = Math.Min(embeddingScore * result.ScoreAdjustmentFactor, 1.0f);

                // 构建等价关系说明
                var mappingDesc = string.Join(", ", result.EquivalenceMappings.Select(m =>
                    $"{m.QueryTerm}={m.CandidateTerm}({m.Type})"));

                if (bestMatch.Explanation != null)
                {
                    bestMatch.Explanation.LLMReasoning = $"[语义等价] {result.Reasoning}";
                    if (!string.IsNullOrEmpty(mappingDesc))
                    {
                        bestMatch.Explanation.LLMReasoning += $" 等价映射: {mappingDesc}";
                    }
                    bestMatch.Explanation.LLMAdjustedScore = adjustedScore;
                    bestMatch.Explanation.MatchedSynonyms = result.EquivalenceMappings
                        .Select(m => $"{m.QueryTerm}={m.CandidateTerm}")
                        .ToList();
                }

                _logger.LogInformation("检测到语义等价: {Reasoning}, 分数从 {Original} 提升至 {Adjusted} (系数: {Factor})",
                    result.Reasoning, embeddingScore, adjustedScore, result.ScoreAdjustmentFactor);

                return (adjustedScore, true);  // LLM 确认了匹配
            }

            return (embeddingScore, false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM 增强失败，使用原始 Embedding 分数");
            return (embeddingScore, false);
        }
    }

    /// <summary>
    /// 批量匹配 - 使用批量 Embedding API
    /// </summary>
    public async Task<List<MatchResult>> MatchBatchAsync(List<MatchQuery> queries)
    {
        if (queries.Count == 0)
        {
            return new List<MatchResult>();
        }

        await EnsureInitializedAsync();

        // 批量生成查询向量（一次 API 调用）
        var queryTexts = queries.Select(q =>
        {
            var text = $"{q.Project} {q.TechnicalSpec}".Trim();
            return _preprocessor.Preprocess(text).Normalized;
        }).ToList();

        var queryVectors = await _embeddingService.EmbedBatchAsync(queryTexts);

        // 并行匹配（使用预计算的向量）
        var tasks = queries.Select((query, index) =>
            MatchWithPrecomputedVectorAsync(query, queryVectors[index]));

        return (await Task.WhenAll(tasks)).ToList();
    }

    /// <summary>
    /// 使用预计算向量进行匹配
    /// </summary>
    private async Task<MatchResult> MatchWithPrecomputedVectorAsync(MatchQuery query, float[] queryVector)
    {
        var stopwatch = Stopwatch.StartNew();
        var config = _configManager.GetAll();
        var enableLLM = config.Matching.EnableLLM;
        var queryText = $"{query.Project} {query.TechnicalSpec}".Trim();
        var preprocessed = _preprocessor.Preprocess(queryText);

        // 找出最佳匹配
        MatchCandidate? bestMatch = null;
        float bestScore = 0;

        foreach (var record in _historyRecords!)
        {
            if (record.Embedding == null || record.Embedding.Length == 0)
            {
                continue;
            }

            var similarity = _embeddingService.CosineSimilarity(queryVector, record.Embedding);

            if (similarity > bestScore)
            {
                bestScore = similarity;
                bestMatch = new MatchCandidate
                {
                    Record = record,
                    SimilarityScore = similarity,
                    HighlightedActualSpec = _keywordHighlighter.Highlight(record.ActualSpec).Html,
                    HighlightedRemark = _keywordHighlighter.Highlight(record.Remark).Html,
                    Explanation = new MatchExplanation
                    {
                        EmbeddingSimilarity = similarity,
                        MatchedSynonyms = new List<string>(),
                        PreprocessingSteps = preprocessed.Corrections.Select(c => new PreprocessingStep
                        {
                            Type = c.Type,
                            Before = c.Original,
                            After = c.Corrected
                        }).ToList()
                    }
                };
            }
        }

        // LLM 增强（可选）
        var usedLLM = false;
        var llmConfirmed = false;
        if (enableLLM && bestMatch != null)
        {
            var (adjustedScore, confirmed) = await EnhanceWithLLMAsync(queryText, bestMatch, bestScore, config);
            bestScore = adjustedScore;
            bestMatch.SimilarityScore = bestScore;
            usedLLM = true;
            llmConfirmed = confirmed;
        }

        // 判断置信度 - LLM 确认的匹配直接设为 Success
        ConfidenceLevel confidence;
        if (llmConfirmed)
        {
            confidence = ConfidenceLevel.Success;
        }
        else
        {
            confidence = bestScore >= config.Matching.MatchSuccessThreshold
                ? ConfidenceLevel.Success
                : ConfidenceLevel.Low;
        }

        stopwatch.Stop();

        return new MatchResult
        {
            Query = query,
            BestMatch = bestMatch,
            SimilarityScore = bestScore,
            Confidence = confidence,
            MatchMode = usedLLM ? "LLM+Embedding" : "Embedding",
            DurationMs = stopwatch.ElapsedMilliseconds
        };
    }

    public async Task UpdateIndexAsync(HistoryRecord record)
    {
        await EnsureInitializedAsync();
        var recordText = $"{record.Project} {record.TechnicalSpec}".Trim();
        var preprocessed = _preprocessor.Preprocess(recordText);
        record.Embedding = await _embeddingService.EmbedAsync(preprocessed.Normalized);
        record.UpdatedAt = DateTime.UtcNow;

        var existingIndex = _historyRecords!.FindIndex(r => r.Id == record.Id);
        if (existingIndex >= 0)
        {
            _historyRecords[existingIndex] = record;
        }

        await SaveHistoryRecords();
    }

    public async Task<List<HistoryRecord>> GetHistoryRecordsAsync()
    {
        await EnsureInitializedAsync();
        return _historyRecords!.ToList();
    }

    public async Task<HistoryRecord> AddHistoryRecordAsync(HistoryRecord record)
    {
        await EnsureInitializedAsync();
        record.Id = $"rec_{Guid.NewGuid():N}";
        record.CreatedAt = DateTime.UtcNow;
        record.UpdatedAt = DateTime.UtcNow;

        // 生成向量（使用预处理后的文本，确保与查询时一致）
        var recordText = $"{record.Project} {record.TechnicalSpec}".Trim();
        var preprocessed = _preprocessor.Preprocess(recordText);
        record.Embedding = await _embeddingService.EmbedAsync(preprocessed.Normalized);

        _historyRecords!.Add(record);
        await SaveHistoryRecords();

        return record;
    }

    public async Task UpdateHistoryRecordAsync(HistoryRecord record)
    {
        await EnsureInitializedAsync();
        var existingIndex = _historyRecords!.FindIndex(r => r.Id == record.Id);
        if (existingIndex >= 0)
        {
            record.UpdatedAt = DateTime.UtcNow;

            // 重新生成向量（使用预处理后的文本，确保与查询时一致）
            var recordText = $"{record.Project} {record.TechnicalSpec}".Trim();
            var preprocessed = _preprocessor.Preprocess(recordText);
            record.Embedding = await _embeddingService.EmbedAsync(preprocessed.Normalized);

            _historyRecords[existingIndex] = record;
            await SaveHistoryRecords();
        }
    }

    private async Task SaveHistoryRecords()
    {
        var store = new HistoryRecordStore { Records = _historyRecords! };
        await _storage.WriteAsync(_historyPath, store);
    }

    /// <summary>
    /// 初始化所有历史记录的向量（用于首次部署或数据迁移）
    /// </summary>
    public async Task<int> InitializeEmbeddingsAsync()
    {
        await EnsureInitializedAsync();
        var needsEmbedding = _historyRecords!
            .Where(r => r.Embedding == null || r.Embedding.Length == 0)
            .ToList();

        if (needsEmbedding.Count == 0)
        {
            return 0;
        }

        // 使用批量 API（预处理后的文本，确保与查询时一致）
        var texts = needsEmbedding.Select(r =>
        {
            var recordText = $"{r.Project} {r.TechnicalSpec}".Trim();
            return _preprocessor.Preprocess(recordText).Normalized;
        }).ToList();
        var embeddings = await _embeddingService.EmbedBatchAsync(texts);

        for (int i = 0; i < needsEmbedding.Count; i++)
        {
            needsEmbedding[i].Embedding = embeddings[i];
        }

        await SaveHistoryRecords();
        return needsEmbedding.Count;
    }

    /// <summary>
    /// 强制重新生成所有历史记录的向量
    /// </summary>
    public async Task<int> RegenerateAllEmbeddingsAsync()
    {
        await EnsureInitializedAsync();

        // 使用批量 API（预处理后的文本，确保与查询时一致）
        var texts = _historyRecords!.Select(r =>
        {
            var recordText = $"{r.Project} {r.TechnicalSpec}".Trim();
            return _preprocessor.Preprocess(recordText).Normalized;
        }).ToList();
        var embeddings = await _embeddingService.EmbedBatchAsync(texts);

        for (int i = 0; i < _historyRecords.Count; i++)
        {
            _historyRecords[i].Embedding = embeddings[i];
        }

        await SaveHistoryRecords();
        return _historyRecords.Count;
    }

    /// <summary>
    /// 获取没有向量的记录数量
    /// </summary>
    public async Task<int> GetRecordsWithoutEmbeddingCountAsync()
    {
        await EnsureInitializedAsync();
        return _historyRecords!.Count(r => r.Embedding == null || r.Embedding.Length == 0);
    }

    /// <summary>
    /// 删除历史记录
    /// </summary>
    public async Task<bool> DeleteHistoryRecordAsync(string id)
    {
        await EnsureInitializedAsync();
        var record = _historyRecords!.FirstOrDefault(r => r.Id == id);
        if (record == null)
        {
            return false;
        }

        _historyRecords.Remove(record);
        await SaveHistoryRecords();
        return true;
    }

    /// <summary>
    /// 批量删除历史记录
    /// </summary>
    public async Task<int> DeleteHistoryRecordsBatchAsync(List<string> ids)
    {
        await EnsureInitializedAsync();
        var idsSet = new HashSet<string>(ids);
        var toRemove = _historyRecords!.Where(r => idsSet.Contains(r.Id)).ToList();

        foreach (var record in toRemove)
        {
            _historyRecords.Remove(record);
        }

        if (toRemove.Count > 0)
        {
            await SaveHistoryRecords();
        }

        return toRemove.Count;
    }
}
