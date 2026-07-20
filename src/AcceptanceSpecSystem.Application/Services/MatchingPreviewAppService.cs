using System.Diagnostics;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;

namespace AcceptanceSpecSystem.Application.Services;

public interface IMatchingPreviewAppService
{
    Task<MatchingOperationResult<BatchPreviewResponse>> BatchPreviewAsync(
        MatchingUserContext user,
        BatchPreviewRequest request,
        CancellationToken cancellationToken = default);

    MatchingOperationResult<BatchPreviewProgressResponse> GetBatchPreviewProgress(string requestId);
}

/// <summary>
/// 匹配预览应用服务。
/// </summary>
public sealed class MatchingPreviewAppService : IMatchingPreviewAppService
{
    private readonly IMatchingService _matchingService;
    private readonly IDocumentFileAccessService _documentFileAccessService;
    private readonly IBatchReplyDocumentTablePort _documentTableAccessService;
    private readonly ITextPreprocessingPipeline _textPipeline;
    private readonly IAuthDataScopeService _authDataScopeService;
    private readonly BatchPreviewProgressTracker _batchPreviewProgressTracker;
    private readonly MatchingApprovalTokenService _approvalTokenService;
    private readonly MatchingConfigResolver _matchingConfigResolver;
    private readonly MatchingCandidateProvider _matchingCandidateProvider;
    private readonly ILogger<MatchingPreviewAppService> _logger;

