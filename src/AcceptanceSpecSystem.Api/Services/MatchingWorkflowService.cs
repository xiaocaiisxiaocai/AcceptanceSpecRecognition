using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.Services;
using CoreAiServicePurpose = AcceptanceSpecSystem.Core.AI.Models.AiServicePurpose;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 智能匹配共享协作组件。
/// </summary>
public sealed class MatchingWorkflowSupportService
{
    private const int MaxScopedCandidateCount = 3000;
    private const int EmbeddingGenerationBatchSize = 200;
    private static readonly TimeSpan ReviewApprovalTokenLifetime = TimeSpan.FromHours(2);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IMatchingService _matchingService;
    private readonly DocumentFileAccessService _documentFileAccessService;
    private readonly DocumentTableAccessService _documentTableAccessService;
    private readonly MatchingResultWriteBackService _matchingResultWriteBackService;
    private readonly ITextPreprocessingPipeline _textPipeline;
    private readonly IAuthDataScopeService _authDataScopeService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IAiServiceSelector _aiServiceSelector;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MatchingTaskSnapshotService _matchingTaskSnapshotService;
    private readonly ExecutionHistoryAppService _executionHistoryAppService;
    private readonly MatchingApprovalTokenService _approvalTokenService;
    private readonly IDataProtector _reviewApprovalProtector;
    private readonly ILogger<MatchingWorkflowSupportService> _logger;

    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class CandidateSpecRow
    {
        public int Id { get; init; }

        public string Project { get; init; } = string.Empty;

        public string Specification { get; init; } = string.Empty;

        public string? Acceptance { get; init; }

        public string? Remark { get; init; }

        public DateTime ImportedAt { get; init; }
    }

    private sealed class ExecutionMatchSnapshot
    {
        public Dictionary<int, MatchResult> MatchLookup { get; init; } = [];

        public Dictionary<int, MatchSourceItem> SourceRowLookup { get; init; } = [];
    }

    private sealed class ReviewApprovalTokenPayload
    {
        public int UserId { get; init; }

        public int? TableIndex { get; init; }

        public int RowIndex { get; init; }

        public int SpecId { get; init; }

        public string SourceProject { get; init; } = string.Empty;

        public string SourceSpecification { get; init; } = string.Empty;

        public string SpecFingerprint { get; init; } = string.Empty;

        public int? CustomerId { get; init; }

        public int? ProcessId { get; init; }

        public int? MachineModelId { get; init; }

        public MatchingConfig Config { get; init; } = new();

        public DateTimeOffset IssuedAtUtc { get; init; }

        public DateTimeOffset ExpiresAtUtc { get; init; }
    }

    private sealed class ReviewApprovalBundle
    {
        public int UserId { get; init; }

        public int? CustomerId { get; init; }

        public int? ProcessId { get; init; }

        public int? MachineModelId { get; init; }

        public MatchingConfig Config { get; init; } = new();

        public Dictionary<ReviewApprovalLookupKey, ReviewApprovalTokenPayload> Tokens { get; init; } = [];
    }

    private readonly record struct ReviewApprovalLookupKey(int? TableIndex, int RowIndex);
    private readonly record struct LlmStreamItemKey(int? TableIndex, int RowIndex);

    private sealed class LlmStepFailureException : Exception
    {
        public LlmStepFailureException(string eventMessage, string? decision = null, Exception? innerException = null)
            : base(eventMessage, innerException)
        {
            EventMessage = eventMessage;
            Decision = decision;
        }

        public string EventMessage { get; }

        public string? Decision { get; }
    }

    /// <summary>
    /// 创建匹配工作流协作组件实例。
    /// </summary>
    public MatchingWorkflowSupportService(
        IUnitOfWork unitOfWork,
        IMatchingService matchingService,
        DocumentFileAccessService documentFileAccessService,
        DocumentTableAccessService documentTableAccessService,
        MatchingResultWriteBackService matchingResultWriteBackService,
        ITextPreprocessingPipeline textPipeline,
        IAuthDataScopeService authDataScopeService,
        IEmbeddingService embeddingService,
        IAiServiceSelector aiServiceSelector,
        IServiceScopeFactory scopeFactory,
        MatchingTaskSnapshotService matchingTaskSnapshotService,
        ExecutionHistoryAppService executionHistoryAppService,
        MatchingApprovalTokenService approvalTokenService,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<MatchingWorkflowSupportService> logger)
    {
        _unitOfWork = unitOfWork;
        _matchingService = matchingService;
        _documentFileAccessService = documentFileAccessService;
        _documentTableAccessService = documentTableAccessService;
        _matchingResultWriteBackService = matchingResultWriteBackService;
        _textPipeline = textPipeline;
        _authDataScopeService = authDataScopeService;
        _embeddingService = embeddingService;
        _aiServiceSelector = aiServiceSelector;
        _scopeFactory = scopeFactory;
        _matchingTaskSnapshotService = matchingTaskSnapshotService;
        _executionHistoryAppService = executionHistoryAppService;
        _approvalTokenService = approvalTokenService;
        _reviewApprovalProtector = dataProtectionProvider.CreateProtector("MatchingWorkflowSupportService.ReviewApprovalToken.v1");
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

    private string IssueReviewApprovalToken(
        DataScopeResult scope,
        int? customerId,
        int? processId,
        int? machineModelId,
        MatchingConfig config,
        MatchLlmStreamItem item,
        MatchCandidate spec)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new ReviewApprovalTokenPayload
        {
            UserId = scope.UserId,
            TableIndex = item.TableIndex,
            RowIndex = item.RowIndex,
            SpecId = item.BestMatchSpecId ?? 0,
            SourceProject = NormalizeForDedup(item.SourceProject),
            SourceSpecification = NormalizeForDedup(item.SourceSpecification),
            SpecFingerprint = ComputeReviewApprovalSpecFingerprint(
                spec.Project,
                spec.Specification,
                spec.Acceptance,
                spec.Remark),
            CustomerId = customerId,
            ProcessId = processId,
            MachineModelId = machineModelId,
            Config = CloneMatchingConfig(config),
            IssuedAtUtc = now,
            ExpiresAtUtc = now.Add(ReviewApprovalTokenLifetime)
        };

        var json = JsonSerializer.Serialize(payload);
        return _reviewApprovalProtector.Protect(json);
    }

    private ReviewApprovalBundle? ResolveReviewApprovalBundle(
        IEnumerable<(int? TableIndex, FillMapping Mapping)> mappings,
        int executingUserId)
    {
        ReviewApprovalTokenPayload? baseline = null;
        var tokens = new Dictionary<ReviewApprovalLookupKey, ReviewApprovalTokenPayload>();
        var now = DateTimeOffset.UtcNow;

        foreach (var (tableIndex, mapping) in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.ReviewApprovalToken))
            {
                continue;
            }

            ReviewApprovalTokenPayload payload;
            try
            {
                var json = _reviewApprovalProtector.Unprotect(mapping.ReviewApprovalToken);
                payload = JsonSerializer.Deserialize<ReviewApprovalTokenPayload>(json)
                    ?? throw new InvalidOperationException("复核放行令牌为空");
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidOperationException)
            {
                throw Failure(400, "复核放行令牌无效，请重新预览并复核");
            }

            if (payload.ExpiresAtUtc <= now)
            {
                throw Failure(400, "复核放行令牌已过期，请重新预览并复核");
            }

            if (payload.UserId != executingUserId)
            {
                throw Failure(400, "复核放行令牌不属于当前用户，请重新预览并复核");
            }

            if (payload.TableIndex != tableIndex ||
                payload.RowIndex != mapping.RowIndex ||
                payload.SpecId != (mapping.SpecId ?? 0))
            {
                throw Failure(400, "复核放行令牌与当前行或规格不一致，请重新预览并复核");
            }

            var key = new ReviewApprovalLookupKey(payload.TableIndex, payload.RowIndex);
            if (!tokens.TryAdd(key, payload))
            {
                throw Failure(400, "同一行存在重复的复核放行令牌，请重新预览并复核");
            }

            if (baseline == null)
            {
                baseline = payload;
                continue;
            }

