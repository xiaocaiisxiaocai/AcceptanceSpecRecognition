using System.Diagnostics;
using System.Security.Claims;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using CoreAiServicePurpose = AcceptanceSpecSystem.Core.AI.Models.AiServicePurpose;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 匹配预览应用服务。
/// </summary>
public sealed class MatchingPreviewAppService
{
    private const int MaxScopedCandidateCount = 2000;
    private const int EmbeddingGenerationBatchSize = 200;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IMatchingService _matchingService;
    private readonly DocumentFileAccessService _documentFileAccessService;
    private readonly DocumentTableAccessService _documentTableAccessService;
    private readonly ITextPreprocessingPipeline _textPipeline;
    private readonly IAuthDataScopeService _authDataScopeService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IAiServiceSelector _aiServiceSelector;
    private readonly BatchPreviewProgressTracker _batchPreviewProgressTracker;
    private readonly MatchingApprovalTokenService _approvalTokenService;
    private readonly ILogger<MatchingPreviewAppService> _logger;

    private sealed class CandidateSpecRow
    {
        public int Id { get; init; }
        public string Project { get; init; } = string.Empty;
        public string Specification { get; init; } = string.Empty;
        public string? Acceptance { get; init; }
        public string? Remark { get; init; }
        public DateTime ImportedAt { get; init; }
    }

    public MatchingPreviewAppService(
        IUnitOfWork unitOfWork,
        IMatchingService matchingService,
        DocumentFileAccessService documentFileAccessService,
        DocumentTableAccessService documentTableAccessService,
        ITextPreprocessingPipeline textPipeline,
        IAuthDataScopeService authDataScopeService,
        IEmbeddingService embeddingService,
        IAiServiceSelector aiServiceSelector,
        BatchPreviewProgressTracker batchPreviewProgressTracker,
        MatchingApprovalTokenService approvalTokenService,
        ILogger<MatchingPreviewAppService> logger)
    {
        _unitOfWork = unitOfWork;
        _matchingService = matchingService;
        _documentFileAccessService = documentFileAccessService;
        _documentTableAccessService = documentTableAccessService;
        _textPipeline = textPipeline;
        _authDataScopeService = authDataScopeService;
        _embeddingService = embeddingService;
        _aiServiceSelector = aiServiceSelector;
        _batchPreviewProgressTracker = batchPreviewProgressTracker;
        _approvalTokenService = approvalTokenService;
        _logger = logger;
    }

    private static MatchingOperationResult<T> Result<T>(T data, string message = "操作成功")
    {
        return new MatchingOperationResult<T>(data, message);
    }

    private static MatchingApiException Failure(int code, string message)
    {
        return new MatchingApiException(code, message);
    }

    private static MatchingApiException NotFoundFailure(string message)
    {
        return new MatchingApiException(404, message, isNotFound: true);
    }