    public MatchingPreviewAppService(
        IMatchingService matchingService,
        IDocumentFileAccessService documentFileAccessService,
        IBatchReplyDocumentTablePort documentTableAccessService,
        ITextPreprocessingPipeline textPipeline,
        IAuthDataScopeService authDataScopeService,
        BatchPreviewProgressTracker batchPreviewProgressTracker,
        MatchingApprovalTokenService approvalTokenService,
        MatchingConfigResolver matchingConfigResolver,
        MatchingCandidateProvider matchingCandidateProvider,
        ILogger<MatchingPreviewAppService> logger)
    {
        _matchingService = matchingService;
        _documentFileAccessService = documentFileAccessService;
        _documentTableAccessService = documentTableAccessService;
        _textPipeline = textPipeline;
        _authDataScopeService = authDataScopeService;
        _batchPreviewProgressTracker = batchPreviewProgressTracker;
        _approvalTokenService = approvalTokenService;
        _matchingConfigResolver = matchingConfigResolver;
        _matchingCandidateProvider = matchingCandidateProvider;
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
        MatchingUserContext user,
        BatchPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var previewRequestId = request.PreviewRequestId?.Trim();
        _batchPreviewProgressTracker.Start(previewRequestId, request.Tables?.Count ?? 0);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

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

            cancellationToken.ThrowIfCancellationRequested();

            var config = await _matchingConfigResolver.ResolveAsync(request.Config, cancellationToken);
            _matchingCandidateProvider.EnsureEmbeddingServiceAvailable(config);
            var candidates = await _matchingCandidateProvider.GetCandidatesAsync(
                request.CustomerId,
                request.ProcessId,
                request.MachineModelId,
                scope,
                config.EmbeddingServiceId,
                hydrateEmbeddings: false,
                cancellationToken: cancellationToken);
            var highConfidenceThreshold = NormalizeHighConfidenceThreshold(config.HighConfidenceThreshold);

            _batchPreviewProgressTracker.Update(
                previewRequestId,
                stage: "candidatePreparation",
                stageText: "候选数据已就绪",
                detailText: $"当前范围内共 {candidates.Count} 条候选验收规格",
                progressPercent: 14);

            var tpSession = await _textPipeline.CreateSessionAsync(cancellationToken);
            var processedCandidates = BuildProcessedCandidates(candidates, tpSession);

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
                cancellationToken.ThrowIfCancellationRequested();
                var regionValidationError = MatchingRegionValidator.GetValidationError(
                    tableConfig.Regions,
                    tableConfig.TableIndex);
                if (regionValidationError != null)
                {
                    throw Failure(400, regionValidationError);
                }

                _batchPreviewProgressTracker.Update(
                    previewRequestId,
                    stage: "extractingTables",
                    stageText: "正在提取表格源数据",
                    detailText: $"正在读取第 {extractedTableCount + 1}/{request.Tables.Count} 个表格",
                    progressPercent: request.Tables.Count == 0
                        ? 18
                        : 18 + (12d * extractedTableCount / request.Tables.Count));

                var effectiveRegions = tableConfig.Regions.Count > 0
                    ? tableConfig.Regions.OrderBy(region => region.RegionIndex).ToList()
                    :
                    [
                        new BatchTableRegionConfig
                        {
                            RegionIndex = 0,
                            ProjectColumnIndex = tableConfig.ProjectColumnIndex,
                            SpecificationColumnIndex = tableConfig.SpecificationColumnIndex,
                            AcceptanceColumnIndex = tableConfig.AcceptanceColumnIndex,
                            RemarkColumnIndex = tableConfig.RemarkColumnIndex,
                            HeaderRowStart = tableConfig.HeaderRowStart,
                            HeaderRowCount = tableConfig.HeaderRowCount,
                            DataStartRow = tableConfig.DataStartRow,
                            DataEndRow = tableConfig.DataEndRow
                        }
                    ];
                var extracted = new List<MatchSourceItem>();
                foreach (var region in effectiveRegions)
                {
                    var regionItems = await _documentTableAccessService.ExtractMatchSourceItemsAsync(
                        wordFile,
                        tableConfig.TableIndex,
                        region.ProjectColumnIndex,
                        region.SpecificationColumnIndex,
                        region.HeaderRowStart,
                        region.HeaderRowCount,
                        region.DataStartRow,
                        region.DataEndRow,
                        tableConfig.FilterEmptySourceRows ?? config.FilterEmptySourceRows,
                        cancellationToken);
                    foreach (var item in regionItems)
                    {
                        item.RegionId = string.IsNullOrWhiteSpace(region.RegionId)
                            ? $"table-{tableConfig.TableIndex}-region-{region.RegionIndex}"
                            : region.RegionId;
                        item.RegionIndex = region.RegionIndex;
                        item.AcceptanceColumnIndex = region.AcceptanceColumnIndex;
                        item.RemarkColumnIndex = region.RemarkColumnIndex;
                    }
                    extracted.AddRange(regionItems);
                }

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
                    if (config.ExactMatchOnly || !RequiresSemanticMatching(allSources, processedCandidates, config))
                    {
                        batchResult = BuildExactMatchBatchResult(allSources, processedCandidates, config);
                    }
                    else
                    {
                        await _matchingCandidateProvider.HydrateCandidateEmbeddingsAsync(
                            candidates,
                            config.EmbeddingServiceId,
                            config.MatchingMode,
                            cancellationToken);
                        processedCandidates = BuildProcessedCandidates(candidates, tpSession);
                        batchResult = await _matchingService.BatchMatchAsync(
                            allSources,
                            processedCandidates,
                            config,
                            CreateBatchMatchProgressReporter(previewRequestId),
                            cancellationToken);
                    }
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
                cancellationToken.ThrowIfCancellationRequested();

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
                            noMatchReason = config.ExactMatchOnly
                                ? GetExactMatchNoMatchReason(config)
                                : "最佳得分低于阈值";
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
                        RegionId = item.RegionId,
                        RegionIndex = item.RegionIndex,
                        AcceptanceColumnIndex = item.AcceptanceColumnIndex,
                        RemarkColumnIndex = item.RemarkColumnIndex,
                        RowIndex = item.RowIndex,
                        SourceProject = item.Project,
                        SourceSpecification = item.Specification,
                        BestMatch = bestMatch != null
                            ? MatchingResultDtoMapper.ToMatchResultDto(bestMatch, previewApprovalToken)
                            : null,
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

    private static BatchMatchResult BuildExactMatchBatchResult(
        IReadOnlyList<MatchSource> sources,
        IReadOnlyList<MatchCandidate> candidates,
        MatchingConfig config)
    {
        var lookup = BuildExactMatchLookup(candidates, config);

        return new BatchMatchResult
        {
            Results = sources
                .Select(source =>
                {
                    var key = BuildExactMatchLookupKey(source.Project, source.Specification, config);
                    return lookup.TryGetValue(key, out var candidatesForKey)
                        ? CreateExactMatchResult(source, candidatesForKey, config)
                        : CreateNoMatchResult(source, config);
                })
                .ToList()
        };
    }

    private static Dictionary<string, List<MatchCandidate>> BuildExactMatchLookup(
        IReadOnlyList<MatchCandidate> candidates,
        MatchingConfig config)
    {
        return candidates
            .GroupBy(candidate => BuildExactMatchLookupKey(candidate.Project, candidate.Specification, config))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(candidate => HasText(candidate.Acceptance))
                    .ThenByDescending(candidate => HasText(candidate.Remark))
                    .ThenByDescending(candidate => candidate.SpecId)
                    .ToList());
    }