            if (!HasSameReviewApprovalContext(baseline, payload))
            {
                throw Failure(400, "复核放行令牌来自不同的预览上下文，请分批执行");
            }
        }

        if (baseline == null)
        {
            return null;
        }

        return new ReviewApprovalBundle
        {
            UserId = baseline.UserId,
            CustomerId = baseline.CustomerId,
            ProcessId = baseline.ProcessId,
            MachineModelId = baseline.MachineModelId,
            Config = CloneMatchingConfig(baseline.Config),
            Tokens = tokens
        };
    }

    private static bool HasSameReviewApprovalContext(
        ReviewApprovalTokenPayload left,
        ReviewApprovalTokenPayload right)
    {
        return left.UserId == right.UserId &&
               left.CustomerId == right.CustomerId &&
               left.ProcessId == right.ProcessId &&
               left.MachineModelId == right.MachineModelId &&
               HasSameMatchingConfig(left.Config, right.Config);
    }

    private static bool HasSameMatchingConfig(MatchingConfig left, MatchingConfig right)
    {
        return left.EmbeddingServiceId == right.EmbeddingServiceId &&
               left.LlmServiceId == right.LlmServiceId &&
               left.MinScoreThreshold == right.MinScoreThreshold &&
               left.RecallTopK == right.RecallTopK &&
               left.AmbiguityMargin == right.AmbiguityMargin &&
               left.HighConfidenceThreshold == right.HighConfidenceThreshold &&
               left.LlmParallelism == right.LlmParallelism &&
               left.LlmRowTimeoutSeconds == right.LlmRowTimeoutSeconds &&
               left.LlmRetryCount == right.LlmRetryCount &&
               left.LlmCircuitBreakFailures == right.LlmCircuitBreakFailures &&
               left.FilterEmptySourceRows == right.FilterEmptySourceRows;
    }

    private static MatchingConfig CloneMatchingConfig(MatchingConfig config)
    {
        return new MatchingConfig
        {
            EmbeddingServiceId = config.EmbeddingServiceId,
            LlmServiceId = config.LlmServiceId,
            MinScoreThreshold = config.MinScoreThreshold,
            RecallTopK = config.RecallTopK,
            AmbiguityMargin = config.AmbiguityMargin,
            HighConfidenceThreshold = config.HighConfidenceThreshold,
            LlmParallelism = config.LlmParallelism,
            LlmRowTimeoutSeconds = config.LlmRowTimeoutSeconds,
            LlmRetryCount = config.LlmRetryCount,
            LlmCircuitBreakFailures = config.LlmCircuitBreakFailures,
            FilterEmptySourceRows = config.FilterEmptySourceRows
        };
    }

    internal async Task RunLlmStreamAsync(ClaimsPrincipal user, HttpResponse response, MatchLlmStreamRequest request, CancellationToken cancellationToken)
    {
        if (request.Items == null || request.Items.Count == 0)
        {
            throw Failure(400, "Items不能为空");
        }

        EnsureDistinctLlmStreamItems(request.Items);

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
        var accessibleSpecLookup = candidates.ToDictionary(candidate => candidate.SpecId);
        var normalizedItems = await BuildAuthoritativeLlmStreamItemsAsync(request.Items, candidates, config);

        response.Headers.CacheControl = "no-cache";
        response.Headers.TryAdd("X-Accel-Buffering", "no");
        response.ContentType = "text/event-stream";

        // 并行处理：每行独立创建 DI 作用域（DbContext 非线程安全），SSE 写入用信号量串行化
        var sseWriteLock = new SemaphoreSlim(1, 1);
        var sw = Stopwatch.StartNew();

        var parallelism = config.LlmParallelism;
        var rowTimeoutSeconds = config.LlmRowTimeoutSeconds;
        var retryCount = config.LlmRetryCount;
        var circuitBreakFailures = config.LlmCircuitBreakFailures;
        var reviewTargetLookup = normalizedItems.ToDictionary(
            GetLlmStreamItemKey,
            RequiresReviewForStreamItem);
        var reviewCount = reviewTargetLookup.Count(item => item.Value);
        var reviewTerminalLookup = new ConcurrentDictionary<LlmStreamItemKey, byte>();
        var reviewSuccess = 0;
        var reviewFailed = 0;
        var reviewTimeout = 0;
        var reviewRetries = 0;
        var totalFailures = 0;
        var circuitOpened = 0;
        var requestAborted = false;

        _logger.LogInformation(
            "[LLM-Stream] 开始并行处理 {Count} 行 (review={ReviewCount}, maxParallelism={Parallelism}, rowTimeoutSec={RowTimeoutSec}, retryCount={RetryCount}, circuitBreakFailures={CircuitBreakFailures})",
            normalizedItems.Count, reviewCount, parallelism,
            rowTimeoutSeconds, retryCount, circuitBreakFailures);

        try
        {
            await Parallel.ForEachAsync(
                normalizedItems,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = parallelism,
                    CancellationToken = cancellationToken
                },
                async (item, ct) =>
                {
                    using var serviceScope = _scopeFactory.CreateScope();
                    var reviewService = serviceScope.ServiceProvider.GetRequiredService<ILlmReviewService>();
                    var location = FormatStreamItemLocation(item);
                    var itemKey = GetLlmStreamItemKey(item);
                    var requiresReview = reviewTargetLookup.GetValueOrDefault(itemKey);

                    if (Volatile.Read(ref circuitOpened) == 1)
                    {
                        await WriteCircuitOpenEventsAsync(response, item, requiresReview, reviewTerminalLookup, sseWriteLock, ct);
                        return;
                    }

                    if (requiresReview)
                    {
                        _logger.LogDebug("[LLM-Stream] {Location}: 开始复核 (specId={SpecId}, score={Score:P1})",
                            location, item.BestMatchSpecId, item.BestMatchScore ?? 0);

                        var reviewResult = await ExecuteLlmStepWithPolicyAsync(
                            response,
                            "review",
                            item,
                            rowTimeoutSeconds,
                            retryCount,
                            token => StreamLlmReviewAsync(
                                response,
                                item,
                                config,
                                scope,
                                request.CustomerId,
                                request.ProcessId,
                                request.MachineModelId,
                                token,
                                accessibleSpecLookup,
                                reviewService,
                                reviewTerminalLookup,
                                sseWriteLock),
                            reviewTerminalLookup,
                            sseWriteLock,
                            ct);

                        Interlocked.Add(ref reviewRetries, reviewResult.RetriesUsed);
                        switch (reviewResult.Outcome)
                        {
                            case LlmStepOutcome.Success:
                                Interlocked.Increment(ref reviewSuccess);
                                break;
                            case LlmStepOutcome.Timeout:
                                Interlocked.Increment(ref reviewTimeout);
                                var timeoutFailures = Interlocked.Increment(ref totalFailures);
                                var openedByTimeout = timeoutFailures >= circuitBreakFailures &&
                                                     Interlocked.Exchange(ref circuitOpened, 1) == 0;
                                if (openedByTimeout)
                                {
                                    return;
                                }
                                break;
                            default:
                                Interlocked.Increment(ref reviewFailed);
                                var failedCount = Interlocked.Increment(ref totalFailures);
                                var openedByFailure = failedCount >= circuitBreakFailures &&
                                                      Interlocked.Exchange(ref circuitOpened, 1) == 0;
                                if (openedByFailure)
                                {
                                    return;
                                }
                                break;
                        }
                    }

                    if (ct.IsCancellationRequested) return;
                    if (Volatile.Read(ref circuitOpened) == 1)
                    {
                        await WriteCircuitOpenEventsAsync(response, item, requiresReview, reviewTerminalLookup, sseWriteLock, ct);
                        return;
                    }
                });
        }
        catch (OperationCanceledException)
        {
            requestAborted = true;
            _logger.LogDebug("LLM 流式输出：客户端已断开连接");
        }
        finally
        {
            if (!requestAborted && !response.HttpContext.RequestAborted.IsCancellationRequested)
            {
                await WriteSseEventSafeAsync(response, "stream.complete", new
                {
                    totalItems = normalizedItems.Count,
                    reviewTargets = reviewCount,
                    reviewSuccess,
                    reviewFailed,
                    reviewTimeout,
                    reviewRetries,
                    totalFailures,
                    circuitOpened = circuitOpened == 1,
                    elapsedMs = sw.ElapsedMilliseconds
                }, CancellationToken.None);
            }

            sseWriteLock.Dispose();
        }

        _logger.LogInformation(
            "[LLM-Stream] 全部完成, 耗时 {Elapsed}ms, review(success={ReviewSuccess}, failed={ReviewFailed}, timeout={ReviewTimeout}, retries={ReviewRetries}), totalFailures={TotalFailures}, circuitOpened={CircuitOpened}",
            sw.ElapsedMilliseconds,
            reviewSuccess, reviewFailed, reviewTimeout, reviewRetries,
            totalFailures, circuitOpened == 1);
    }

    internal async Task<MatchingOperationResult<ExecuteFillResponse>> BatchExecuteFillCoreAsync(ClaimsPrincipal user, BatchExecuteFillRequest request)
    {
        if (request.Tables == null || request.Tables.Count == 0)
        {
            throw Failure(400, "请至少提供一个表格的填充映射");
        }

        EnsureDistinctBatchTableIndexes(request.Tables);
        foreach (var table in request.Tables)
        {
            EnsureDistinctFillMappings(
                table.Mappings,
                $"表格{table.TableIndex + 1}存在重复的行索引，请删除重复映射后重试");
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

        // 获取源文件
        var scope = await ResolveSpecScopeAsync(user);
        if (scope == null)
        {
            throw Failure(401, "会话缺少用户上下文");
        }

        var wordFile = await _documentFileAccessService.GetAccessibleWordFileAsync(request.FileId, scope);
        if (wordFile == null)
        {
            throw Failure(400, "源文件不存在");
        }
        var reviewApprovalBundle = _approvalTokenService.ResolveBundle(
            request.Tables.SelectMany(table =>
                table.Mappings.Select(mapping => (TableIndex: (int?)table.TableIndex, Mapping: mapping))),
            scope.UserId);
        var executionConfig = reviewApprovalBundle?.Config ?? await ResolveExecutionMatchingConfigAsync(request.Config);
        var effectiveCustomerId = reviewApprovalBundle?.CustomerId ?? request.CustomerId;
        var effectiveProcessId = reviewApprovalBundle?.ProcessId ?? request.ProcessId;
        var effectiveMachineModelId = reviewApprovalBundle?.MachineModelId ?? request.MachineModelId;

        // 收集所有 specId 一次查 DB
        var allSpecIds = request.Tables
            .SelectMany(t => t.Mappings)
            .Select(m => m.SpecId)
            .Where(id => id.HasValue && id.Value > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var specDict = await GetScopedSpecDictionaryAsync(allSpecIds, scope);

        var currentMatchLookups = new Dictionary<int, ExecutionMatchSnapshot>();
        foreach (var table in request.Tables)
        {
            EnsureExecutionPreviewContext(table.ProjectColumnIndex, table.SpecificationColumnIndex, table.TableIndex);
            currentMatchLookups[table.TableIndex] = await BuildCurrentMatchLookupAsync(
                wordFile,
                table.TableIndex,
                table.ProjectColumnIndex,
                table.SpecificationColumnIndex,
                table.HeaderRowStart,
                table.HeaderRowCount,
                table.DataStartRow,
                table.FilterEmptySourceRows ?? executionConfig.FilterEmptySourceRows,
                effectiveCustomerId,
                effectiveProcessId,
                effectiveMachineModelId,
                executionConfig,
                scope);
        }

        // 遍历每个表格生成 TableFillEntry
        int totalFilled = 0, totalSkipped = 0;
        var tableEntries = new List<TableFillEntry>();
        var adoptedRowLookup = new Dictionary<int, HashSet<int>>();

        foreach (var tableFill in request.Tables)
        {
            var entry = new TableFillEntry
            {
                TableIndex = tableFill.TableIndex,
                AcceptanceColumnIndex = tableFill.AcceptanceColumnIndex,
                RemarkColumnIndex = tableFill.RemarkColumnIndex
            };
            adoptedRowLookup[tableFill.TableIndex] = new HashSet<int>();
            currentMatchLookups.TryGetValue(tableFill.TableIndex, out var currentMatchSnapshot);
            currentMatchSnapshot ??= new ExecutionMatchSnapshot();
            var currentMatchLookup = currentMatchSnapshot.MatchLookup;
            var currentSourceRowLookup = currentMatchSnapshot.SourceRowLookup;

            foreach (var mapping in tableFill.Mappings)
            {
                var selectedSpecId = mapping.SpecId ?? 0;
                if (selectedSpecId <= 0 || !specDict.TryGetValue(selectedSpecId, out var spec))
                {
                    if (TryCreateManualFillResult(mapping, out var manualFillResult))
                    {
                        entry.FillResults.Add(manualFillResult);
                        adoptedRowLookup[tableFill.TableIndex].Add(mapping.RowIndex);
                        totalFilled++;
                    }
                    else
                    {
                        totalSkipped++;
                    }
                }
                else
                {
                    currentMatchLookup.TryGetValue(mapping.RowIndex, out var currentMatch);
                    var reviewApprovalToken = reviewApprovalBundle?.Tokens.GetValueOrDefault(
                        new MatchingApprovalTokenService.ApprovalLookupKey(tableFill.TableIndex, mapping.RowIndex));
                    if (!CanApplyMatchedSpec(
                            mapping,
                            spec,
                            currentMatch,
                            currentSourceRowLookup.GetValueOrDefault(mapping.RowIndex)?.Project,
                            currentSourceRowLookup.GetValueOrDefault(mapping.RowIndex)?.Specification,
                            reviewApprovalToken))
                    {
                        totalSkipped++;
                        continue;
                    }

                    entry.FillResults.Add(new FillResult
                    {
                        RowIndex = mapping.RowIndex,
                        SpecId = spec.Id,
                        Acceptance = mapping.OverrideAcceptance ?? spec.Acceptance ?? "",
                        Remark = mapping.OverrideRemark ?? spec.Remark
                    });
                    adoptedRowLookup[tableFill.TableIndex].Add(mapping.RowIndex);
                    totalFilled++;
                }
            }

            tableEntries.Add(entry);
        }

        // 生成任务ID
        var taskId = Guid.NewGuid().ToString("N");
        var taskResult = new FillTaskResult
        {
            TaskId = taskId,
            SourceFileId = request.FileId,
            IsBatchMode = true,
            TableEntries = tableEntries,
            CreatedAt = DateTime.UtcNow
        };

        var isExcelSource = wordFile.FileType == UploadedFileType.ExcelXlsx;
        var persistedTaskResult = isExcelSource
            ? CreatePersistableTaskResult(taskResult, includeFillEntries: false)
            : taskResult;
        if (isExcelSource)
        {
            try
            {
                var renderedFile = await _matchingResultWriteBackService.RenderFillResultToSourceFileAsync(wordFile, taskResult);
                EnsureWriteBackCompleted(renderedFile.Summary);

                await _unitOfWork.BeginTransactionAsync();
                try
                {
                    await _matchingTaskSnapshotService.SaveAsync(user, persistedTaskResult, saveImmediately: false);
                    await _unitOfWork.SaveChangesAsync();

                    await SaveExecutionHistoryAsync(
                        user,
                        wordFile,
                        taskId,
                        taskResult.CreatedAt,
                        request.Tables,
                        request.PreviewTables,
                        specDict,
                        adoptedRowLookup,
                        currentMatchLookups,
                        saveImmediately: false);
                    await _unitOfWork.SaveChangesAsync();

                    await PersistExcelExecutionAsync(wordFile, renderedFile.Content);
                    await PersistDownloadArtifactAsync(taskId, persistedTaskResult, wordFile, renderedFile.Content);
                    await _unitOfWork.CommitTransactionAsync();
                }
                catch
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量填充后写回 Excel 失败: 文件{FileId}", wordFile.Id);
                throw Failure(500, $"写回 Excel 失败: {ex.Message}");
            }
        }
        else
        {
            try
            {
                var renderedFile = await _matchingResultWriteBackService.RenderFillResultToSourceFileAsync(wordFile, taskResult);
                EnsureWriteBackCompleted(renderedFile.Summary);

                await _matchingTaskSnapshotService.SaveAsync(user, persistedTaskResult);
                await PersistDownloadArtifactAsync(taskId, persistedTaskResult, wordFile, renderedFile.Content);
                await SaveExecutionHistoryAsync(
                    user,
                    wordFile,
                    taskId,
                    taskResult.CreatedAt,
                    request.Tables,
                    request.PreviewTables,
                    specDict,
                    adoptedRowLookup,
                    currentMatchLookups);
            }
            catch (MatchingApiException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量填充后固化 Word 下载产物失败: 文件{FileId}", wordFile.Id);
                throw Failure(500, $"固化下载产物失败: {ex.Message}");
            }
        }

        var response = new ExecuteFillResponse
        {
            TaskId = taskId,
            FilledCount = totalFilled,
            SkippedCount = totalSkipped,
            DownloadUrl = isExcelSource ? string.Empty : $"/api/matching/download/{taskId}"
        };

        _logger.LogInformation(
            "批量填充完成: 任务{TaskId}, 文件类型{FileType}, {TableCount}个表格, 填充{Filled}行, 跳过{Skipped}行",
            taskId, wordFile.FileType, request.Tables.Count, totalFilled, totalSkipped);

        return Result(response, isExcelSource
            ? $"批量填充完成：已填充{totalFilled}行，跳过{totalSkipped}行，已写回并可下载 Excel"
            : $"批量填充完成：已填充{totalFilled}行，跳过{totalSkipped}行");
    }

    private static void EnsureWriteBackCompleted(WriteBackSummary summary)
    {
        if (summary.RequestedCells > 0 && summary.WrittenCells == 0)
        {
            throw Failure(400, "未写入任何单元格，请检查列索引和行配置是否正确");
        }

        if (summary.WrittenCells < summary.RequestedCells)
        {
            throw Failure(500, $"写回不完整：期望写入{summary.RequestedCells}个单元格，实际仅写入{summary.WrittenCells}个");
        }
    }

    private async Task PersistExcelExecutionAsync(WordFile wordFile, byte[] updatedContent, CancellationToken cancellationToken = default)
    {
        var originalContent = await ReadSourceFileContentAsync(wordFile, cancellationToken);
        var filePersisted = false;

        try
        {
            await _documentFileAccessService.PersistUpdatedFileContentAsync(wordFile, updatedContent, cancellationToken);
            filePersisted = true;
            await _unitOfWork.SaveChangesAsync();
        }
        catch
        {
            if (filePersisted)
            {
                try
                {
                    await _documentFileAccessService.PersistUpdatedFileContentAsync(wordFile, originalContent, cancellationToken);
                }
                catch (Exception restoreEx)
                {
                    _logger.LogError(restoreEx, "Excel 源文件回滚失败: 文件{FileId}", wordFile.Id);
                }
            }

            throw;
        }
    }

    private async Task<byte[]> ReadSourceFileContentAsync(WordFile wordFile, CancellationToken cancellationToken)
    {
        await using var stream = _documentFileAccessService.OpenReadStream(wordFile);
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);
        return memoryStream.ToArray();
    }

    private async Task PersistDownloadArtifactAsync(
        string taskId,
        FillTaskResult taskResult,
        WordFile wordFile,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        await _matchingTaskSnapshotService.PersistDownloadArtifactAsync(
            taskId,
            taskResult,
            GetDownloadFileName(wordFile),
            GetDownloadContentType(wordFile.FileType),
            content,
            cancellationToken);
    }

    private static string GetDownloadFileName(WordFile wordFile)
    {
        var downloadFileName = Path.GetFileName(wordFile.FileName);
        if (!string.IsNullOrWhiteSpace(downloadFileName))
        {
            return downloadFileName;
        }

        return wordFile.FileType == UploadedFileType.ExcelXlsx ? "filled.xlsx" : "filled.docx";
    }

    private static string GetDownloadContentType(UploadedFileType fileType)
    {
        return fileType == UploadedFileType.ExcelXlsx
            ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            : "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    }

    private static FillTaskResult CreatePersistableTaskResult(FillTaskResult taskResult, bool includeFillEntries)
    {
        return new FillTaskResult
        {
            PayloadVersion = taskResult.PayloadVersion,
            TaskId = taskResult.TaskId,
            SourceFileId = taskResult.SourceFileId,
            SourceTableIndex = taskResult.SourceTableIndex,
            AcceptanceColumnIndex = taskResult.AcceptanceColumnIndex,
            RemarkColumnIndex = taskResult.RemarkColumnIndex,
            FillResults = includeFillEntries
                ? taskResult.FillResults
                    .Select(CloneFillResult)
                    .ToList()
                : [],
            FilledFilePath = taskResult.FilledFilePath,
            CreatedAt = taskResult.CreatedAt,
            IsBatchMode = taskResult.IsBatchMode,
            TableEntries = includeFillEntries
                ? taskResult.TableEntries
                    .Select(entry => new TableFillEntry
                    {
                        TableIndex = entry.TableIndex,
                        AcceptanceColumnIndex = entry.AcceptanceColumnIndex,
                        RemarkColumnIndex = entry.RemarkColumnIndex,
                        FillResults = entry.FillResults
                            .Select(CloneFillResult)
                            .ToList()
                    })
                    .ToList()
                : [],
            DownloadArtifactRelativePath = taskResult.DownloadArtifactRelativePath,
            DownloadArtifactFileName = taskResult.DownloadArtifactFileName,
            DownloadArtifactContentType = taskResult.DownloadArtifactContentType
        };
    }

    private static FillResult CloneFillResult(FillResult fillResult)
    {
        return new FillResult
        {
            RowIndex = fillResult.RowIndex,
            SpecId = fillResult.SpecId,
            Acceptance = fillResult.Acceptance,
            Remark = fillResult.Remark
        };
    }

    private static bool TryCreateManualFillResult(FillMapping mapping, out FillResult fillResult)
    {
        fillResult = null!;
        if (!mapping.ManualFill)
        {
            return false;
        }

        var hasManualValue =
            !string.IsNullOrWhiteSpace(mapping.OverrideAcceptance) ||
            !string.IsNullOrWhiteSpace(mapping.OverrideRemark);
        if (!hasManualValue)
        {
            return false;
        }

        fillResult = new FillResult
        {
            RowIndex = mapping.RowIndex,
            SpecId = 0,
            Acceptance = mapping.OverrideAcceptance ?? string.Empty,
            Remark = mapping.OverrideRemark
        };
        return true;
    }

    private async Task SaveExecutionHistoryAsync(
        ClaimsPrincipal user,
        WordFile wordFile,
        string taskId,
        DateTime createdAt,
        IReadOnlyCollection<BatchTableFillMapping> tables,
        IReadOnlyCollection<ExecutionHistoryPreviewTableSnapshot> previewTables,
        IReadOnlyDictionary<int, AcceptanceSpec> specDict,
        IReadOnlyDictionary<int, HashSet<int>> adoptedRowLookup,
        IReadOnlyDictionary<int, ExecutionMatchSnapshot> currentMatchLookups,
        bool saveImmediately = true)
    {
        var tableMetas = await _documentTableAccessService.GetTablesAsync(wordFile);
        var tableMetaLookup = tableMetas.ToDictionary(table => table.Index);
        var previewLookup = BuildExecutionHistoryPreviewLookup(previewTables);
        var fileDetail = new ExecutionHistoryFileDto
        {
            FileName = wordFile.FileName,
            FileType = wordFile.FileType
        };
        var playbackFile = new ExecutionHistorySmartFillFileDto
        {
            FileName = wordFile.FileName,
            FileType = wordFile.FileType
        };

        foreach (var table in tables.OrderBy(item => item.TableIndex))
        {
            var rows = await BuildExecutionHistoryRowsAsync(
                wordFile,
                table,
                specDict,
                adoptedRowLookup.GetValueOrDefault(table.TableIndex),
                currentMatchLookups.GetValueOrDefault(table.TableIndex)?.MatchLookup);
            var sheetName = tableMetaLookup.TryGetValue(table.TableIndex, out var meta) && !string.IsNullOrWhiteSpace(meta.Name)
                ? meta.Name!
                : $"表格 {table.TableIndex + 1}";

            fileDetail.Sheets.Add(new ExecutionHistorySheetDto
            {
                SheetIndex = table.TableIndex,
                SheetName = sheetName,
                Rows = rows
            });

            if (previewLookup.Count > 0)
            {
                playbackFile.Sheets.Add(new ExecutionHistorySmartFillSheetDto
                {
                    SheetIndex = table.TableIndex,
                    SheetName = sheetName,
                    Rows = BuildSmartFillPlaybackRows(
                        rows,
                        table.Mappings,
                        previewLookup.GetValueOrDefault(table.TableIndex))
                });
            }
        }

        var playback = previewLookup.Count > 0
            ? new ExecutionHistorySmartFillPlaybackDto
            {
                PayloadVersion = ExecutionHistoryDraft.CurrentSmartFillPlaybackVersion,
                Files = [playbackFile]
            }
            : null;

        await _executionHistoryAppService.SaveAsync(user, new ExecutionHistoryDraft
        {
            TaskId = taskId,
            TaskType = ExecutionHistoryTaskTypes.SmartFill,
            SourceFileId = wordFile.Id,
            SourceFileName = wordFile.FileName,
            SourceFileType = wordFile.FileType,
            CreatedAt = createdAt,
            Files = [fileDetail],
            SmartFillSummary = playback == null ? null : BuildSmartFillSummary(playback),
            SmartFillPlayback = playback
        }, saveImmediately: saveImmediately);
    }

    private async Task<List<ExecutionHistoryRowDto>> BuildExecutionHistoryRowsAsync(
        WordFile wordFile,
        BatchTableFillMapping table,
        IReadOnlyDictionary<int, AcceptanceSpec> specDict,
        HashSet<int>? adoptedRows,
        IReadOnlyDictionary<int, MatchResult>? currentMatchLookup)
    {
        var mappingLookup = table.Mappings.ToDictionary(item => item.RowIndex);
        var sourceRows = new List<MatchSourceItem>();

        if (table.ProjectColumnIndex.HasValue && table.SpecificationColumnIndex.HasValue)
        {
            sourceRows = await _documentTableAccessService.ExtractMatchSourceItemsAsync(
                wordFile,
                table.TableIndex,
                table.ProjectColumnIndex.Value,
                table.SpecificationColumnIndex.Value,
                table.HeaderRowStart,
                table.HeaderRowCount,
                table.DataStartRow,
                table.FilterEmptySourceRows ?? true);
        }

        if (sourceRows.Count == 0)
        {
            return table.Mappings
                .OrderBy(item => item.RowIndex)
                .Select(item => BuildExecutionHistoryRow(
                    item.RowIndex,
                    string.Empty,
                    string.Empty,
                    mappingLookup.GetValueOrDefault(item.RowIndex),
                    specDict,
                    adoptedRows,
                    currentMatchLookup?.GetValueOrDefault(item.RowIndex),
                    table.AcceptanceColumnIndex,
                    table.RemarkColumnIndex))
                .ToList();
        }

        return sourceRows
            .OrderBy(item => item.RowIndex)
            .Select(item => BuildExecutionHistoryRow(
                item.RowIndex,
                item.Project,
                item.Specification,
                mappingLookup.GetValueOrDefault(item.RowIndex),
                specDict,
                adoptedRows,
                currentMatchLookup?.GetValueOrDefault(item.RowIndex),
                table.AcceptanceColumnIndex,
                table.RemarkColumnIndex))
            .ToList();
    }

    private ExecutionHistoryRowDto BuildExecutionHistoryRow(
        int rowIndex,
        string project,
        string specification,
        FillMapping? mapping,
        IReadOnlyDictionary<int, AcceptanceSpec> specDict,
        HashSet<int>? adoptedRows,
        MatchResult? currentMatch,
        int acceptanceColumnIndex,
        int? remarkColumnIndex)
    {
        var selectedSpecId = mapping?.SpecId ?? 0;
        AcceptanceSpec? matchedSpec = null;
        var hasSpec = selectedSpecId > 0 && specDict.TryGetValue(selectedSpecId, out matchedSpec);
        var confidencePercent = currentMatch != null &&
                                currentMatch.MatchedSpecId == selectedSpecId &&
                                currentMatch.Score > 0
            ? Math.Round(currentMatch.Score * 100, 1)
            : 0;

        if (mapping == null || !hasSpec)
        {
            return new ExecutionHistoryRowDto
            {
                RowIndex = rowIndex,
                Project = project,
                Specification = specification,
                ConfidencePercent = 0,
                Status = ExecutionHistoryStatuses.Unmatched,
                IsManualSelected = false,
                AcceptanceColumnIndex = acceptanceColumnIndex,
                RemarkColumnIndex = remarkColumnIndex
            };
        }

        var status = adoptedRows?.Contains(rowIndex) == true
            ? ExecutionHistoryStatuses.Adopted
            : ExecutionHistoryStatuses.NotAdopted;

        return new ExecutionHistoryRowDto
        {
            RowIndex = rowIndex,
            Project = project,
            Specification = specification,
            MatchedSpecId = matchedSpec!.Id,
            MatchedProject = matchedSpec.Project,
            MatchedSpecification = matchedSpec.Specification,
            Acceptance = mapping.OverrideAcceptance ?? matchedSpec.Acceptance,
            Remark = mapping.OverrideRemark ?? matchedSpec.Remark,
            ConfidencePercent = confidencePercent,
            Status = status,
            IsManualSelected = mapping.ManualConfirmed,
            AcceptanceColumnIndex = acceptanceColumnIndex,
            RemarkColumnIndex = remarkColumnIndex
        };
    }

    private static IReadOnlyDictionary<int, IReadOnlyDictionary<int, MatchPreviewItem>> BuildExecutionHistoryPreviewLookup(
        IReadOnlyCollection<ExecutionHistoryPreviewTableSnapshot>? previewTables)
    {
        if (previewTables == null || previewTables.Count == 0)
        {
            return new Dictionary<int, IReadOnlyDictionary<int, MatchPreviewItem>>();
        }

        return previewTables.ToDictionary(
            table => table.TableIndex,
            table => (IReadOnlyDictionary<int, MatchPreviewItem>)table.Items
                .GroupBy(item => item.RowIndex)
                .Select(group => group.Last())
                .ToDictionary(item => item.RowIndex));
    }

    private static List<ExecutionHistorySmartFillRowDto> BuildSmartFillPlaybackRows(
        IReadOnlyCollection<ExecutionHistoryRowDto> rows,
        IReadOnlyCollection<FillMapping> mappings,
        IReadOnlyDictionary<int, MatchPreviewItem>? previewLookup)
    {
        var mappingLookup = mappings.ToDictionary(item => item.RowIndex);

        return rows
            .OrderBy(row => row.RowIndex)
            .Select(row =>
            {
                mappingLookup.TryGetValue(row.RowIndex, out var mapping);
                var previewItem = previewLookup?.GetValueOrDefault(row.RowIndex);
                var matchOrigin = ResolveMatchOrigin(previewItem);
                var manualEdited = mapping != null &&
                                   (mapping.OverrideAcceptance != null || mapping.OverrideRemark != null);

                return new ExecutionHistorySmartFillRowDto
                {
                    RowIndex = row.RowIndex,
                    SourceProject = row.Project,
                    SourceSpecification = row.Specification,
                    Status = row.Status,
                    MatchOrigin = matchOrigin,
                    IsManualConfirmed = mapping?.ManualConfirmed == true,
                    IsManualEdited = manualEdited,
                    DisplayTags = BuildDisplayTags(
                        matchOrigin,
                        mapping?.ManualConfirmed == true,
                        manualEdited,
                        row.Status),
                    PreviewSnapshot = BuildPersistedPreviewSnapshot(previewItem, matchOrigin),
                    ExecutionSnapshot = new ExecutionHistorySmartFillExecutionSnapshotDto
                    {
                        SelectedSpecId = row.MatchedSpecId,
                        SelectedProject = row.MatchedProject,
                        SelectedSpecification = row.MatchedSpecification,
                        FinalAcceptance = row.Acceptance,
                        FinalRemark = row.Remark,
                        OverrideAcceptance = mapping?.OverrideAcceptance,
                        OverrideRemark = mapping?.OverrideRemark,
                        ManualConfirmed = mapping?.ManualConfirmed == true,
                        ManualEdited = manualEdited,
                        Status = row.Status
                    }
                };
            })
            .ToList();
    }

    private static ExecutionHistorySmartFillPreviewSnapshotDto BuildPersistedPreviewSnapshot(
        MatchPreviewItem? previewItem,
        string matchOrigin)
    {
        var isExactMatch = string.Equals(matchOrigin, ExecutionHistoryMatchOrigins.Exact, StringComparison.Ordinal);
        return new ExecutionHistorySmartFillPreviewSnapshotDto
        {
            ConfidenceLevel = previewItem?.ConfidenceLevel ?? "none",
            NoMatchReason = previewItem?.NoMatchReason,
            BestMatch = previewItem?.BestMatch == null
                ? null
                : BuildPersistedBestMatchSnapshot(previewItem.BestMatch, isExactMatch)
        };
    }

    private static MatchResultDto BuildPersistedBestMatchSnapshot(MatchResultDto bestMatch, bool isExactMatch)
    {
        return new MatchResultDto
        {
            SpecId = bestMatch.SpecId,
            Project = bestMatch.Project,
            Specification = bestMatch.Specification,
            Acceptance = isExactMatch ? null : bestMatch.Acceptance,
            Remark = isExactMatch ? null : bestMatch.Remark,
            Score = bestMatch.Score,
            EmbeddingScore = bestMatch.EmbeddingScore,
            ScoreDetails = isExactMatch
                ? []
                : new Dictionary<string, double>(bestMatch.ScoreDetails),
            Decision = bestMatch.Decision,
            EvidenceSummary = isExactMatch ? [] : [.. bestMatch.EvidenceSummary],
            ConflictSummary = isExactMatch ? [] : [.. bestMatch.ConflictSummary],
            Issues = isExactMatch ? [] : [.. bestMatch.Issues.Select(CloneIssueDto)],
            Entities = [],
            TopCandidates = isExactMatch
                ? []
                : [.. bestMatch.TopCandidates.Select(CloneCandidateDto)],
            RecalledCandidateCount = isExactMatch
                ? Math.Min(bestMatch.RecalledCandidateCount, 1)
                : bestMatch.RecalledCandidateCount,
            IsAmbiguous = bestMatch.IsAmbiguous,
            ScoreGap = isExactMatch ? null : bestMatch.ScoreGap,
            RerankSummary = isExactMatch ? null : bestMatch.RerankSummary,
            SelectionMode = bestMatch.SelectionMode,
            SelectionSummary = bestMatch.SelectionSummary,
            LlmEquivalence = isExactMatch ? null : bestMatch.LlmEquivalence,
            ReviewApprovalToken = null
        };
    }

    private static MatchCandidateDto CloneCandidateDto(MatchCandidateDto candidate)
    {
        return new MatchCandidateDto
        {
            Rank = candidate.Rank,
            SpecId = candidate.SpecId,
            Project = candidate.Project,
            Specification = candidate.Specification,
            Acceptance = candidate.Acceptance,
            Remark = candidate.Remark,
            Score = candidate.Score,
            EmbeddingScore = candidate.EmbeddingScore,
            ScoreDetails = new Dictionary<string, double>(candidate.ScoreDetails),
            Decision = candidate.Decision,
            EvidenceSummary = [.. candidate.EvidenceSummary],
            ConflictSummary = [.. candidate.ConflictSummary],
            Issues = [.. candidate.Issues.Select(CloneIssueDto)],
            Entities = [],
            RerankSummary = candidate.RerankSummary,
            SelectionMode = candidate.SelectionMode,
            SelectionSummary = candidate.SelectionSummary,
            LlmEquivalence = candidate.LlmEquivalence
        };
    }

    private static MatchIssueDto CloneIssueDto(MatchIssueDto issue)
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

    private static ExecutionHistorySmartFillSummaryDto BuildSmartFillSummary(
        ExecutionHistorySmartFillPlaybackDto playback)
    {
        var rows = playback.Files
            .SelectMany(file => file.Sheets)
            .SelectMany(sheet => sheet.Rows)
            .ToList();

        return new ExecutionHistorySmartFillSummaryDto
        {
            ExactMatchedRowCount = rows.Count(row => row.MatchOrigin == ExecutionHistoryMatchOrigins.Exact),
            AiMatchedRowCount = rows.Count(row => row.MatchOrigin == ExecutionHistoryMatchOrigins.Ai),
            ManualConfirmedRowCount = rows.Count(row => row.IsManualConfirmed),
            ManualEditedRowCount = rows.Count(row => row.IsManualEdited),
            NotUsedRowCount = rows.Count(row => row.Status != ExecutionHistoryStatuses.Adopted),
            HasPlaybackArchive = true
        };
    }

    private static string ResolveMatchOrigin(MatchPreviewItem? previewItem)
    {
        if (string.Equals(previewItem?.BestMatch?.SelectionMode, "exactShortcut", StringComparison.Ordinal))
        {
            return ExecutionHistoryMatchOrigins.Exact;
        }

        if (previewItem?.HasMatch == true)
        {
            return ExecutionHistoryMatchOrigins.Ai;
        }

        return ExecutionHistoryMatchOrigins.None;
    }

    private static List<string> BuildDisplayTags(
        string matchOrigin,
        bool isManualConfirmed,
        bool isManualEdited,
        string status)
    {
        var tags = new List<string>();

        if (string.Equals(matchOrigin, ExecutionHistoryMatchOrigins.Exact, StringComparison.Ordinal))
        {
            tags.Add(ExecutionHistoryDisplayTags.ExactMatch);
        }
        else if (string.Equals(matchOrigin, ExecutionHistoryMatchOrigins.Ai, StringComparison.Ordinal))
        {
            tags.Add(ExecutionHistoryDisplayTags.AiMatch);
        }

        if (isManualConfirmed)
        {
            tags.Add(ExecutionHistoryDisplayTags.ManualConfirm);
        }

        if (isManualEdited)
        {
            tags.Add(ExecutionHistoryDisplayTags.ManualWrite);
        }

        if (!string.Equals(status, ExecutionHistoryStatuses.Adopted, StringComparison.Ordinal))
        {
            tags.Add(ExecutionHistoryDisplayTags.NotUsed);
        }

        return tags;
    }

    private async Task<MatchingConfig> ResolveExecutionMatchingConfigAsync(MatchConfigDto? dto)
    {
        return await ConvertToMatchingConfigAsync(dto);
    }

    private async Task<ExecutionMatchSnapshot> BuildCurrentMatchLookupAsync(
        WordFile wordFile,
        int tableIndex,
        int? projectColumnIndex,
        int? specificationColumnIndex,
        int? headerRowStart,
        int? headerRowCount,
        int? dataStartRow,
        bool filterEmptySourceRows,
        int? customerId,
        int? processId,
        int? machineModelId,
        MatchingConfig config,
        DataScopeResult scope)
    {
        if (!projectColumnIndex.HasValue || !specificationColumnIndex.HasValue)
        {
            return new ExecutionMatchSnapshot();
        }

        var sourceRows = await _documentTableAccessService.ExtractMatchSourceItemsAsync(
            wordFile,
            tableIndex,
            projectColumnIndex.Value,
            specificationColumnIndex.Value,
            headerRowStart,
            headerRowCount,
            dataStartRow,
            filterEmptySourceRows);

        if (sourceRows.Count == 0)
        {
            throw Failure(400, "无法重建执行前的源项目/规格数据，请重新预览后再执行");
        }

        var sourceRowLookup = sourceRows.ToDictionary(item => item.RowIndex);

        var candidates = await GetCandidatesAsync(
            customerId,
            processId,
            machineModelId,
            scope,
            config.EmbeddingServiceId,
            hydrateEmbeddings: !config.ExactMatchOnly);

        if (candidates.Count == 0)
        {
            return new ExecutionMatchSnapshot
            {
                SourceRowLookup = sourceRowLookup
            };
        }

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

        var sourceItems = sourceRows.Select(item => new MatchSource
        {
            Project = tpSession.Process(item.Project),
            Specification = tpSession.Process(item.Specification)
        }).ToList();

        BatchMatchResult batchResult;
        try
        {
            batchResult = config.ExactMatchOnly
                ? BuildExactMatchBatchResult(sourceItems, processedCandidates, config)
                : await _matchingService.BatchMatchAsync(sourceItems, processedCandidates, config);
        }
        catch (AiServiceUnavailableException ex)
        {
            throw Failure(400, $"Embedding 服务不可用: {ex.Reason}");
        }

        var lookup = new Dictionary<int, MatchResult>();
        for (var index = 0; index < sourceRows.Count && index < batchResult.Results.Count; index++)
        {
            var result = batchResult.Results[index];
            if (!result.MatchedSpecId.HasValue)
            {
                continue;
            }

            lookup[sourceRows[index].RowIndex] = result;
        }

        return new ExecutionMatchSnapshot
        {
            MatchLookup = lookup,
            SourceRowLookup = sourceRowLookup
        };
    }

    private async Task<List<MatchLlmStreamItem>> BuildAuthoritativeLlmStreamItemsAsync(
        IReadOnlyList<MatchLlmStreamItem> requestItems,
        IReadOnlyList<MatchCandidate> candidates,
        MatchingConfig config)
    {
        if (requestItems.Count == 0)
        {
            return [];
        }

        if (candidates.Count == 0)
        {
            return requestItems.Select(CreateNoMatchLlmStreamItem).ToList();
        }

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

        var sourceItems = requestItems.Select(item => new MatchSource
        {
            Project = tpSession.Process(item.SourceProject),
            Specification = tpSession.Process(item.SourceSpecification)
        }).ToList();

        BatchMatchResult batchResult;
        try
        {
            batchResult = await _matchingService.BatchMatchAsync(sourceItems, processedCandidates, config);
        }
        catch (AiServiceUnavailableException ex)
        {
            throw Failure(400, $"Embedding 服务不可用: {ex.Reason}");
        }

        var normalizedItems = new List<MatchLlmStreamItem>(requestItems.Count);
        for (var index = 0; index < requestItems.Count; index++)
        {
            var requestItem = requestItems[index];
            var result = index < batchResult.Results.Count
                ? batchResult.Results[index]
                : null;

            normalizedItems.Add(CreateAuthoritativeLlmStreamItem(requestItem, result));
        }

        return normalizedItems;
    }

    private void EnsureExecutionPreviewContext(int? projectColumnIndex, int? specificationColumnIndex, int? tableIndex = null)
    {
        if (projectColumnIndex.HasValue && specificationColumnIndex.HasValue)
        {
            return;
        }

        var prefix = tableIndex.HasValue
            ? $"表格{tableIndex.Value}执行填充"
            : "执行填充";
        throw Failure(400, $"{prefix}必须提供项目列索引和规格列索引，请重新预览后再执行");
    }

    /// <summary>
    /// 获取候选验收规格列表（含 EmbeddingCache 复用）
    /// </summary>
    private async Task<List<MatchCandidate>> GetCandidatesAsync(
        int? customerId,
        int? processId,
        int? machineModelId,
        DataScopeResult scope,
        int? embeddingServiceId,
        bool hydrateEmbeddings = true)
    {
        var baseQuery = BuildCandidateSpecQuery(customerId, processId, machineModelId);
        var scopedQuery = ApplySpecScopeToQuery(baseQuery, scope);
        var rawCount = await baseQuery.CountAsync();
        var scopedCount = await scopedQuery.CountAsync();
        EnsureCandidateScopeWithinLimit(scopedCount, customerId, processId, machineModelId);

        var scopedSpecs = await scopedQuery
            .Select(s => new CandidateSpecRow
            {
                Id = s.Id,
                Project = s.Project,
                Specification = s.Specification,
                Acceptance = s.Acceptance,
                Remark = s.Remark,
                ImportedAt = s.ImportedAt
            })
            .ToListAsync();

        // 同一范围内可能存在重复导入（项目+规格相同，但验收/备注完整度不同）。
        // 这里先做候选去重，优先保留“验收标准非空 > 备注非空 > 导入时间新 > ID大”的记录，
        // 避免匹配命中到信息缺失的旧记录。
        var dedupedSpecs = scopedSpecs
            .GroupBy(s => BuildCandidateDedupKey(s.Project, s.Specification))
            .Select(g => g
                .OrderByDescending(s => HasText(s.Acceptance))
                .ThenByDescending(s => HasText(s.Remark))
                .ThenByDescending(s => s.ImportedAt)
                .ThenByDescending(s => s.Id)
                .First())
            .ToList();

        _logger.LogInformation(
            "匹配候选去重: 原始{RawCount}条, 范围内{ScopedCount}条 -> 去重后{DedupedCount}条 (customerId={CustomerId}, processId={ProcessId}, machineModelId={MachineModelId})",
            rawCount, scopedCount, dedupedSpecs.Count, customerId, processId, machineModelId);

        var candidates = dedupedSpecs.Select(s => new MatchCandidate
        {
            SpecId = s.Id,
            Project = s.Project,
            Specification = s.Specification,
            Acceptance = s.Acceptance,
            Remark = s.Remark
        }).ToList();

        // 复用 EmbeddingCache（避免每次都重新调用 Embedding API）
        if (hydrateEmbeddings)
        {
            await HydrateCandidateEmbeddingsAsync(candidates, embeddingServiceId);
        }

        return candidates;
    }

    private async Task EnsureEmbeddingServiceConfiguredAsync(int? embeddingServiceId)
    {
        var configs = await _aiServiceSelector.GetCandidatesAsync(
            CoreAiServicePurpose.Embedding,
            embeddingServiceId);
        if (configs.Count == 0)
        {
            throw Failure(400, "Embedding 服务不可用: 未检测到可用的 Embedding 服务配置");
        }
    }

    private static BatchMatchResult BuildExactMatchBatchResult(
        IReadOnlyList<MatchSource> sources,
        IReadOnlyList<MatchCandidate> candidates,
        MatchingConfig config)
    {
        var lookup = candidates
            .GroupBy(candidate => BuildCandidateDedupKey(candidate.Project, candidate.Specification))
            .ToDictionary(group => group.Key, group => group.First());

        return new BatchMatchResult
        {
            Results = sources
                .Select(source =>
                {
                    var key = BuildCandidateDedupKey(source.Project, source.Specification);
                    return lookup.TryGetValue(key, out var candidate)
                        ? CreateExactMatchResult(source, candidate, config)
                        : new MatchResult
                        {
                            SourceText = source.CombinedText,
                            MinScoreThreshold = config.MinScoreThreshold,
                            HighConfidenceThreshold = config.HighConfidenceThreshold,
                            Decision = MatchDecision.ManualReview
                        };
                })
                .ToList()
        };
    }

    private static MatchResult CreateExactMatchResult(
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
                    LlmEquivalence = equivalence
                }
            ]
        };
    }

    /// <summary>
    /// 从缓存读取候选项的 Embedding，生成缺失的向量并写入缓存
    /// </summary>
    private async Task HydrateCandidateEmbeddingsAsync(List<MatchCandidate> candidates, int? embeddingServiceId)
    {
        string? embeddingModel = null;
        IReadOnlyList<EmbeddingCache> caches = [];

        if (embeddingServiceId.HasValue)
        {
            var configs = await _aiServiceSelector.GetCandidatesAsync(CoreAiServicePurpose.Embedding, embeddingServiceId);
            var config = configs.FirstOrDefault();
            embeddingModel = config?.EmbeddingModel?.Trim();
        }

        if (!string.IsNullOrWhiteSpace(embeddingModel))
        {
            caches = await _unitOfWork.EmbeddingCaches.GetBySpecIdsAndModelAsync(
                candidates.Select(c => c.SpecId),
                embeddingModel);

            var cacheLookup = caches.ToDictionary(c => c.SpecId);
            foreach (var candidate in candidates)
            {
                if (cacheLookup.TryGetValue(candidate.SpecId, out var cache))
                {
                    candidate.Embedding = DeserializeVector(cache.Vector);
                }
            }
        }

        var missingCandidates = candidates.Where(c => c.Embedding == null || c.Embedding.Length == 0).ToList();
        if (missingCandidates.Count == 0)
        {
            _logger.LogDebug("匹配候选 Embedding 全部命中缓存，跳过远程调用");
            return;
        }

        var missingTexts = missingCandidates
            .Select(c => c.CombinedText)
            .ToList();

        List<float[]> newEmbeddings;
        try
        {
            newEmbeddings = await GenerateEmbeddingsInBatchesAsync(missingTexts, embeddingServiceId);
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
            var existingCacheLookup = caches.ToDictionary(c => c.SpecId);
            var hasMutation = false;

            for (var i = 0; i < missingCandidates.Count; i++)
            {
                if (i < newEmbeddings.Count && newEmbeddings[i].Length > 0)
                {
                    missingCandidates[i].Embedding = newEmbeddings[i];

                    var specId = missingCandidates[i].SpecId;
                    if (existingCacheLookup.TryGetValue(specId, out var existingCache))
                    {
                        existingCache.Vector = SerializeVector(newEmbeddings[i]);
                        existingCache.CreatedAt = DateTime.UtcNow;
                        _unitOfWork.EmbeddingCaches.Update(existingCache);
                    }
                    else
                    {
                        await _unitOfWork.EmbeddingCaches.AddAsync(new EmbeddingCache
                        {
                            SpecId = specId,
                            ModelName = embeddingModel,
                            Vector = SerializeVector(newEmbeddings[i]),
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    hasMutation = true;
                }
            }

            if (hasMutation)
            {
                await _unitOfWork.SaveChangesAsync();
            }
        }
        else
        {
            for (var i = 0; i < missingCandidates.Count && i < newEmbeddings.Count; i++)
            {
                missingCandidates[i].Embedding = newEmbeddings[i];
            }
        }

        _logger.LogInformation(
            "匹配候选 Embedding: 命中缓存{Cached}个, 新生成{Generated}个",
            candidates.Count - missingCandidates.Count, missingCandidates.Count);
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

    private IQueryable<AcceptanceSpec> BuildCandidateSpecQuery(
        int? customerId,
        int? processId,
        int? machineModelId)
    {
        var query = _unitOfWork.AcceptanceSpecs.Query();

        if (customerId.HasValue)
        {
            query = query.Where(s => s.CustomerId == customerId.Value);
        }

        if (processId.HasValue)
        {
            query = query.Where(s => s.ProcessId == processId.Value);
        }

        if (machineModelId.HasValue)
        {
            query = query.Where(s => s.MachineModelId == machineModelId.Value);
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

        var scopedOrgUnitIds = scope.OrgUnitIds
            .Distinct()
            .ToArray();

        if (scope.IncludeSelf && scopedOrgUnitIds.Length > 0)
        {
            return query.Where(s =>
                (s.CreatedByUserId.HasValue && s.CreatedByUserId.Value == scope.UserId) ||
                (s.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(s.OwnerOrgUnitId.Value)));
        }

        if (scope.IncludeSelf)
        {
            return query.Where(s =>
                s.CreatedByUserId.HasValue && s.CreatedByUserId.Value == scope.UserId);
        }

        if (scopedOrgUnitIds.Length > 0)
        {
            return query.Where(s =>
                s.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(s.OwnerOrgUnitId.Value));
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
            return string.Empty;

        return string.Join(" ", value
            .Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static void EnsureDistinctBatchTableIndexes(IReadOnlyCollection<BatchTableFillMapping> tables)
    {
        var uniqueCount = tables
            .Select(table => table.TableIndex)
            .Distinct()
            .Count();
        if (uniqueCount != tables.Count)
        {
            throw Failure(400, "存在重复的表格索引，请删除重复表格后重试");
        }
    }

    private static void EnsureDistinctFillMappings(IReadOnlyCollection<FillMapping> mappings, string message)
    {
        var uniqueCount = mappings
            .Select(mapping => mapping.RowIndex)
            .Distinct()
            .Count();
        if (uniqueCount != mappings.Count)
        {
            throw Failure(400, message);
        }
    }

    private static void EnsureDistinctLlmStreamItems(IReadOnlyCollection<MatchLlmStreamItem> items)
    {
        var uniqueCount = items
            .Select(item => (item.TableIndex, item.RowIndex))
            .Distinct()
            .Count();
        if (uniqueCount != items.Count)
        {
            throw Failure(400, "同一行存在重复的复核请求，请刷新预览后重试");
        }
    }

    private static bool HasText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    /// 转换为匹配配置
    /// </summary>
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
            ExactMatchOnly = dto?.ExactMatchOnly ?? fallbackConfig.ExactMatchOnly,
            FilterEmptySourceRows = dto?.FilterEmptySourceRows ?? fallbackConfig.FilterEmptySourceRows
        };
    }

    private async Task<int> ResolveDefaultRecallTopKAsync(int? embeddingServiceId)
    {
        var fallbackConfig = new MatchingConfig();
        var query = _unitOfWork.AiServiceConfigs
            .Query()
            .AsNoTracking()
            .Where(item =>
                !item.IsDisabled &&
                (item.Purpose & AiServicePurpose.Embedding) == AiServicePurpose.Embedding);

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

    private static bool CanApplyMatchedSpec(
        FillMapping mapping,
        AcceptanceSpec selectedSpec,
        MatchResult? currentMatch,
        string? sourceProject,
        string? sourceSpecification,
        MatchingApprovalTokenService.ApprovalTokenPayload? reviewApprovalToken)
    {
        if (reviewApprovalToken != null)
        {
            return MatchesPreviewApprovalToken(
                reviewApprovalToken,
                mapping.SpecId ?? 0,
                sourceProject,
                sourceSpecification,
                selectedSpec);
        }

        if (currentMatch == null || !currentMatch.MatchedSpecId.HasValue)
        {
            return false;
        }

        if (mapping.SpecId != currentMatch.MatchedSpecId)
        {
            return false;
        }

        if (currentMatch.Decision == MatchDecision.Reject)
        {
            return false;
        }

        if (RequiresManualReviewByEquivalenceVerdict(currentMatch.LlmEquivalence?.Verdict.ToString()))
        {
            return false;
        }

        if (mapping.ManualConfirmed)
        {
            return true;
        }

        if (currentMatch.Decision == MatchDecision.AutoApply)
        {
            return true;
        }

        return false;
    }

    private static bool MatchesPreviewApprovalToken(
        MatchingApprovalTokenService.ApprovalTokenPayload reviewApprovalToken,
        int selectedSpecId,
        string? sourceProject,
        string? sourceSpecification,
        AcceptanceSpec selectedSpec)
    {
        return reviewApprovalToken.SpecId == selectedSpecId &&
               string.Equals(reviewApprovalToken.SourceProject, NormalizeForDedup(sourceProject), StringComparison.Ordinal) &&
               string.Equals(reviewApprovalToken.SourceSpecification, NormalizeForDedup(sourceSpecification), StringComparison.Ordinal) &&
               string.Equals(
                   reviewApprovalToken.SpecFingerprint,
                   ComputeReviewApprovalSpecFingerprint(
                       selectedSpec.Project,
                       selectedSpec.Specification,
                       selectedSpec.Acceptance,
                       selectedSpec.Remark),
                   StringComparison.Ordinal);
    }

    private static bool RequiresManualReviewByEquivalenceVerdict(string? verdict)
    {
        return string.Equals(verdict, "different", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(verdict, "uncertain", StringComparison.OrdinalIgnoreCase);
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

        if (result.LlmEquivalence?.Verdict == LlmEquivalenceVerdict.Equivalent ||
            result.Score >= NormalizeHighConfidenceThreshold(highConfidenceThreshold))
        {
            return "high";
        }

        if (result.Score >= minScoreThreshold)
        {
            return "medium";
        }

        return "low";
    }

    private static double NormalizeLlmReviewScore(double? reviewScore)
    {
        if (!reviewScore.HasValue)
        {
            return 0;
        }

        var normalized = reviewScore.Value;
        if (normalized > 0 && normalized <= 1)
        {
            normalized *= 100;
        }

        return Math.Clamp(normalized, 0, 100);
    }

    private static string ComputeReviewApprovalSpecFingerprint(
        string? project,
        string? specification,
        string? acceptance,
        string? remark)
    {
        var normalized = string.Join('\n', [
            NormalizeForDedup(project),
            NormalizeForDedup(specification),
            NormalizeForDedup(acceptance),
            NormalizeForDedup(remark)
        ]);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// 转换为匹配结果DTO
    /// </summary>
    private static MatchResultDto ConvertToMatchResultDto(MatchResult result)
    {
        return new MatchResultDto
        {
            SpecId = result.MatchedSpecId ?? 0,
            Project = result.MatchedProject ?? "",
            Specification = result.MatchedSpecification ?? "",
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
            SelectionSummary = result.SelectionSummary
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

    private async Task<LlmStepOutcome> StreamLlmReviewAsync(
        HttpResponse response,
        MatchLlmStreamItem item,
        MatchingConfig config,
        DataScopeResult scope,
        int? customerId,
        int? processId,
        int? machineModelId,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<int, MatchCandidate> accessibleSpecLookup,
        ILlmReviewService reviewService,
        ConcurrentDictionary<LlmStreamItemKey, byte> reviewTerminalLookup,
        SemaphoreSlim sseWriteLock)
    {
        var specId = item.BestMatchSpecId ?? 0;
        if (specId <= 0)
            return LlmStepOutcome.Failed;

        var location = FormatStreamItemLocation(item);

        if (RequiresManualReviewByEquivalenceVerdict(item.LlmEquivalenceVerdict))
        {
            await WriteSseEventLockedAsync(response, sseWriteLock, "review.done", new
            {
                tableIndex = item.TableIndex,
                rowIndex = item.RowIndex,
                score = 0,
                reason = "AI 等价裁决已要求人工确认，跳过旧复核",
                commentary = "保留 AI 等价裁决结果，不再用旧 LLM 复核反向放行",
                decision = "manualReview"
            }, cancellationToken);
            reviewTerminalLookup.TryAdd(GetLlmStreamItemKey(item), 0);
            return LlmStepOutcome.Success;
        }

        if (!item.IsAmbiguous)
        {
            return LlmStepOutcome.Success;
        }

        if (!accessibleSpecLookup.TryGetValue(specId, out var spec))
        {
            _logger.LogWarning("[LLM复核] {Location}: 最佳匹配规格ID={SpecId}不存在或无权限", location, specId);
            throw new LlmStepFailureException("最佳匹配规格不存在或无权限", "manualReview");
        }

        _logger.LogDebug(
            "[LLM复核] {Location}: 源=[{SrcProj}/{SrcSpec}] 匹配=[{MatchProj}/{MatchSpec}] 基础得分={Score:P1}",
            location, item.SourceProject, item.SourceSpecification,
            spec.Project, spec.Specification, item.BestMatchScore ?? 0);

        var reviewRequest = new LlmReviewRequest
        {
            SourceProject = item.SourceProject,
            SourceSpecification = item.SourceSpecification,
            BestMatchProject = spec.Project,
            BestMatchSpecification = spec.Specification,
            BestMatchAcceptance = spec.Acceptance,
            BestMatchRemark = spec.Remark,
            BaseScore = (item.BestMatchScore ?? 0) * 100,
            ScoreDetails = item.ScoreDetails ?? new Dictionary<string, double>(),
            CurrentDecision = item.Decision ?? "manualReview",
            EvidenceSummary = item.EvidenceSummary ?? [],
            ConflictSummary = item.ConflictSummary ?? [],
            ReviewTrigger = BuildReviewTrigger(item),
            LlmServiceId = config.LlmServiceId,
            ReviewScene = LlmReviewScene.MatchingReview
        };

        await WriteSseEventLockedAsync(response, sseWriteLock, "review.start", new
        {
            tableIndex = item.TableIndex,
            rowIndex = item.RowIndex
        }, cancellationToken);

        var buffer = new StringBuilder();
        try
        {
            await foreach (var chunk in reviewService.ReviewStreamAsync(reviewRequest, cancellationToken))
            {
                buffer.Append(chunk);
                await WriteSseEventLockedAsync(response, sseWriteLock, "review.delta", new
                {
                    tableIndex = item.TableIndex,
                    rowIndex = item.RowIndex,
                    chunk
                }, cancellationToken);
            }

            if (reviewService.TryParseReviewResult(buffer.ToString(), out var result))
            {
                var normalizedScore = NormalizeLlmReviewScore(result.Score);
                var passed = normalizedScore >= MatchingThresholds.LlmReviewPassScore;
                var reviewApprovalToken = passed
                    ? _approvalTokenService.IssueToken(
                        scope.UserId,
                        item.TableIndex,
                        item.RowIndex,
                        item.BestMatchSpecId ?? 0,
                        item.SourceProject,
                        item.SourceSpecification,
                        spec.Project,
                        spec.Specification,
                        spec.Acceptance,
                        spec.Remark,
                        customerId,
                        processId,
                        machineModelId,
                        config)
                    : null;
                _logger.LogDebug("[LLM复核] {Location}: 完成, score={Score}, reason={Reason}",
                    location, normalizedScore, result.Reason);
                await WriteSseEventLockedAsync(response, sseWriteLock, "review.done", new
                {
                    tableIndex = item.TableIndex,
                    rowIndex = item.RowIndex,
                    score = normalizedScore,
                    reason = result.Reason,
                    commentary = result.Commentary,
                    decision = passed ? "autoApply" : "manualReview",
                    reviewApprovalToken
                }, cancellationToken);
                reviewTerminalLookup.TryAdd(GetLlmStreamItemKey(item), 0);
                return LlmStepOutcome.Success;
            }
            else
            {
                _logger.LogWarning("[LLM复核] {Location}: JSON解析失败, 原始输出: {Raw}", location, buffer.ToString());
                throw new LlmStepFailureException("LLM复核输出解析失败", "manualReview");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AiServiceUnavailableException ex)
        {
            _logger.LogWarning(ex, "LLM复核失败");
            throw new LlmStepFailureException(ex.Reason, "manualReview", ex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM复核失败");
            if (ex is LlmStepFailureException)
            {
                throw;
            }

            throw new LlmStepFailureException("LLM复核失败", "manualReview", ex);
        }
    }

    private static LlmStreamItemKey GetLlmStreamItemKey(MatchLlmStreamItem item)
    {
        return new LlmStreamItemKey(item.TableIndex, item.RowIndex);
    }

    private static bool RequiresReviewForStreamItem(MatchLlmStreamItem item)
    {
        return item.BestMatchSpecId.HasValue &&
               (item.IsAmbiguous || RequiresManualReviewByEquivalenceVerdict(item.LlmEquivalenceVerdict));
    }

    private static string FormatStreamItemLocation(MatchLlmStreamItem item)
    {
        return item.TableIndex.HasValue
            ? $"表{item.TableIndex.Value + 1}/行{item.RowIndex + 1}"
            : $"行{item.RowIndex + 1}";
    }

    private async Task WriteCircuitOpenEventsAsync(
        HttpResponse response,
        MatchLlmStreamItem item,
        bool requiresReview,
        ConcurrentDictionary<LlmStreamItemKey, byte> reviewTerminalLookup,
        SemaphoreSlim sseWriteLock,
        CancellationToken cancellationToken)
    {
        const string message = "LLM 失败率过高，已触发熔断，请稍后重试";
        var itemKey = GetLlmStreamItemKey(item);
        if (requiresReview && reviewTerminalLookup.TryAdd(itemKey, 0))
        {
            await WriteSseEventLockedAsync(response, sseWriteLock, "review.error", new
            {
                tableIndex = item.TableIndex,
                rowIndex = item.RowIndex,
                message,
                decision = "manualReview"
            }, cancellationToken);
        }
    }

    private async Task<LlmStepExecutionResult> ExecuteLlmStepWithPolicyAsync(
        HttpResponse response,
        string stepName,
        MatchLlmStreamItem item,
        int timeoutSeconds,
        int retryCount,
        Func<CancellationToken, Task<LlmStepOutcome>> executeAsync,
        ConcurrentDictionary<LlmStreamItemKey, byte> reviewTerminalLookup,
        SemaphoreSlim sseWriteLock,
        CancellationToken requestCancellationToken)
    {
        var itemKey = GetLlmStreamItemKey(item);
        for (var attempt = 0; attempt <= retryCount; attempt++)
        {
            using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(requestCancellationToken);
            stepCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                var outcome = await executeAsync(stepCts.Token);
                if (outcome == LlmStepOutcome.Success || attempt >= retryCount)
                {
                    return new LlmStepExecutionResult(outcome, attempt);
                }

                _logger.LogDebug("[LLM-Stream] {Location}: {Step} 第 {Attempt} 次失败，准备重试",
                    FormatStreamItemLocation(item), stepName, attempt + 1);
            }
            catch (OperationCanceledException) when (requestCancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                if (attempt < retryCount)
                {
                    _logger.LogDebug("[LLM-Stream] {Location}: {Step} 第 {Attempt} 次超时，准备重试",
                        FormatStreamItemLocation(item), stepName, attempt + 1);
                    continue;
                }

                await WriteSseEventLockedAsync(response, sseWriteLock, $"{stepName}.error", new
                {
                    tableIndex = item.TableIndex,
                    rowIndex = item.RowIndex,
                    message = $"{GetLlmStepDisplayName(stepName)}超时（>{timeoutSeconds}s）",
                    decision = string.Equals(stepName, "review", StringComparison.OrdinalIgnoreCase)
                        ? "manualReview"
                        : null
                }, requestCancellationToken);
                reviewTerminalLookup.TryAdd(itemKey, 0);
                return new LlmStepExecutionResult(LlmStepOutcome.Timeout, attempt);
            }
            catch (LlmStepFailureException ex)
            {
                if (attempt < retryCount)
                {
                    _logger.LogWarning(ex, "[LLM-Stream] {Location}: {Step} 第 {Attempt} 次失败，准备重试",
                        FormatStreamItemLocation(item), stepName, attempt + 1);
                    continue;
                }

                _logger.LogWarning(ex, "[LLM-Stream] {Location}: {Step} 重试后仍失败",
                    FormatStreamItemLocation(item), stepName);
                await WriteSseEventLockedAsync(response, sseWriteLock, $"{stepName}.error", new
                {
                    tableIndex = item.TableIndex,
                    rowIndex = item.RowIndex,
                    message = ex.EventMessage,
                    decision = ex.Decision ?? (string.Equals(stepName, "review", StringComparison.OrdinalIgnoreCase)
                        ? "manualReview"
                        : null)
                }, requestCancellationToken);
                reviewTerminalLookup.TryAdd(itemKey, 0);
                return new LlmStepExecutionResult(LlmStepOutcome.Failed, attempt);
            }
            catch (Exception ex)
            {
                if (attempt < retryCount)
                {
                    _logger.LogWarning(ex, "[LLM-Stream] {Location}: {Step} 第 {Attempt} 次异常，准备重试",
                        FormatStreamItemLocation(item), stepName, attempt + 1);
                    continue;
                }

                _logger.LogWarning(ex, "[LLM-Stream] {Location}: {Step} 重试后仍失败",
                    FormatStreamItemLocation(item), stepName);
                await WriteSseEventLockedAsync(response, sseWriteLock, $"{stepName}.error", new
                {
                    tableIndex = item.TableIndex,
                    rowIndex = item.RowIndex,
                    message = $"{GetLlmStepDisplayName(stepName)}失败（已达到重试上限）",
                    decision = string.Equals(stepName, "review", StringComparison.OrdinalIgnoreCase)
                        ? "manualReview"
                        : null
                }, requestCancellationToken);
                reviewTerminalLookup.TryAdd(itemKey, 0);
                return new LlmStepExecutionResult(LlmStepOutcome.Failed, attempt);
            }
        }

        return new LlmStepExecutionResult(LlmStepOutcome.Failed, retryCount);
    }

    private static string GetLlmStepDisplayName(string stepName)
    {
        return string.Equals(stepName, "review", StringComparison.OrdinalIgnoreCase)
            ? "LLM复核"
            : stepName;
    }

    private async Task<DataScopeResult?> ResolveSpecScopeAsync(ClaimsPrincipal user)
    {
        return await SpecDataScopeHelper.ResolveScopeAsync(user, _authDataScopeService);
    }

    private async Task<Dictionary<int, AcceptanceSpec>> GetScopedSpecDictionaryAsync(
        IEnumerable<int> specIds,
        DataScopeResult scope)
    {
        var distinctIds = specIds
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (distinctIds.Count == 0)
        {
            return new Dictionary<int, AcceptanceSpec>();
        }

        var specs = await _unitOfWork.AcceptanceSpecs.FindAsync(s => distinctIds.Contains(s.Id));
        return SpecDataScopeHelper.ApplyScope(specs, scope)
            .ToDictionary(spec => spec.Id);
    }

    private static MatchLlmStreamItem CreateAuthoritativeLlmStreamItem(
        MatchLlmStreamItem requestItem,
        MatchResult? result)
    {
        if (result == null || !result.MatchedSpecId.HasValue)
        {
            return CreateNoMatchLlmStreamItem(requestItem);
        }

        return new MatchLlmStreamItem
        {
            TableIndex = requestItem.TableIndex,
            RowIndex = requestItem.RowIndex,
            SourceProject = requestItem.SourceProject,
            SourceSpecification = requestItem.SourceSpecification,
            BestMatchSpecId = result.MatchedSpecId,
            BestMatchScore = result.Score,
            ScoreDetails = result.ScoreDetails,
            Decision = ToDecisionKey(result.Decision),
            LlmEquivalenceVerdict = result.LlmEquivalence == null
                ? null
                : ToEquivalenceVerdictKey(result.LlmEquivalence.Verdict),
            IsAmbiguous = result.IsAmbiguous,
            EvidenceSummary = [.. result.Evidence.Summary],
            ConflictSummary = [.. result.Evidence.Conflicts]
        };
    }

    private static MatchLlmStreamItem CreateNoMatchLlmStreamItem(MatchLlmStreamItem item)
    {
        return new MatchLlmStreamItem
        {
            TableIndex = item.TableIndex,
            RowIndex = item.RowIndex,
            SourceProject = item.SourceProject,
            SourceSpecification = item.SourceSpecification,
            BestMatchSpecId = null,
            BestMatchScore = null,
            ScoreDetails = null,
            Decision = "manualReview",
            LlmEquivalenceVerdict = null,
            IsAmbiguous = false,
            EvidenceSummary = [],
            ConflictSummary = []
        };
    }

    private static string BuildReviewTrigger(MatchLlmStreamItem item)
    {
        if (RequiresManualReviewByEquivalenceVerdict(item.LlmEquivalenceVerdict))
        {
            return "AI 等价裁决已要求人工确认，禁止旧复核反向放行";
        }

        if (item.ConflictSummary?.Count > 0)
        {
            return "存在结构化冲突证据，需要结合 AI 复核确认";
        }

        if (!string.IsNullOrWhiteSpace(item.Decision) &&
            string.Equals(item.Decision, "manualReview", StringComparison.OrdinalIgnoreCase))
        {
            return "证据不足或候选接近，需要人工/LLM进一步复核";
        }

        return "需要补充复核结论";
    }

    private static async Task WriteSseEventAsync(HttpResponse response, string eventName, object data, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(data, SseJsonOptions);
        await response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// 安全写入 SSE 事件：连接已断开时静默忽略，不抛异常
    /// </summary>
    private static async Task WriteSseEventSafeAsync(HttpResponse response, string eventName, object data, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;
        try
        {
            await WriteSseEventAsync(response, eventName, data, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw; // 让调用方的 catch(OperationCanceledException) 处理
        }
        catch (ObjectDisposedException)
        {
            // Response 已释放，连接已断开
        }
    }

    /// <summary>
    /// 线程安全的 SSE 写入：用信号量串行化并发写入（Parallel.ForEachAsync 场景）
    /// </summary>
    private static async Task WriteSseEventLockedAsync(
        HttpResponse response,
        SemaphoreSlim sseWriteLock,
        string eventName,
        object data,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;
        await sseWriteLock.WaitAsync(cancellationToken);
        try
        {
            await WriteSseEventAsync(response, eventName, data, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (ObjectDisposedException) { /* Response 已释放 */ }
        finally
        {
            sseWriteLock.Release();
        }
    }

}

public readonly record struct MatchingOperationResult<T>(T Data, string Message);

public readonly record struct MatchingDownloadResult(byte[] Content, string ContentType, string FileName);

internal sealed class MatchingApiException : Exception
{
    public MatchingApiException(int code, string message, bool isNotFound = false)
        : base(message)
    {
        Code = code;
        IsNotFound = isNotFound;
    }

    public int Code { get; }

    public bool IsNotFound { get; }
}

/// <summary>
/// 填充任务结果
/// </summary>
internal class FillTaskResult
{
    public int PayloadVersion { get; set; } = 2;
    public string TaskId { get; set; } = string.Empty;
    public int SourceFileId { get; set; }
    public int SourceTableIndex { get; set; }
    public int? AcceptanceColumnIndex { get; set; }
    public int? RemarkColumnIndex { get; set; }
    public List<FillResult> FillResults { get; set; } = [];
    public string? FilledFilePath { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 是否为批量模式（多表格一次性填充）
    /// </summary>
    public bool IsBatchMode { get; set; }

    /// <summary>
    /// 批量模式下各表格的填充条目
    /// </summary>
    public List<TableFillEntry> TableEntries { get; set; } = [];

    /// <summary>
    /// 下载产物相对路径
    /// </summary>
    public string? DownloadArtifactRelativePath { get; set; }

    /// <summary>
    /// 下载产物文件名
    /// </summary>
    public string? DownloadArtifactFileName { get; set; }

    /// <summary>
    /// 下载产物内容类型
    /// </summary>
    public string? DownloadArtifactContentType { get; set; }
}

/// <summary>
/// 单个表格的填充条目（批量模式）
/// </summary>
internal class TableFillEntry
{
    public int TableIndex { get; set; }
    public int AcceptanceColumnIndex { get; set; }
    public int? RemarkColumnIndex { get; set; }
    public List<FillResult> FillResults { get; set; } = [];
}

/// <summary>
/// 单行填充结果
/// </summary>
internal class FillResult
{
    public int RowIndex { get; set; }
    public int SpecId { get; set; }
    public string Acceptance { get; set; } = string.Empty;
    public string? Remark { get; set; }
}

internal class GeneratedArtifactFile
{
    public int FileId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = [];
}

internal readonly record struct WriteBackSummary(int RequestedCells, int WrittenCells);

internal enum LlmStepOutcome
{
    Success = 0,
    Failed = 1,
    Timeout = 2
}

internal readonly record struct LlmStepExecutionResult(LlmStepOutcome Outcome, int RetriesUsed);