    public async Task<MatchingOperationResult<BatchPreviewResponse>> BatchPreviewAsync(
        ClaimsPrincipal user,
        BatchPreviewRequest request)
    {
        var sw = Stopwatch.StartNew();
        var previewRequestId = request.PreviewRequestId?.Trim();
        _batchPreviewProgressTracker.Start(previewRequestId, request.Tables?.Count ?? 0);

        try
        {
            if (request.Tables == null || request.Tables.Count == 0)
            {
                throw Failure(400, "请至少选择一个表格");
            }

            const int MaxBatchTableCount = 500;
            if (request.Tables.Count > MaxBatchTableCount)
            {
                throw Failure(400, $"批量操作不能超过 {MaxBatchTableCount} 个表格");
            }

            if (request.FileId <= 0)
            {
                throw Failure(400, "文件ID不能为空");
            }

            _batchPreviewProgressTracker.Update(
                previewRequestId,
                stage: "scope",
                stageText: "正在加载匹配范围与候选数据",
                detailText: "正在解析当前用户数据范围与匹配配置",
                progressPercent: 6);

            var scope = await ResolveSpecScopeAsync(user);
            if (scope == null)
            {
                throw Failure(401, "会话缺少用户上下文");
            }

            var config = await ConvertToMatchingConfigAsync(request.Config);
            var candidates = await GetCandidatesAsync(
                request.CustomerId,
                request.ProcessId,
                request.MachineModelId,
                scope,
                config.EmbeddingServiceId);
            var highConfidenceThreshold = NormalizeHighConfidenceThreshold(config.HighConfidenceThreshold);

            _batchPreviewProgressTracker.Update(
                previewRequestId,
                stage: "candidatePreparation",
                stageText: "候选数据已就绪",
                detailText: $"当前范围内共 {candidates.Count} 条候选验收规格",
                progressPercent: 14);

            var tpSession = await _textPipeline.CreateSessionAsync();
            var processedCandidates = candidates.Select(candidate => new MatchCandidate
            {
                SpecId = candidate.SpecId,
                Project = tpSession.Process(candidate.Project),
                Specification = tpSession.Process(candidate.Specification),
                Acceptance = candidate.Acceptance,
                Remark = candidate.Remark,
                Embedding = candidate.Embedding
            }).ToList();

            var response = new BatchPreviewResponse();
            var allTableData = new List<(BatchTableConfig Config, List<MatchSourceItem> Items, List<MatchSource> Sources)>();
            var wordFile = await _documentFileAccessService.GetAccessibleWordFileAsync(request.FileId, scope);
            if (wordFile == null)
            {
                throw NotFoundFailure("源文件不存在");
            }

            var extractedTableCount = 0;
            var extractedRowCount = 0;
            foreach (var tableConfig in request.Tables)
            {
                _batchPreviewProgressTracker.Update(
                    previewRequestId,
                    stage: "extractingTables",
                    stageText: "正在提取表格源数据",
                    detailText: $"正在读取第 {extractedTableCount + 1}/{request.Tables.Count} 个表格",
                    progressPercent: request.Tables.Count == 0
                        ? 18
                        : 18 + (12d * extractedTableCount / request.Tables.Count));

                var extracted = await _documentTableAccessService.ExtractMatchSourceItemsAsync(
                    wordFile,
                    tableConfig.TableIndex,
                    tableConfig.ProjectColumnIndex,
                    tableConfig.SpecificationColumnIndex,
                    tableConfig.HeaderRowStart,
                    tableConfig.HeaderRowCount,
                    tableConfig.DataStartRow,
                    tableConfig.FilterEmptySourceRows ?? config.FilterEmptySourceRows);

                var sources = extracted.Select(item => new MatchSource
                {
                    Project = tpSession.Process(item.Project),
                    Specification = tpSession.Process(item.Specification)
                }).ToList();

                allTableData.Add((tableConfig, extracted, sources));
                extractedTableCount++;
                extractedRowCount += extracted.Count;

                _batchPreviewProgressTracker.Update(
                    previewRequestId,
                    stage: "extractingTables",
                    stageText: "表格源数据提取完成",
                    detailText: $"已提取 {extractedTableCount}/{request.Tables.Count} 个表格，共 {extractedRowCount} 行",
                    progressPercent: request.Tables.Count == 0
                        ? 30
                        : 18 + (12d * extractedTableCount / request.Tables.Count));
            }

            var allSources = allTableData.SelectMany(item => item.Sources).ToList();
            if (processedCandidates.Count == 0)
            {
                throw Failure(400, "范围内无候选数据");
            }

            BatchMatchResult batchResult;
            if (allSources.Count > 0)
            {
                _batchPreviewProgressTracker.Update(
                    previewRequestId,
                    stage: "embedding",
                    stageText: "正在生成向量并启动匹配",
                    detailText: $"待匹配 {allSources.Count} 行，候选 {processedCandidates.Count} 条",
                    completedItems: 0,
                    totalItems: allSources.Count,
                    progressPercent: 32);

                try
                {
                    batchResult = await _matchingService.BatchMatchAsync(
                        allSources,
                        processedCandidates,
                        config,
                        CreateBatchMatchProgressReporter(previewRequestId));
                }
                catch (AiServiceUnavailableException ex)
                {
                    throw Failure(400, $"Embedding 服务不可用: {ex.Reason}");
                }
            }
            else
            {
                batchResult = new BatchMatchResult();
            }

            _batchPreviewProgressTracker.Update(
                previewRequestId,
                stage: "assembling",
                stageText: "正在整理预览结果",
                detailText: $"正在汇总 {request.Tables.Count} 个表格的匹配结果",
                completedItems: allSources.Count,
                totalItems: allSources.Count,
                progressPercent: 98);

            var resultOffset = 0;
            foreach (var (tableConfig, extracted, _) in allTableData)
            {
                var tableResult = new BatchTablePreviewResult
                {
                    TableIndex = tableConfig.TableIndex
                };
                var highCount = 0;
                var mediumCount = 0;
                var lowCount = 0;

                for (var idx = 0; idx < extracted.Count; idx++)
                {
                    var item = extracted[idx];
                    MatchResult? bestMatch = null;
                    string? noMatchReason = null;

                    if ((resultOffset + idx) < batchResult.Results.Count)
                    {
                        var matchResult = batchResult.Results[resultOffset + idx];
                        if (matchResult.MatchedSpecId.HasValue)
                        {
                            bestMatch = matchResult;
                        }
                        else
                        {
                            noMatchReason = "最佳得分低于阈值";
                        }
                    }

                    var previewApprovalToken = bestMatch != null &&
                                               bestMatch.MatchedSpecId.HasValue &&
                                               bestMatch.Decision == MatchDecision.AutoApply
                        ? _approvalTokenService.IssueToken(
                            scope.UserId,
                            tableConfig.TableIndex,
                            item.RowIndex,
                            bestMatch.MatchedSpecId.Value,
                            item.Project,
                            item.Specification,
                            bestMatch.MatchedProject,
                            bestMatch.MatchedSpecification,
                            bestMatch.MatchedAcceptance,
                            bestMatch.MatchedRemark,
                            request.CustomerId,
                            request.ProcessId,
                            request.MachineModelId,
                            config)
                        : null;

                    var previewItem = new MatchPreviewItem
                    {
                        RowIndex = item.RowIndex,
                        SourceProject = item.Project,
                        SourceSpecification = item.Specification,
                        BestMatch = bestMatch != null ? ConvertToMatchResultDto(bestMatch, previewApprovalToken) : null,
                        NoMatchReason = noMatchReason,
                        ConfidenceLevel = GetConfidenceLevel(bestMatch, highConfidenceThreshold)
                    };

                    tableResult.Items.Add(previewItem);
                    if (previewItem.BestMatch == null)
                    {
                        continue;
                    }

                    switch (previewItem.ConfidenceLevel)
                    {
                        case "high":
                            highCount++;
                            break;
                        case "medium":
                            mediumCount++;
                            break;
                        default:
                            lowCount++;
                            break;
                    }
                }

                resultOffset += extracted.Count;
                tableResult.TotalMatched = tableResult.Items.Count(item => item.HasMatch);
                tableResult.HighConfidenceCount = highCount;
                tableResult.MediumConfidenceCount = mediumCount;
                tableResult.LowConfidenceCount = lowCount;
                tableResult.AmbiguousCount = tableResult.Items.Count(item => item.BestMatch?.IsAmbiguous == true);

                response.Tables.Add(tableResult);
            }

            _batchPreviewProgressTracker.Complete(
                previewRequestId,
                completedItems: allSources.Count,
                totalItems: allSources.Count,
                detailText: $"已完成 {request.Tables.Count} 个表格、{allSources.Count} 行的匹配预览");

            sw.Stop();
            _logger.LogInformation(
                "批量匹配预览完成: {TableCount}个表格, 总匹配{Total}, 高{High}/中{Medium}/低{Low}, 歧义{Ambiguous}, 耗时{Elapsed}ms",
                request.Tables.Count,
                response.TotalMatched,
                response.HighConfidenceCount,
                response.MediumConfidenceCount,
                response.LowConfidenceCount,
                response.AmbiguousCount,
                sw.ElapsedMilliseconds);

            return Result(response);
        }
        catch (MatchingApiException ex)
        {
            _batchPreviewProgressTracker.Fail(previewRequestId, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _batchPreviewProgressTracker.Fail(previewRequestId, ex.Message);
            throw;
        }
    }

    public MatchingOperationResult<BatchPreviewProgressResponse> GetBatchPreviewProgress(string requestId)
    {
        var progress = _batchPreviewProgressTracker.GetSnapshot(requestId);
        if (progress == null)
        {
            throw NotFoundFailure("未找到对应的预览进度，请重新发起匹配预览");
        }

        return Result(progress);
    }

    private async Task<List<MatchCandidate>> GetCandidatesAsync(
        int? customerId,
        int? processId,
        int? machineModelId,
        DataScopeResult scope,
        int? embeddingServiceId)
    {
        var baseQuery = BuildCandidateSpecQuery(customerId, processId, machineModelId);
        var scopedQuery = ApplySpecScopeToQuery(baseQuery, scope);
        var rawCount = await baseQuery.CountAsync();
        var scopedCount = await scopedQuery.CountAsync();
        EnsureCandidateScopeWithinLimit(scopedCount, customerId, processId, machineModelId);

        var scopedSpecs = await scopedQuery
            .Select(spec => new CandidateSpecRow
            {
                Id = spec.Id,
                Project = spec.Project,
                Specification = spec.Specification,
                Acceptance = spec.Acceptance,
                Remark = spec.Remark,
                ImportedAt = spec.ImportedAt
            })
            .ToListAsync();

        var dedupedSpecs = scopedSpecs
            .GroupBy(spec => BuildCandidateDedupKey(spec.Project, spec.Specification))
            .Select(group => group
                .OrderByDescending(spec => HasText(spec.Acceptance))
                .ThenByDescending(spec => HasText(spec.Remark))
                .ThenByDescending(spec => spec.ImportedAt)
                .ThenByDescending(spec => spec.Id)
                .First())
            .ToList();

        _logger.LogInformation(
            "匹配候选去重: 原始{RawCount}条, 范围内{ScopedCount}条 -> 去重后{DedupedCount}条 (customerId={CustomerId}, processId={ProcessId}, machineModelId={MachineModelId})",
            rawCount,
            scopedCount,
            dedupedSpecs.Count,
            customerId,
            processId,
            machineModelId);

        var candidates = dedupedSpecs.Select(spec => new MatchCandidate
        {
            SpecId = spec.Id,
            Project = spec.Project,
            Specification = spec.Specification,
            Acceptance = spec.Acceptance,
            Remark = spec.Remark
        }).ToList();

        await HydrateCandidateEmbeddingsAsync(candidates, embeddingServiceId);
        return candidates;
    }

    private async Task HydrateCandidateEmbeddingsAsync(List<MatchCandidate> candidates, int? embeddingServiceId)
    {
        string? embeddingModel = null;
        IReadOnlyList<EmbeddingCache> caches = [];

        if (embeddingServiceId.HasValue)
        {
            var configs = await _aiServiceSelector.GetCandidatesAsync(CoreAiServicePurpose.Embedding, embeddingServiceId);
            embeddingModel = configs.FirstOrDefault()?.EmbeddingModel?.Trim();
        }

        if (!string.IsNullOrWhiteSpace(embeddingModel))
        {
            caches = await _unitOfWork.EmbeddingCaches.GetBySpecIdsAndModelAsync(
                candidates.Select(candidate => candidate.SpecId),
                embeddingModel);

            var cacheLookup = caches.ToDictionary(cache => cache.SpecId);
            foreach (var candidate in candidates)
            {
                if (cacheLookup.TryGetValue(candidate.SpecId, out var cache))
                {
                    candidate.Embedding = DeserializeVector(cache.Vector);
                }
            }
        }

        var missingCandidates = candidates.Where(candidate => candidate.Embedding == null || candidate.Embedding.Length == 0).ToList();
        if (missingCandidates.Count == 0)
        {
            _logger.LogDebug("匹配候选 Embedding 全部命中缓存，跳过远程调用");
            return;
        }

        List<float[]> newEmbeddings;
        try
        {
            newEmbeddings = await GenerateEmbeddingsInBatchesAsync(
                missingCandidates.Select(candidate => candidate.CombinedText),
                embeddingServiceId);
        }
        catch (AiServiceUnavailableException ex)
        {
            _logger.LogWarning(ex, "匹配候选生成 Embedding 失败");
            throw Failure(400, $"Embedding 服务不可用: {ex.Reason}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "匹配候选生成 Embedding 失败");
            throw Failure(400, "Embedding 服务不可用: 匹配候选 Embedding 生成失败");
        }

        if (!string.IsNullOrWhiteSpace(embeddingModel))
        {
            var existingCacheLookup = caches.ToDictionary(cache => cache.SpecId);
            var hasMutation = false;

            for (var index = 0; index < missingCandidates.Count; index++)
            {
                if (index >= newEmbeddings.Count || newEmbeddings[index].Length == 0)
                {
                    continue;
                }

                var candidate = missingCandidates[index];
                candidate.Embedding = newEmbeddings[index];

                if (existingCacheLookup.TryGetValue(candidate.SpecId, out var existingCache))
                {
                    existingCache.Vector = SerializeVector(newEmbeddings[index]);
                    existingCache.CreatedAt = DateTime.UtcNow;
                    _unitOfWork.EmbeddingCaches.Update(existingCache);
                }
                else
                {
                    await _unitOfWork.EmbeddingCaches.AddAsync(new EmbeddingCache
                    {
                        SpecId = candidate.SpecId,
                        ModelName = embeddingModel,
                        Vector = SerializeVector(newEmbeddings[index]),
                        CreatedAt = DateTime.UtcNow
                    });
                }

                hasMutation = true;
            }

            if (hasMutation)
            {
                await _unitOfWork.SaveChangesAsync();
            }
        }
        else
        {
            for (var index = 0; index < missingCandidates.Count && index < newEmbeddings.Count; index++)
            {
                missingCandidates[index].Embedding = newEmbeddings[index];
            }
        }

        _logger.LogInformation(
            "匹配候选 Embedding: 命中缓存{Cached}个, 新生成{Generated}个",
            candidates.Count - missingCandidates.Count,
            missingCandidates.Count);
    }

    private void EnsureCandidateScopeWithinLimit(
        int scopedCount,
        int? customerId,
        int? processId,
        int? machineModelId)
    {
        if (scopedCount <= MaxScopedCandidateCount)
        {
            return;
        }

        _logger.LogWarning(
            "匹配范围候选过多，拒绝继续处理: scopedCount={ScopedCount}, limit={Limit}, customerId={CustomerId}, processId={ProcessId}, machineModelId={MachineModelId}",
            scopedCount,
            MaxScopedCandidateCount,
            customerId,
            processId,
            machineModelId);
        throw Failure(400, $"匹配范围内候选数据过多（{scopedCount}条），请按客户/制程/机型缩小范围后重试");
    }

    private async Task<List<float[]>> GenerateEmbeddingsInBatchesAsync(
        IEnumerable<string> texts,
        int? embeddingServiceId)
    {
        var vectors = new List<float[]>();
        foreach (var batch in texts.Chunk(EmbeddingGenerationBatchSize))
        {
            var batchVectors = await _embeddingService.GenerateEmbeddingsAsync(batch, embeddingServiceId);
            vectors.AddRange(batchVectors);
        }

        return vectors;
    }

    private static byte[] SerializeVector(float[] vector)
    {
        if (vector.Length == 0)
        {
            return Array.Empty<byte>();
        }

        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] DeserializeVector(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0 || bytes.Length % sizeof(float) != 0)
        {
            return Array.Empty<float>();
        }

        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }

    private IQueryable<AcceptanceSpec> BuildCandidateSpecQuery(
        int? customerId,
        int? processId,
        int? machineModelId)
    {
        var query = _unitOfWork.AcceptanceSpecs.Query();

        if (customerId.HasValue)
        {
            query = query.Where(spec => spec.CustomerId == customerId.Value);
        }

        if (processId.HasValue)
        {
            query = query.Where(spec => spec.ProcessId == processId.Value);
        }

        if (machineModelId.HasValue)
        {
            query = query.Where(spec => spec.MachineModelId == machineModelId.Value);
        }

        return query;
    }

    private static IQueryable<AcceptanceSpec> ApplySpecScopeToQuery(
        IQueryable<AcceptanceSpec> query,
        DataScopeResult scope)
    {
        if (scope.IsAll)
        {
            return query;
        }

        var scopedOrgUnitIds = scope.OrgUnitIds.Distinct().ToArray();
        if (scope.IncludeSelf && scopedOrgUnitIds.Length > 0)
        {
            return query.Where(spec =>
                (spec.CreatedByUserId.HasValue && spec.CreatedByUserId.Value == scope.UserId) ||
                (spec.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(spec.OwnerOrgUnitId.Value)));
        }

        if (scope.IncludeSelf)
        {
            return query.Where(spec =>
                spec.CreatedByUserId.HasValue && spec.CreatedByUserId.Value == scope.UserId);
        }

        if (scopedOrgUnitIds.Length > 0)
        {
            return query.Where(spec =>
                spec.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(spec.OwnerOrgUnitId.Value));
        }

        return query.Where(_ => false);
    }

    private static string BuildCandidateDedupKey(string? project, string? specification)
    {
        return string.Join(
            "\u001f",
            NormalizeForDedup(project),
            NormalizeForDedup(specification));
    }

    private static string NormalizeForDedup(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(" ", value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool HasText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    private async Task<MatchingConfig> ConvertToMatchingConfigAsync(MatchConfigDto? dto)
    {
        var fallbackConfig = new MatchingConfig();
        var defaultRecallTopK = await ResolveDefaultRecallTopKAsync(dto?.EmbeddingServiceId);

        return new MatchingConfig
        {
            EmbeddingServiceId = dto?.EmbeddingServiceId,
            LlmServiceId = dto?.LlmServiceId,
            MinScoreThreshold = dto?.MinScoreThreshold ?? fallbackConfig.MinScoreThreshold,
            HighConfidenceThreshold = NormalizeHighConfidenceThreshold(dto?.HighConfidenceThreshold ?? fallbackConfig.HighConfidenceThreshold),
            RecallTopK = Math.Clamp(dto?.RecallTopK ?? defaultRecallTopK, 1, MatchingThresholds.MaxRecallTopK),
            AmbiguityMargin = Math.Clamp(dto?.AmbiguityMargin ?? fallbackConfig.AmbiguityMargin, 0, 1),
            LlmParallelism = Math.Clamp(dto?.LlmParallelism ?? fallbackConfig.LlmParallelism, 1, 10),
            LlmRowTimeoutSeconds = Math.Clamp(dto?.LlmRowTimeoutSeconds ?? fallbackConfig.LlmRowTimeoutSeconds, 5, 300),
            LlmRetryCount = Math.Clamp(dto?.LlmRetryCount ?? fallbackConfig.LlmRetryCount, 0, 3),
            LlmCircuitBreakFailures = Math.Clamp(dto?.LlmCircuitBreakFailures ?? fallbackConfig.LlmCircuitBreakFailures, 3, 200),
            FilterEmptySourceRows = dto?.FilterEmptySourceRows ?? fallbackConfig.FilterEmptySourceRows
        };
    }

    private async Task<int> ResolveDefaultRecallTopKAsync(int? embeddingServiceId)
    {
        var fallbackConfig = new MatchingConfig();
        var query = _unitOfWork.AiServiceConfigs
            .Query()
            .AsNoTracking()
            .Where(item => (item.Purpose & AiServicePurpose.Embedding) == AiServicePurpose.Embedding);

        AiServiceConfig? embeddingService;
        if (embeddingServiceId.HasValue)
        {
            embeddingService = await query.FirstOrDefaultAsync(item => item.Id == embeddingServiceId.Value);
        }
        else
        {
            embeddingService = await query
                .OrderBy(item => item.Priority)
                .ThenByDescending(item => item.UpdatedAt ?? item.CreatedAt)
                .FirstOrDefaultAsync();
        }

        return embeddingService?.DefaultRecallTopK ?? fallbackConfig.RecallTopK;
    }

    private static double NormalizeHighConfidenceThreshold(double? highConfidenceThreshold)
    {
        return MatchingThresholds.NormalizeHighConfidenceThreshold(highConfidenceThreshold);
    }

    private static string GetConfidenceLevel(MatchResult? result, double highConfidenceThreshold)
    {
        if (result == null || !result.MatchedSpecId.HasValue || result.Score <= 0)
        {
            return "none";
        }

        if (result.Decision == MatchDecision.Reject)
        {
            return "low";
        }

        if (result.Decision != MatchDecision.AutoApply)
        {
            return result.Score >= MatchingThresholds.MediumConfidenceScore ? "medium" : "low";
        }

        if (result.LlmEquivalence?.Verdict == LlmEquivalenceVerdict.Equivalent ||
            result.Score >= NormalizeHighConfidenceThreshold(highConfidenceThreshold))
        {
            return "high";
        }

        if (result.Score >= MatchingThresholds.MediumConfidenceScore)
        {
            return "medium";
        }

        return "low";
    }

    private static MatchResultDto ConvertToMatchResultDto(MatchResult result, string? reviewApprovalToken = null)
    {
        return new MatchResultDto
        {
            SpecId = result.MatchedSpecId ?? 0,
            Project = result.MatchedProject ?? string.Empty,
            Specification = result.MatchedSpecification ?? string.Empty,
            Acceptance = result.MatchedAcceptance,
            Remark = result.MatchedRemark,
            Score = result.Score,
            EmbeddingScore = result.EmbeddingScore,
            ScoreDetails = result.ScoreDetails,
            Decision = ToDecisionKey(result.Decision),
            EvidenceSummary = [.. result.Evidence.Summary],
            ConflictSummary = [.. result.Evidence.Conflicts],
            Issues = result.Issues.Select(ConvertToIssueDto).ToList(),
            Entities = result.Evidence.Entities.Select(ConvertToEntityDto).ToList(),
            LlmEquivalence = ConvertToLlmEquivalenceDto(result.LlmEquivalence),
            TopCandidates = result.TopCandidates
                .Select(candidate => new MatchCandidateDto
                {
                    Rank = candidate.Rank,
                    SpecId = candidate.SpecId,
                    Project = candidate.Project,
                    Specification = candidate.Specification,
                    Acceptance = candidate.Acceptance,
                    Remark = candidate.Remark,
                    Score = candidate.Score,
                    EmbeddingScore = candidate.EmbeddingScore,
                    ScoreDetails = candidate.ScoreDetails,
                    Decision = result.MatchedSpecId == candidate.SpecId
                        ? ToDecisionKey(result.Decision)
                        : "manualReview",
                    EvidenceSummary = [.. candidate.Evidence.Summary],
                    ConflictSummary = [.. candidate.Evidence.Conflicts],
                    Issues = candidate.Issues.Select(ConvertToIssueDto).ToList(),
                    Entities = candidate.Evidence.Entities.Select(ConvertToEntityDto).ToList(),
                    RerankSummary = candidate.RerankSummary,
                    SelectionMode = ToSelectionModeKey(candidate.SelectionMode),
                    SelectionSummary = candidate.SelectionSummary,
                    LlmEquivalence = ConvertToLlmEquivalenceDto(candidate.LlmEquivalence)
                })
                .ToList(),
            RecalledCandidateCount = result.RecalledCandidateCount,
            IsAmbiguous = result.IsAmbiguous,
            ScoreGap = result.ScoreGap,
            RerankSummary = result.RerankSummary,
            SelectionMode = ToSelectionModeKey(result.SelectionMode),
            SelectionSummary = result.SelectionSummary,
            ReviewApprovalToken = reviewApprovalToken
        };
    }

    private static LlmEquivalenceDto? ConvertToLlmEquivalenceDto(LlmEquivalenceAdjudicationResult? result)
    {
        if (result == null)
        {
            return null;
        }

        return new LlmEquivalenceDto
        {
            Verdict = ToEquivalenceVerdictKey(result.Verdict),
            ReasonType = ToEquivalenceReasonTypeKey(result.ReasonType),
            Reason = result.Reason,
            Confidence = result.Confidence
        };
    }

    private static string ToEquivalenceVerdictKey(LlmEquivalenceVerdict verdict)
    {
        return verdict switch
        {
            LlmEquivalenceVerdict.Equivalent => "equivalent",
            LlmEquivalenceVerdict.Different => "different",
            _ => "uncertain"
        };
    }

    private static string ToEquivalenceReasonTypeKey(LlmEquivalenceReasonType reasonType)
    {
        return reasonType switch
        {
            LlmEquivalenceReasonType.FormatOnly => "format_only",
            LlmEquivalenceReasonType.PunctuationOnly => "punctuation_only",
            LlmEquivalenceReasonType.EquivalentExpression => "equivalent_expression",
            LlmEquivalenceReasonType.SymbolEquivalent => "symbol_equivalent",
            LlmEquivalenceReasonType.SemanticDifference => "semantic_difference",
            LlmEquivalenceReasonType.SymbolConflict => "symbol_conflict",
            _ => "uncertain"
        };
    }

    private static string ToSelectionModeKey(MatchSelectionMode selectionMode)
    {
        return selectionMode switch
        {
            MatchSelectionMode.ExactShortcut => "exactShortcut",
            MatchSelectionMode.AiRerank => "aiRerank",
            _ => "embeddingTop1"
        };
    }

    private static MatchEntityEvidenceDto ConvertToEntityDto(EntityEvidence entity)
    {
        return new MatchEntityEvidenceDto
        {
            EntityType = entity.EntityType,
            SourceValue = entity.SourceValue,
            CandidateValue = entity.CandidateValue,
            NormalizedSourceValue = entity.NormalizedSourceValue,
            NormalizedCandidateValue = entity.NormalizedCandidateValue,
            Relation = ToEvidenceRelationKey(entity.Relation)
        };
    }

    private static MatchIssueDto ConvertToIssueDto(MatchIssue issue)
    {
        return new MatchIssueDto
        {
            Code = issue.Code,
            Severity = issue.Severity,
            FieldName = issue.FieldName,
            SourceValue = issue.SourceValue,
            CandidateValue = issue.CandidateValue,
            Message = issue.Message,
            SuggestedAction = issue.SuggestedAction
        };
    }

    private static string ToDecisionKey(MatchDecision decision)
    {
        return decision switch
        {
            MatchDecision.AutoApply => "autoApply",
            MatchDecision.ManualReview => "manualReview",
            MatchDecision.Reject => "reject",
            _ => "manualReview"
        };
    }

    private static string ToEvidenceRelationKey(EvidenceRelation relation)
    {
        return relation switch
        {
            EvidenceRelation.Exact => "exact",
            EvidenceRelation.Compatible => "compatible",
            EvidenceRelation.Overlap => "overlap",
            EvidenceRelation.Conflict => "conflict",
            EvidenceRelation.AliasSame => "aliasSame",
            EvidenceRelation.ParentChild => "parentChild",
            EvidenceRelation.PossiblyRelated => "possiblyRelated",
            _ => "unknown"
        };
    }

    private async Task<DataScopeResult?> ResolveSpecScopeAsync(ClaimsPrincipal user)
    {
        return await SpecDataScopeHelper.ResolveScopeAsync(user, _authDataScopeService);
    }

    private IProgress<BatchMatchProgress>? CreateBatchMatchProgressReporter(string? previewRequestId)
    {
        if (string.IsNullOrWhiteSpace(previewRequestId))
        {
            return null;
        }

        return new Progress<BatchMatchProgress>(progress =>
        {
            if (progress == null)
            {
                return;
            }

            var percent = progress.TotalItems <= 0
                ? 40d
                : 40d + (55d * progress.CompletedItems / progress.TotalItems);

            _batchPreviewProgressTracker.Update(
                previewRequestId,
                stage: progress.Stage,
                stageText: string.IsNullOrWhiteSpace(progress.StageText)
                    ? "正在逐行执行匹配与 AI 裁决"
                    : progress.StageText,
                detailText: string.IsNullOrWhiteSpace(progress.DetailText)
                    ? $"已完成 {progress.CompletedItems}/{progress.TotalItems} 行"
                    : progress.DetailText,
                completedItems: progress.CompletedItems,
                totalItems: progress.TotalItems,
                progressPercent: percent);
        });
    }
}