    private static string BuildExactMatchLookupKey(
        string? project,
        string? specification,
        MatchingConfig config)
    {
        return config.MatchingMode == MatchingMode.SpecificationOnly
            ? MatchingCandidateProvider.BuildCandidateDedupKey(null, specification)
            : MatchingCandidateProvider.BuildCandidateDedupKey(project, specification);
    }

    private static MatchResult CreateNoMatchResult(MatchSource source, MatchingConfig config)
    {
        return new MatchResult
        {
            SourceText = source.CombinedText,
            MinScoreThreshold = config.MinScoreThreshold,
            HighConfidenceThreshold = config.HighConfidenceThreshold,
            Decision = MatchDecision.ManualReview
        };
    }

    private static MatchResult CreateExactMatchResult(
        MatchSource source,
        IReadOnlyList<MatchCandidate> candidates,
        MatchingConfig config)
    {
        var candidate = candidates[0];
        return config.MatchingMode == MatchingMode.SpecificationOnly
            ? CreateSpecificationOnlyMatchResult(source, candidate, candidates, config)
            : CreateProjectSpecificationExactMatchResult(source, candidate, config);
    }

    private static bool RequiresSemanticMatching(
        IReadOnlyList<MatchSource> sources,
        IReadOnlyList<MatchCandidate> candidates,
        MatchingConfig config)
    {
        if (sources.Count == 0)
        {
            return false;
        }

        var lookup = BuildExactMatchLookup(candidates, config);

        return sources.Any(source =>
            !lookup.ContainsKey(BuildExactMatchLookupKey(source.Project, source.Specification, config)));
    }

    private static List<MatchCandidate> BuildProcessedCandidates(
        IEnumerable<MatchCandidate> candidates,
        TextProcessingSession tpSession)
    {
        return candidates.Select(candidate => new MatchCandidate
        {
            SpecId = candidate.SpecId,
            Project = tpSession.Process(candidate.Project),
            Specification = tpSession.Process(candidate.Specification),
            Acceptance = candidate.Acceptance,
            Remark = candidate.Remark,
            Embedding = candidate.Embedding
        }).ToList();
    }

    private static MatchResult CreateProjectSpecificationExactMatchResult(
        MatchSource source,
        MatchCandidate candidate,
        MatchingConfig config)
    {
        var scoreDetails = new Dictionary<string, double>
        {
            ["Final"] = 1,
            ["Embedding"] = 1,
            ["Exact"] = 1
        };

        var equivalence = new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Reason = "项目与规格文本完全一致，已直接视为等价",
            Confidence = 1
        };

        return new MatchResult
        {
            SourceText = source.CombinedText,
            MatchedText = candidate.CombinedText,
            MatchedSpecId = candidate.SpecId,
            MatchedProject = candidate.Project,
            MatchedSpecification = candidate.Specification,
            MatchedAcceptance = candidate.Acceptance,
            MatchedRemark = candidate.Remark,
            Score = 1,
            EmbeddingScore = 1,
            ScoreDetails = scoreDetails,
            Decision = MatchDecision.AutoApply,
            SelectionMode = MatchSelectionMode.ExactShortcut,
            SelectionSummary = "项目与规格精确一致，直接命中",
            MatchBasis = MatchBasis.ProjectSpecification,
            RecalledCandidateCount = 1,
            IsAmbiguous = false,
            MinScoreThreshold = config.MinScoreThreshold,
            HighConfidenceThreshold = config.HighConfidenceThreshold,
            LlmEquivalence = equivalence,
            TopCandidates =
            [
                new MatchCandidateSnapshot
                {
                    Rank = 1,
                    SpecId = candidate.SpecId,
                    Project = candidate.Project,
                    Specification = candidate.Specification,
                    Acceptance = candidate.Acceptance,
                    Remark = candidate.Remark,
                    Score = 1,
                    EmbeddingScore = 1,
                    ScoreDetails = scoreDetails,
                    SelectionMode = MatchSelectionMode.ExactShortcut,
                    SelectionSummary = "项目与规格精确一致，直接命中",
                    MatchBasis = MatchBasis.ProjectSpecification,
                    LlmEquivalence = equivalence
                }
            ]
        };
    }

    private static MatchResult CreateSpecificationOnlyMatchResult(
        MatchSource source,
        MatchCandidate candidate,
        IReadOnlyList<MatchCandidate> candidatesForSpecification,
        MatchingConfig config)
    {
        var hasMultipleCandidates = candidatesForSpecification.Count > 1;
        var scoreDetails = new Dictionary<string, double>
        {
            ["Final"] = 1,
            ["Embedding"] = 1,
            ["Exact"] = 1
        };

        var equivalence = new LlmEquivalenceAdjudicationResult
        {
            Verdict = LlmEquivalenceVerdict.Equivalent,
            ReasonType = LlmEquivalenceReasonType.EquivalentExpression,
            Reason = "规格文本完全一致，已按用户选择的仅规格模式命中",
            Confidence = 1
        };

        return new MatchResult
        {
            SourceText = source.CombinedText,
            MatchedText = candidate.CombinedText,
            MatchedSpecId = candidate.SpecId,
            MatchedProject = candidate.Project,
            MatchedSpecification = candidate.Specification,
            MatchedAcceptance = candidate.Acceptance,
            MatchedRemark = candidate.Remark,
            Score = 1,
            EmbeddingScore = 1,
            ScoreDetails = scoreDetails,
            Decision = hasMultipleCandidates ? MatchDecision.ManualReview : MatchDecision.AutoApply,
            SelectionMode = MatchSelectionMode.ExactShortcut,
            SelectionSummary = hasMultipleCandidates
                ? "规格精确一致，但同规格存在多条候选，需人工确认"
                : "规格精确一致，按仅规格模式直接命中",
            MatchBasis = MatchBasis.Specification,
            RecalledCandidateCount = candidatesForSpecification.Count,
            IsAmbiguous = hasMultipleCandidates,
            MinScoreThreshold = config.MinScoreThreshold,
            HighConfidenceThreshold = config.HighConfidenceThreshold,
            LlmEquivalence = equivalence,
            TopCandidates = candidatesForSpecification
                .Take(3)
                .Select((item, index) => new MatchCandidateSnapshot
                {
                    Rank = index + 1,
                    SpecId = item.SpecId,
                    Project = item.Project,
                    Specification = item.Specification,
                    Acceptance = item.Acceptance,
                    Remark = item.Remark,
                    Score = 1,
                    EmbeddingScore = 1,
                    ScoreDetails = scoreDetails,
                    SelectionMode = MatchSelectionMode.ExactShortcut,
                    SelectionSummary = "规格精确一致",
                    MatchBasis = MatchBasis.Specification,
                    LlmEquivalence = index == 0 ? equivalence : null
                })
                .ToList()
        };
    }

    private static string GetExactMatchNoMatchReason(MatchingConfig config)
    {
        return config.MatchingMode == MatchingMode.SpecificationOnly
            ? "仅规格匹配模式下未找到规格完全一致的验收规格"
            : "仅精确匹配模式下未找到项目+规格完全一致的验收规格";
    }

    private static bool HasText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
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

        var minScoreThreshold = Math.Clamp(result.MinScoreThreshold, 0, 1);

        if (result.Decision == MatchDecision.Reject)
        {
            return "low";
        }

        if (result.Decision != MatchDecision.AutoApply)
        {
            return result.Score >= minScoreThreshold ? "medium" : "low";
        }

        if (result.Score >= NormalizeHighConfidenceThreshold(highConfidenceThreshold))
        {
            return "high";
        }

        // LLM 判等价的自动通过：高置信归 high，中置信（含低于阈值）归 medium 供审核员优先复查
        if (result.LlmEquivalence?.Verdict == LlmEquivalenceVerdict.Equivalent)
        {
            return result.LlmEquivalence.Confidence >= MatchingThresholds.HighConfidenceLlmEquivalenceMinConfidence
                ? "high"
                : "medium";
        }

        if (result.Score >= minScoreThreshold)
        {
            return "medium";
        }

        return "low";
    }

    private async Task<DataScopeResult?> ResolveSpecScopeAsync(MatchingUserContext user)
    {
        return await _authDataScopeService.GetScopeAsync(user.UserId, user.CompanyId, "spec");
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
