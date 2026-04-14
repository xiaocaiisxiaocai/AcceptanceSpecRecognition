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
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 智能匹配共享协作组件。
/// </summary>
public sealed class MatchingWorkflowSupportService
{
    private const int MaxScopedCandidateCount = 2000;
    private const int EmbeddingGenerationBatchSize = 200;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IMatchingService _matchingService;
    private readonly DocumentFileAccessService _documentFileAccessService;
    private readonly DocumentTableAccessService _documentTableAccessService;
    private readonly MatchingResultWriteBackService _matchingResultWriteBackService;
    private readonly ITextPreprocessingPipeline _textPipeline;
    private readonly ILlmReviewService _llmReviewService;
    private readonly ILlmSuggestionService _llmSuggestionService;
    private readonly IAuthDataScopeService _authDataScopeService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IAiServiceSelector _aiServiceSelector;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MatchingTaskSnapshotService _matchingTaskSnapshotService;
    private readonly ExecutionHistoryAppService _executionHistoryAppService;
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
        ILlmReviewService llmReviewService,
        ILlmSuggestionService llmSuggestionService,
        IAuthDataScopeService authDataScopeService,
        IEmbeddingService embeddingService,
        IAiServiceSelector aiServiceSelector,
        IServiceScopeFactory scopeFactory,
        MatchingTaskSnapshotService matchingTaskSnapshotService,
        ExecutionHistoryAppService executionHistoryAppService,
        ILogger<MatchingWorkflowSupportService> logger)
    {
        _unitOfWork = unitOfWork;
        _matchingService = matchingService;
        _documentFileAccessService = documentFileAccessService;
        _documentTableAccessService = documentTableAccessService;
        _matchingResultWriteBackService = matchingResultWriteBackService;
        _textPipeline = textPipeline;
        _llmReviewService = llmReviewService;
        _llmSuggestionService = llmSuggestionService;
        _authDataScopeService = authDataScopeService;
        _embeddingService = embeddingService;
        _aiServiceSelector = aiServiceSelector;
        _scopeFactory = scopeFactory;
        _matchingTaskSnapshotService = matchingTaskSnapshotService;
        _executionHistoryAppService = executionHistoryAppService;
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

    internal async Task RunLlmStreamAsync(ClaimsPrincipal user, HttpResponse response, MatchLlmStreamRequest request, CancellationToken cancellationToken)
    {
        if (request.Items == null || request.Items.Count == 0)
        {
            throw Failure(400, "Items不能为空");
        }

        var scope = await ResolveSpecScopeAsync(user);
        if (scope == null)
        {
            throw Failure(401, "会话缺少用户上下文");
        }

        var config = await ConvertToMatchingConfigAsync(request.Config);
        var accessibleSpecLookup = await GetScopedSpecDictionaryAsync(
            request.Items.Select(item => item.BestMatchSpecId ?? 0),
            scope);
        var normalizedItems = request.Items
            .Select(item => NormalizeLlmStreamItem(item, accessibleSpecLookup.ContainsKey(item.BestMatchSpecId ?? 0)))
            .ToList();

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
        var reviewCount = normalizedItems.Count(item => config.UseLlmReview && item.BestMatchSpecId.HasValue);
        var suggestionCount = normalizedItems.Count(item => ShouldGenerateSuggestion(config, item));
        var reviewSuccess = 0;
        var reviewFailed = 0;
        var reviewTimeout = 0;
        var reviewRetries = 0;
        var suggestionSuccess = 0;
        var suggestionFailed = 0;
        var suggestionTimeout = 0;
        var suggestionRetries = 0;
        var totalFailures = 0;
        var circuitOpened = 0;

        _logger.LogInformation(
            "[LLM-Stream] 开始并行处理 {Count} 行 (review={ReviewCount}, suggestion={SuggestionCount}, maxParallelism={Parallelism}), useLlmReview={Review}, useLlmSuggestion={Suggestion}, suggestNoMatch={SuggestNoMatch}, suggestionThreshold={Threshold}, rowTimeoutSec={RowTimeoutSec}, retryCount={RetryCount}, circuitBreakFailures={CircuitBreakFailures}",
            normalizedItems.Count, reviewCount, suggestionCount, parallelism,
            config.UseLlmReview, config.UseLlmSuggestion, config.SuggestNoMatchRows, config.LlmSuggestionScoreThreshold,
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
                    using var scope = _scopeFactory.CreateScope();
                    var reviewService = scope.ServiceProvider.GetRequiredService<ILlmReviewService>();
                    var suggestionService = scope.ServiceProvider.GetRequiredService<ILlmSuggestionService>();
                    var location = FormatStreamItemLocation(item);

                    if (Volatile.Read(ref circuitOpened) == 1)
                    {
                        await WriteCircuitOpenEventsAsync(response, item, config, sseWriteLock, ct);
                        return;
                    }

                    // 同一行内：先复核，再生成建议（顺序执行）
                    if (config.UseLlmReview && item.BestMatchSpecId.HasValue)
                    {
                        _logger.LogDebug("[LLM-Stream] {Location}: 开始复核 (specId={SpecId}, score={Score:P1})",
                            location, item.BestMatchSpecId, item.BestMatchScore ?? 0);

                        var reviewResult = await ExecuteLlmStepWithPolicyAsync(
                            response,
                            "review",
                            item,
                            rowTimeoutSeconds,
                            retryCount,
                            token => StreamLlmReviewAsync(response, item, config, token, accessibleSpecLookup, reviewService, sseWriteLock),
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
                                if (Interlocked.Increment(ref totalFailures) >= circuitBreakFailures)
                                {
                                    Interlocked.Exchange(ref circuitOpened, 1);
                                }
                                break;
                            default:
                                Interlocked.Increment(ref reviewFailed);
                                if (Interlocked.Increment(ref totalFailures) >= circuitBreakFailures)
                                {
                                    Interlocked.Exchange(ref circuitOpened, 1);
                                }
                                break;
                        }
                    }

                    if (ct.IsCancellationRequested) return;
                    if (Volatile.Read(ref circuitOpened) == 1)
                    {
                        await WriteCircuitOpenEventsAsync(response, item, config, sseWriteLock, ct);
                        return;
                    }

                    if (ShouldGenerateSuggestion(config, item))
                    {
                        _logger.LogDebug("[LLM-Stream] {Location}: 开始生成建议 (specId={SpecId}, score={Score}, threshold={Threshold}, suggestNoMatch={SuggestNoMatch})",
                            location, item.BestMatchSpecId, item.BestMatchScore?.ToString("P1") ?? "无匹配",
                            config.LlmSuggestionScoreThreshold, config.SuggestNoMatchRows);

                        var suggestionResult = await ExecuteLlmStepWithPolicyAsync(
                            response,
                            "suggestion",
                            item,
                            rowTimeoutSeconds,
                            retryCount,
                            token => StreamLlmSuggestionAsync(response, item, config, token, accessibleSpecLookup, suggestionService, sseWriteLock),
                            sseWriteLock,
                            ct);

                        Interlocked.Add(ref suggestionRetries, suggestionResult.RetriesUsed);
                        switch (suggestionResult.Outcome)
                        {
                            case LlmStepOutcome.Success:
                                Interlocked.Increment(ref suggestionSuccess);
                                break;
                            case LlmStepOutcome.Timeout:
                                Interlocked.Increment(ref suggestionTimeout);
                                if (Interlocked.Increment(ref totalFailures) >= circuitBreakFailures)
                                {
                                    Interlocked.Exchange(ref circuitOpened, 1);
                                }
                                break;
                            default:
                                Interlocked.Increment(ref suggestionFailed);
                                if (Interlocked.Increment(ref totalFailures) >= circuitBreakFailures)
                                {
                                    Interlocked.Exchange(ref circuitOpened, 1);
                                }
                                break;
                        }
                    }
                    else if (config.UseLlmSuggestion)
                    {
                        _logger.LogDebug("[LLM-Stream] {Location}: 跳过建议 (specId={SpecId}, score={Score}, threshold={Threshold}, suggestNoMatch={SuggestNoMatch})",
                            location, item.BestMatchSpecId, item.BestMatchScore?.ToString("P1") ?? "无匹配",
                            config.LlmSuggestionScoreThreshold, config.SuggestNoMatchRows);
                    }
                });
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("LLM 流式输出：客户端已断开连接");
        }
        finally
        {
            sseWriteLock.Dispose();
        }

        _logger.LogInformation(
            "[LLM-Stream] 全部完成, 耗时 {Elapsed}ms, review(success={ReviewSuccess}, failed={ReviewFailed}, timeout={ReviewTimeout}, retries={ReviewRetries}), suggestion(success={SuggestionSuccess}, failed={SuggestionFailed}, timeout={SuggestionTimeout}, retries={SuggestionRetries}), totalFailures={TotalFailures}, circuitOpened={CircuitOpened}",
            sw.ElapsedMilliseconds,
            reviewSuccess, reviewFailed, reviewTimeout, reviewRetries,
            suggestionSuccess, suggestionFailed, suggestionTimeout, suggestionRetries,
            totalFailures, circuitOpened == 1);
    }

    internal async Task<MatchingOperationResult<ExecuteFillResponse>> ExecuteFillCoreAsync(ClaimsPrincipal user, ExecuteFillRequest request)
    {
        if (request.Mappings == null || request.Mappings.Count == 0)
        {
            throw Failure(400, "填充映射不能为空");
        }

        var fileId = request.FileId ?? request.SourceFileId;
        var tableIndex = request.TableIndex ?? request.SourceTableIndex;

        if (!fileId.HasValue)
        {
            throw Failure(400, "源文件ID不能为空");
        }

        if (!tableIndex.HasValue)
        {
            throw Failure(400, "源表格索引不能为空");
        }

        // 获取源文件
        var scope = await ResolveSpecScopeAsync(user);
        if (scope == null)
        {
            throw Failure(401, "会话缺少用户上下文");
        }

        var wordFile = await _documentFileAccessService.GetAccessibleWordFileAsync(fileId.Value, scope);
        if (wordFile == null)
        {
            throw Failure(400, "源文件不存在");
        }
        var highConfidenceThreshold = NormalizeHighConfidenceThreshold(request.HighConfidenceThreshold);

        // 获取所有相关的验收规格
        var hasLlmSuggestions = request.Mappings.Any(m => m.UseLlmSuggestion);
        if (hasLlmSuggestions)
        {
            throw Failure(400, "已停用 LLM 生成建议写回，请仅使用匹配结果");
        }

        var specIds = request.Mappings
            .Where(m => !m.UseLlmSuggestion)
            .Select(m => m.SpecId ?? m.SelectedSpecId)
            .Where(id => id.HasValue && id.Value > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (specIds.Count == 0)
        {
            throw Failure(400, "未提供有效的验收规格ID");
        }

        var specDict = await GetScopedSpecDictionaryAsync(specIds, scope);

        TableData tableData;
        try
        {
            tableData = await _documentTableAccessService.ExtractTableDataAsync(
                wordFile,
                tableIndex.Value,
                new ColumnMapping
                {
                    HeaderRowIndex = 0,
                    DataStartRowIndex = 1
                });
        }
        catch (ApplicationServiceException ex)
        {
            throw Failure(ex.Code, ex.Message);
        }

        // 列索引必须由用户手动指定（不做关键字推断）
        if (!request.AcceptanceColumnIndex.HasValue)
        {
            throw Failure(400, "请手动指定验收列索引");
        }
        var acceptanceColumnIndex = request.AcceptanceColumnIndex.Value;
        var remarkColumnIndex = request.RemarkColumnIndex;

        // 执行填充
        int filledCount = 0;
        int skippedCount = 0;
        var fillResults = new List<FillResult>();

        foreach (var fillMapping in request.Mappings)
        {
            var selectedSpecId = (fillMapping.SpecId ?? fillMapping.SelectedSpecId) ?? 0;
            if (selectedSpecId <= 0 || !specDict.TryGetValue(selectedSpecId, out var spec))
            {
                skippedCount++;
                continue;
            }

            if (!CanApplyMatchedSpec(fillMapping, highConfidenceThreshold))
            {
                skippedCount++;
                continue;
            }

            // 记录填充信息
            fillResults.Add(new FillResult
            {
                RowIndex = fillMapping.RowIndex,
                SpecId = spec.Id,
                Acceptance = spec.Acceptance ?? "",
                Remark = spec.Remark
            });
            filledCount++;
        }

        // 生成任务ID
        var taskId = Guid.NewGuid().ToString("N");
        var taskResult = new FillTaskResult
        {
            TaskId = taskId,
            SourceFileId = fileId.Value,
            SourceTableIndex = tableIndex.Value,
            AcceptanceColumnIndex = acceptanceColumnIndex,
            RemarkColumnIndex = remarkColumnIndex,
            FillResults = fillResults,
            CreatedAt = DateTime.UtcNow
        };

        taskResult.StrictReuseSession = await TryBuildStrictReuseSessionAsync(
            wordFile,
            [
                new StrictReuseSourceTableDefinition
                {
                    TableIndex = tableIndex.Value,
                    ProjectColumnIndex = request.ProjectColumnIndex,
                    SpecificationColumnIndex = request.SpecificationColumnIndex,
                    AcceptanceColumnIndex = acceptanceColumnIndex,
                    RemarkColumnIndex = remarkColumnIndex,
                    HeaderRowStart = request.HeaderRowStart,
                    HeaderRowCount = request.HeaderRowCount,
                    DataStartRow = request.DataStartRow,
                    FilterEmptySourceRows = request.FilterEmptySourceRows,
                    FillResults = fillResults
                }
            ],
            taskResult.CreatedAt);

        var isExcelSource = wordFile.FileType == UploadedFileType.ExcelXlsx;
        if (isExcelSource)
        {
            try
            {
                var writeBackSummary = await _matchingResultWriteBackService.ApplyFillResultToSourceFileAsync(wordFile, taskResult);
                if (writeBackSummary.RequestedCells > 0 && writeBackSummary.WrittenCells == 0)
                {
                    throw Failure(400, "未写入任何单元格，请检查列索引和行配置是否正确");
                }

                if (writeBackSummary.WrittenCells < writeBackSummary.RequestedCells)
                {
                    _logger.LogWarning(
                        "Excel回写存在部分未命中: task={TaskId}, requested={Requested}, written={Written}",
                        taskId, writeBackSummary.RequestedCells, writeBackSummary.WrittenCells);
                }
                await _unitOfWork.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行填充后写回 Excel 失败: 文件{FileId}", wordFile.Id);
                throw Failure(500, $"写回 Excel 失败: {ex.Message}");
            }
        }

        await _matchingTaskSnapshotService.SaveAsync(user, taskResult);
        await SaveExecutionHistoryAsync(
            user,
            wordFile,
            taskId,
            taskResult.CreatedAt,
            [
                new BatchTableFillMapping
                {
                    TableIndex = tableIndex.Value,
                    AcceptanceColumnIndex = acceptanceColumnIndex,
                    RemarkColumnIndex = remarkColumnIndex,
                    ProjectColumnIndex = request.ProjectColumnIndex,
                    SpecificationColumnIndex = request.SpecificationColumnIndex,
                    HeaderRowStart = request.HeaderRowStart,
                    HeaderRowCount = request.HeaderRowCount,
                    DataStartRow = request.DataStartRow,
                    FilterEmptySourceRows = request.FilterEmptySourceRows,
                    Mappings = request.Mappings
                }
            ],
            specDict,
            highConfidenceThreshold);

        var response = new ExecuteFillResponse
        {
            TaskId = taskId,
            FilledCount = filledCount,
            SkippedCount = skippedCount,
            DownloadUrl = isExcelSource ? string.Empty : $"/api/matching/download/{taskId}"
        };

        _logger.LogInformation(
            "执行填充完成: 任务{TaskId}, 文件类型{FileType}, 填充{Filled}行, 跳过{Skipped}行",
            taskId, wordFile.FileType, filledCount, skippedCount);

        return Result(response, isExcelSource
            ? $"填充完成：已填充{filledCount}行，跳过{skippedCount}行，已写回并可下载 Excel"
            : $"填充完成：已填充{filledCount}行，跳过{skippedCount}行");
    }

    internal async Task<MatchingOperationResult<ExecuteFillResponse>> BatchExecuteFillCoreAsync(ClaimsPrincipal user, BatchExecuteFillRequest request)
    {
        if (request.Tables == null || request.Tables.Count == 0)
        {
            throw Failure(400, "请至少提供一个表格的填充映射");
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
        var highConfidenceThreshold = NormalizeHighConfidenceThreshold(request.HighConfidenceThreshold);

        if (request.Tables.SelectMany(t => t.Mappings).Any(m => m.UseLlmSuggestion))
        {
            throw Failure(400, "已停用 LLM 生成建议写回，请仅使用匹配结果");
        }

        // 收集所有 specId 一次查 DB
        var allSpecIds = request.Tables
            .SelectMany(t => t.Mappings)
            .Where(m => !m.UseLlmSuggestion)
            .Select(m => m.SpecId ?? m.SelectedSpecId)
            .Where(id => id.HasValue && id.Value > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var specDict = await GetScopedSpecDictionaryAsync(allSpecIds, scope);

        // 遍历每个表格生成 TableFillEntry
        int totalFilled = 0, totalSkipped = 0;
        var tableEntries = new List<TableFillEntry>();

        foreach (var tableFill in request.Tables)
        {
            var entry = new TableFillEntry
            {
                TableIndex = tableFill.TableIndex,
                AcceptanceColumnIndex = tableFill.AcceptanceColumnIndex,
                RemarkColumnIndex = tableFill.RemarkColumnIndex
            };

            foreach (var mapping in tableFill.Mappings)
            {
                var selectedSpecId = (mapping.SpecId ?? mapping.SelectedSpecId) ?? 0;
                if (selectedSpecId <= 0 || !specDict.TryGetValue(selectedSpecId, out var spec))
                {
                    totalSkipped++;
                }
                else
                {
                    if (!CanApplyMatchedSpec(mapping, highConfidenceThreshold))
                    {
                        totalSkipped++;
                        continue;
                    }

                    entry.FillResults.Add(new FillResult
                    {
                        RowIndex = mapping.RowIndex,
                        SpecId = spec.Id,
                        Acceptance = spec.Acceptance ?? "",
                        Remark = spec.Remark
                    });
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

        taskResult.StrictReuseSession = await TryBuildStrictReuseSessionAsync(
            wordFile,
            request.Tables.Select(table => new StrictReuseSourceTableDefinition
            {
                TableIndex = table.TableIndex,
                ProjectColumnIndex = table.ProjectColumnIndex,
                SpecificationColumnIndex = table.SpecificationColumnIndex,
                AcceptanceColumnIndex = table.AcceptanceColumnIndex,
                RemarkColumnIndex = table.RemarkColumnIndex,
                HeaderRowStart = table.HeaderRowStart,
                HeaderRowCount = table.HeaderRowCount,
                DataStartRow = table.DataStartRow,
                FilterEmptySourceRows = table.FilterEmptySourceRows,
                FillResults = tableEntries
                    .FirstOrDefault(entry => entry.TableIndex == table.TableIndex)?.FillResults ?? []
            }),
            taskResult.CreatedAt);

        var isExcelSource = wordFile.FileType == UploadedFileType.ExcelXlsx;
        if (isExcelSource)
        {
            try
            {
                var writeBackSummary = await _matchingResultWriteBackService.ApplyFillResultToSourceFileAsync(wordFile, taskResult);
                if (writeBackSummary.RequestedCells > 0 && writeBackSummary.WrittenCells == 0)
                {
                    throw Failure(400, "未写入任何单元格，请检查列索引和行配置是否正确");
                }

                if (writeBackSummary.WrittenCells < writeBackSummary.RequestedCells)
                {
                    _logger.LogWarning(
                        "Excel批量回写存在部分未命中: task={TaskId}, requested={Requested}, written={Written}",
                        taskId, writeBackSummary.RequestedCells, writeBackSummary.WrittenCells);
                }
                await _unitOfWork.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量填充后写回 Excel 失败: 文件{FileId}", wordFile.Id);
                throw Failure(500, $"写回 Excel 失败: {ex.Message}");
            }
        }

        await _matchingTaskSnapshotService.SaveAsync(user, taskResult);
        await SaveExecutionHistoryAsync(
            user,
            wordFile,
            taskId,
            taskResult.CreatedAt,
            request.Tables,
            specDict,
            highConfidenceThreshold);

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

    private async Task<StrictReuseSession?> TryBuildStrictReuseSessionAsync(
        WordFile sourceFile,
        IEnumerable<StrictReuseSourceTableDefinition> sourceTables,
        DateTime createdAt)
    {
        var normalizedTables = sourceTables?
            .Where(table =>
                table.FillResults.Count > 0 &&
                table.ProjectColumnIndex.HasValue &&
                table.SpecificationColumnIndex.HasValue)
            .GroupBy(table => table.TableIndex)
            .Select(group => group.First())
            .OrderBy(table => table.TableIndex)
            .ToList() ?? [];

        if (normalizedTables.Count == 0)
        {
            return null;
        }

        var snapshots = new List<StrictReuseTableSnapshot>();
        foreach (var table in normalizedTables)
        {
            var sourceRows = await _documentTableAccessService.ExtractMatchSourceItemsAsync(
                sourceFile,
                table.TableIndex,
                table.ProjectColumnIndex!.Value,
                table.SpecificationColumnIndex!.Value,
                table.HeaderRowStart,
                table.HeaderRowCount,
                table.DataStartRow,
                table.FilterEmptySourceRows ?? true);

            if (sourceRows.Count == 0)
            {
                continue;
            }

            snapshots.Add(new StrictReuseTableSnapshot
            {
                TableIndex = table.TableIndex,
                ProjectColumnIndex = table.ProjectColumnIndex.Value,
                SpecificationColumnIndex = table.SpecificationColumnIndex.Value,
                AcceptanceColumnIndex = table.AcceptanceColumnIndex,
                RemarkColumnIndex = table.RemarkColumnIndex,
                HeaderRowStart = table.HeaderRowStart,
                HeaderRowCount = table.HeaderRowCount,
                DataStartRow = table.DataStartRow,
                FilterEmptySourceRows = table.FilterEmptySourceRows ?? true,
                RowSignatures = sourceRows
                    .Select(row => new StrictReuseRowSignature
                    {
                        RowIndex = row.RowIndex,
                        Project = row.Project,
                        Specification = row.Specification
                    })
                    .ToList(),
                FillResults = CloneFillResults(table.FillResults)
            });
        }

        if (snapshots.Count == 0)
        {
            return null;
        }

        return new StrictReuseSession
        {
            SourceFileId = sourceFile.Id,
            SourceFileName = sourceFile.FileName,
            SourceFileType = sourceFile.FileType,
            CreatedAt = createdAt,
            Tables = snapshots
        };
    }

    private static List<FillResult> CloneFillResults(IEnumerable<FillResult> fillResults)
    {
        return fillResults
            .Select(fill => new FillResult
            {
                RowIndex = fill.RowIndex,
                SpecId = fill.SpecId,
                Acceptance = fill.Acceptance,
                Remark = fill.Remark
            })
            .ToList();
    }

    private async Task SaveExecutionHistoryAsync(
        ClaimsPrincipal user,
        WordFile wordFile,
        string taskId,
        DateTime createdAt,
        IReadOnlyCollection<BatchTableFillMapping> tables,
        IReadOnlyDictionary<int, AcceptanceSpec> specDict,
        double highConfidenceThreshold)
    {
        var tableMetas = await _documentTableAccessService.GetTablesAsync(wordFile);
        var tableMetaLookup = tableMetas.ToDictionary(table => table.Index);
        var fileDetail = new ExecutionHistoryFileDto
        {
            FileName = wordFile.FileName,
            FileType = wordFile.FileType
        };

        foreach (var table in tables.OrderBy(item => item.TableIndex))
        {
            var rows = await BuildExecutionHistoryRowsAsync(wordFile, table, specDict, highConfidenceThreshold);
            var sheetName = tableMetaLookup.TryGetValue(table.TableIndex, out var meta) && !string.IsNullOrWhiteSpace(meta.Name)
                ? meta.Name!
                : $"表格 {table.TableIndex + 1}";

            fileDetail.Sheets.Add(new ExecutionHistorySheetDto
            {
                SheetIndex = table.TableIndex,
                SheetName = sheetName,
                Rows = rows
            });
        }

        await _executionHistoryAppService.SaveAsync(user, new ExecutionHistoryDraft
        {
            TaskId = taskId,
            TaskType = ExecutionHistoryTaskTypes.SmartFill,
            SourceFileId = wordFile.Id,
            SourceFileName = wordFile.FileName,
            SourceFileType = wordFile.FileType,
            CreatedAt = createdAt,
            Files = [fileDetail]
        });
    }

    private async Task<List<ExecutionHistoryRowDto>> BuildExecutionHistoryRowsAsync(
        WordFile wordFile,
        BatchTableFillMapping table,
        IReadOnlyDictionary<int, AcceptanceSpec> specDict,
        double highConfidenceThreshold)
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
                    highConfidenceThreshold,
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
                highConfidenceThreshold,
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
        double highConfidenceThreshold,
        int acceptanceColumnIndex,
        int? remarkColumnIndex)
    {
        var selectedSpecId = (mapping?.SpecId ?? mapping?.SelectedSpecId) ?? 0;
        AcceptanceSpec? matchedSpec = null;
        var hasSpec = selectedSpecId > 0 && specDict.TryGetValue(selectedSpecId, out matchedSpec);
        var confidencePercent = Math.Round(Math.Max(mapping?.MatchScore ?? 0, 0) * 100, 1);

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

        var status = CanApplyMatchedSpec(mapping, highConfidenceThreshold)
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
            Acceptance = matchedSpec.Acceptance,
            Remark = matchedSpec.Remark,
            ConfidencePercent = confidencePercent,
            Status = status,
            IsManualSelected = mapping.ManualConfirmed,
            AcceptanceColumnIndex = acceptanceColumnIndex,
            RemarkColumnIndex = remarkColumnIndex
        };
    }

    /// <summary>
    /// 获取候选验收规格列表（含 EmbeddingCache 复用）
    /// </summary>
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
        await HydrateCandidateEmbeddingsAsync(candidates, embeddingServiceId);

        return candidates;
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "匹配候选生成 Embedding 失败，将使用空向量继续匹配");
            return;
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
        return $"{NormalizeForDedup(project)}\u001f{NormalizeForDedup(specification)}";
    }

    private static string NormalizeForDedup(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(" ", value
            .Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
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
        var (defaultStrategy, defaultRecallTopK) = await ResolveMatchingDefaultsAsync(dto?.EmbeddingServiceId);

        var strategy = dto?.MatchingStrategy is { } configuredStrategy && Enum.IsDefined(configuredStrategy)
            ? configuredStrategy
            : defaultStrategy;

        if (dto?.UseLlmEntityResolution == true && strategy != MatchingStrategy.MultiStage)
        {
            throw Failure(400, "LLM 实体判别仅支持多阶段匹配策略，请切换为多阶段后再启用");
        }

        return new MatchingConfig
        {
            MatchingStrategy = strategy,
            EmbeddingServiceId = dto?.EmbeddingServiceId,
            LlmServiceId = dto?.LlmServiceId,
            MinScoreThreshold = dto?.MinScoreThreshold ?? fallbackConfig.MinScoreThreshold,
            HighConfidenceThreshold = NormalizeHighConfidenceThreshold(dto?.HighConfidenceThreshold ?? fallbackConfig.HighConfidenceThreshold),
            RecallTopK = Math.Clamp(dto?.RecallTopK ?? defaultRecallTopK, 1, MatchingThresholds.MaxRecallTopK),
            AmbiguityMargin = Math.Clamp(dto?.AmbiguityMargin ?? fallbackConfig.AmbiguityMargin, 0, 1),
            UseLlmEntityResolution = dto?.UseLlmEntityResolution ?? fallbackConfig.UseLlmEntityResolution,
            LlmEntityResolutionTopCandidates = Math.Clamp(
                dto?.LlmEntityResolutionTopCandidates ?? fallbackConfig.LlmEntityResolutionTopCandidates,
                1,
                MatchingThresholds.MaxLlmEntityResolutionTopCandidates),
            LlmEntityPositiveConfidenceThreshold = Math.Clamp(dto?.LlmEntityPositiveConfidenceThreshold ?? fallbackConfig.LlmEntityPositiveConfidenceThreshold, 0, 1),
            LlmEntityConflictReviewConfidenceThreshold = Math.Clamp(dto?.LlmEntityConflictReviewConfidenceThreshold ?? fallbackConfig.LlmEntityConflictReviewConfidenceThreshold, 0, 1),
            LlmEntityConflictRejectConfidenceThreshold = Math.Clamp(dto?.LlmEntityConflictRejectConfidenceThreshold ?? fallbackConfig.LlmEntityConflictRejectConfidenceThreshold, 0, 1),
            UseLlmReview = dto?.UseLlmReview ?? fallbackConfig.UseLlmReview,
            UseLlmSuggestion = dto?.UseLlmSuggestion ?? fallbackConfig.UseLlmSuggestion,
            SuggestNoMatchRows = dto?.SuggestNoMatchRows ?? fallbackConfig.SuggestNoMatchRows,
            LlmSuggestionScoreThreshold = dto?.LlmSuggestionScoreThreshold ?? fallbackConfig.LlmSuggestionScoreThreshold,
            LlmParallelism = Math.Clamp(dto?.LlmParallelism ?? fallbackConfig.LlmParallelism, 1, 10),
            LlmRowTimeoutSeconds = Math.Clamp(dto?.LlmRowTimeoutSeconds ?? fallbackConfig.LlmRowTimeoutSeconds, 5, 300),
            LlmRetryCount = Math.Clamp(dto?.LlmRetryCount ?? fallbackConfig.LlmRetryCount, 0, 3),
            LlmCircuitBreakFailures = Math.Clamp(dto?.LlmCircuitBreakFailures ?? fallbackConfig.LlmCircuitBreakFailures, 3, 200),
            FilterEmptySourceRows = dto?.FilterEmptySourceRows ?? fallbackConfig.FilterEmptySourceRows
        };
    }

    private async Task<(MatchingStrategy Strategy, int RecallTopK)> ResolveMatchingDefaultsAsync(int? embeddingServiceId)
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

        return embeddingService == null
            ? (fallbackConfig.MatchingStrategy, fallbackConfig.RecallTopK)
            : (embeddingService.DefaultMatchingStrategy switch
            {
                AiServiceDefaultMatchingStrategy.MultiStage => MatchingStrategy.MultiStage,
                _ => MatchingStrategy.SingleStage
            }, embeddingService.DefaultRecallTopK);
    }

    private static bool CanApplyMatchedSpec(FillMapping mapping, double highConfidenceThreshold)
    {
        if (mapping.ManualConfirmed)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(mapping.Decision))
        {
            if (string.Equals(mapping.Decision, "autoApply", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(mapping.Decision, "manualReview", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mapping.Decision, "reject", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!mapping.MatchScore.HasValue)
        {
            return false;
        }

        var matchScore = mapping.MatchScore.Value;
        if (matchScore >= highConfidenceThreshold)
        {
            return true;
        }

        if (matchScore < MatchingThresholds.MediumConfidenceScore)
        {
            return false;
        }

        return NormalizeLlmReviewScore(mapping.LlmReviewScore) >= MatchingThresholds.LlmReviewPassScore;
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
            HasHardConflict = result.Evidence.HasHardConflict,
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
                    Decision = candidate.Evidence.HasHardConflict
                        ? "reject"
                        : result.MatchedSpecId == candidate.SpecId
                            ? ToDecisionKey(result.Decision)
                            : "manualReview",
                    HasHardConflict = candidate.Evidence.HasHardConflict,
                    EvidenceSummary = [.. candidate.Evidence.Summary],
                    ConflictSummary = [.. candidate.Evidence.Conflicts],
                    Issues = candidate.Issues.Select(ConvertToIssueDto).ToList(),
                    Entities = candidate.Evidence.Entities.Select(ConvertToEntityDto).ToList(),
                    RerankSummary = candidate.RerankSummary,
                    LlmEquivalence = ConvertToLlmEquivalenceDto(candidate.LlmEquivalence)
                })
                .ToList(),
            MatchingStrategy = result.MatchingStrategy,
            RecalledCandidateCount = result.RecalledCandidateCount,
            IsAmbiguous = result.IsAmbiguous,
            ScoreGap = result.ScoreGap,
            RerankSummary = result.RerankSummary,
            LlmScore = result.LlmScore,
            LlmReason = result.LlmReason,
            LlmCommentary = result.LlmCommentary,
            IsLlmReviewed = result.IsLlmReviewed
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
        CancellationToken cancellationToken,
        IReadOnlyDictionary<int, AcceptanceSpec> accessibleSpecLookup,
        ILlmReviewService reviewService,
        SemaphoreSlim sseWriteLock)
    {
        var specId = item.BestMatchSpecId ?? 0;
        if (specId <= 0)
            return LlmStepOutcome.Failed;

        var location = FormatStreamItemLocation(item);

        if (!accessibleSpecLookup.TryGetValue(specId, out var spec))
        {
            _logger.LogWarning("[LLM复核] {Location}: 最佳匹配规格ID={SpecId}不存在或无权限", location, specId);
            await WriteSseEventLockedAsync(response, sseWriteLock, "review.error", new
            {
                tableIndex = item.TableIndex,
                rowIndex = item.RowIndex,
                message = "最佳匹配规格不存在或无权限",
                decision = "manualReview"
            }, cancellationToken);
            return LlmStepOutcome.Failed;
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
            HasHardConflict = item.HasHardConflict,
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
                var passed = normalizedScore >= MatchingThresholds.LlmReviewPassScore && !item.HasHardConflict;
                _logger.LogDebug("[LLM复核] {Location}: 完成, score={Score}, reason={Reason}",
                    location, normalizedScore, result.Reason);
                await WriteSseEventLockedAsync(response, sseWriteLock, "review.done", new
                {
                    tableIndex = item.TableIndex,
                    rowIndex = item.RowIndex,
                    score = normalizedScore,
                    reason = result.Reason,
                    commentary = result.Commentary,
                    decision = passed ? "autoApply" : "manualReview"
                }, cancellationToken);
                return LlmStepOutcome.Success;
            }
            else
            {
                _logger.LogWarning("[LLM复核] {Location}: JSON解析失败, 原始输出: {Raw}", location, buffer.ToString());
                await WriteSseEventLockedAsync(response, sseWriteLock, "review.error", new
                {
                    tableIndex = item.TableIndex,
                    rowIndex = item.RowIndex,
                    message = "LLM复核输出解析失败",
                    decision = "manualReview"
                }, cancellationToken);
                return LlmStepOutcome.Failed;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AiServiceUnavailableException ex)
        {
            _logger.LogWarning(ex, "LLM复核失败");
            await WriteSseEventLockedAsync(response, sseWriteLock, "review.error", new
            {
                tableIndex = item.TableIndex,
                rowIndex = item.RowIndex,
                message = ex.Reason,
                decision = "manualReview"
            }, cancellationToken);
            return LlmStepOutcome.Failed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM复核失败");
            await WriteSseEventLockedAsync(response, sseWriteLock, "review.error", new
            {
                tableIndex = item.TableIndex,
                rowIndex = item.RowIndex,
                message = "LLM复核失败",
                decision = "manualReview"
            }, cancellationToken);
            return LlmStepOutcome.Failed;
        }
    }

    private async Task<LlmStepOutcome> StreamLlmSuggestionAsync(
        HttpResponse response,
        MatchLlmStreamItem item,
        MatchingConfig config,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<int, AcceptanceSpec> accessibleSpecLookup,
        ILlmSuggestionService suggestionService,
        SemaphoreSlim sseWriteLock)
    {
        var request = new LlmSuggestionRequest
        {
            SourceProject = item.SourceProject,
            SourceSpecification = item.SourceSpecification,
            LlmServiceId = config.LlmServiceId
        };
        var location = FormatStreamItemLocation(item);

        // 如果有最佳匹配（虽然得分低于阈值），包含为参考数据
        if (item.BestMatchSpecId.HasValue && item.BestMatchSpecId.Value > 0)
        {
            if (accessibleSpecLookup.TryGetValue(item.BestMatchSpecId.Value, out var spec))
            {
                request.BestMatchProject = spec.Project;
                request.BestMatchSpecification = spec.Specification;
                request.BestMatchAcceptance = spec.Acceptance;
                request.BestMatchRemark = spec.Remark;
                request.BestMatchScore = item.BestMatchScore;
            }
        }

        _logger.LogDebug(
            "[LLM建议] {Location}: 源=[{SrcProj}/{SrcSpec}] 参考=[{RefProj}] 得分={Score}",
            location, item.SourceProject, item.SourceSpecification,
            request.BestMatchProject ?? "(无)", item.BestMatchScore?.ToString("P1") ?? "N/A");

        await WriteSseEventLockedAsync(response, sseWriteLock, "suggestion.start", new
        {
            tableIndex = item.TableIndex,
            rowIndex = item.RowIndex
        }, cancellationToken);

        var buffer = new StringBuilder();
        try
        {
            await foreach (var chunk in suggestionService.GenerateSuggestionStreamAsync(request, cancellationToken))
            {
                buffer.Append(chunk);
                await WriteSseEventLockedAsync(response, sseWriteLock, "suggestion.delta", new
                {
                    tableIndex = item.TableIndex,
                    rowIndex = item.RowIndex,
                    chunk
                }, cancellationToken);
            }

            if (suggestionService.TryParseSuggestionResult(buffer.ToString(), out var result))
            {
                _logger.LogDebug("[LLM建议] {Location}: 完成, acceptance={Acceptance}, remark={Remark}",
                    location, result.Acceptance ?? "(空)", result.Remark ?? "(空)");
                await WriteSseEventLockedAsync(response, sseWriteLock, "suggestion.done", new
                {
                    tableIndex = item.TableIndex,
                    rowIndex = item.RowIndex,
                    acceptance = result.Acceptance,
                    remark = result.Remark,
                    reason = result.Reason
                }, cancellationToken);
                return LlmStepOutcome.Success;
            }
            else
            {
                _logger.LogWarning("[LLM建议] {Location}: JSON解析失败, 原始输出: {Raw}", location, buffer.ToString());
                await WriteSseEventLockedAsync(response, sseWriteLock, "suggestion.error", new
                {
                    tableIndex = item.TableIndex,
                    rowIndex = item.RowIndex,
                    message = "LLM生成输出解析失败"
                }, cancellationToken);
                return LlmStepOutcome.Failed;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AiServiceUnavailableException ex)
        {
            _logger.LogWarning(ex, "LLM生成建议失败");
            await WriteSseEventLockedAsync(response, sseWriteLock, "suggestion.error", new
            {
                tableIndex = item.TableIndex,
                rowIndex = item.RowIndex,
                message = ex.Reason
            }, cancellationToken);
            return LlmStepOutcome.Failed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM生成建议失败");
            await WriteSseEventLockedAsync(response, sseWriteLock, "suggestion.error", new
            {
                tableIndex = item.TableIndex,
                rowIndex = item.RowIndex,
                message = "LLM生成建议失败"
            }, cancellationToken);
            return LlmStepOutcome.Failed;
        }
    }

    private static bool ShouldGenerateSuggestion(MatchingConfig config, MatchLlmStreamItem item)
    {
        if (!config.UseLlmSuggestion)
        {
            return false;
        }

        if (item.BestMatchSpecId.HasValue)
        {
            return (item.BestMatchScore ?? 0) < config.LlmSuggestionScoreThreshold;
        }

        return config.SuggestNoMatchRows;
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
        MatchingConfig config,
        SemaphoreSlim sseWriteLock,
        CancellationToken cancellationToken)
    {
        const string message = "LLM 失败率过高，已触发熔断，请稍后重试";
        if (config.UseLlmReview && item.BestMatchSpecId.HasValue)
        {
            await WriteSseEventLockedAsync(response, sseWriteLock, "review.error", new
            {
                tableIndex = item.TableIndex,
                rowIndex = item.RowIndex,
                message,
                decision = "manualReview"
            }, cancellationToken);
        }

        if (ShouldGenerateSuggestion(config, item))
        {
            await WriteSseEventLockedAsync(response, sseWriteLock, "suggestion.error", new
            {
                tableIndex = item.TableIndex,
                rowIndex = item.RowIndex,
                message
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
        SemaphoreSlim sseWriteLock,
        CancellationToken requestCancellationToken)
    {
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
                return new LlmStepExecutionResult(LlmStepOutcome.Timeout, attempt);
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
                return new LlmStepExecutionResult(LlmStepOutcome.Failed, attempt);
            }
        }

        return new LlmStepExecutionResult(LlmStepOutcome.Failed, retryCount);
    }

    private static string GetLlmStepDisplayName(string stepName)
    {
        return string.Equals(stepName, "review", StringComparison.OrdinalIgnoreCase)
            ? "LLM复核"
            : "LLM建议";
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

    private static MatchLlmStreamItem NormalizeLlmStreamItem(
        MatchLlmStreamItem item,
        bool hasAccessibleBestMatch)
    {
        if (hasAccessibleBestMatch)
        {
            return item;
        }

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
            HasHardConflict = item.HasHardConflict,
            EvidenceSummary = item.EvidenceSummary,
            ConflictSummary = item.ConflictSummary
        };
    }

    private static string BuildReviewTrigger(MatchLlmStreamItem item)
    {
        if (item.HasHardConflict)
        {
            return "存在硬冲突，默认不允许自动采用，仅记录复核上下文";
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
    /// 当前填充任务的严格复用会话
    /// </summary>
    public StrictReuseSession? StrictReuseSession { get; set; }

    /// <summary>
    /// 批量严格复用产物相对路径
    /// </summary>
    public string? DownloadArtifactRelativePath { get; set; }

    /// <summary>
    /// 批量严格复用下载文件名
    /// </summary>
    public string? DownloadArtifactFileName { get; set; }

    /// <summary>
    /// 批量严格复用下载内容类型
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

internal class StrictReuseSession
{
    public int SourceFileId { get; set; }
    public string SourceFileName { get; set; } = string.Empty;
    public UploadedFileType SourceFileType { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<StrictReuseTableSnapshot> Tables { get; set; } = [];
}

internal class StrictReuseTableSnapshot
{
    public int TableIndex { get; set; }
    public int ProjectColumnIndex { get; set; }
    public int SpecificationColumnIndex { get; set; }
    public int AcceptanceColumnIndex { get; set; }
    public int? RemarkColumnIndex { get; set; }
    public int? HeaderRowStart { get; set; }
    public int? HeaderRowCount { get; set; }
    public int? DataStartRow { get; set; }
    public bool FilterEmptySourceRows { get; set; } = true;
    public List<StrictReuseRowSignature> RowSignatures { get; set; } = [];
    public List<FillResult> FillResults { get; set; } = [];
}

internal class StrictReuseRowSignature
{
    public int RowIndex { get; set; }
    public string Project { get; set; } = string.Empty;
    public string Specification { get; set; } = string.Empty;
}

internal class StrictReuseSourceTableDefinition
{
    public int TableIndex { get; set; }
    public int? ProjectColumnIndex { get; set; }
    public int? SpecificationColumnIndex { get; set; }
    public int AcceptanceColumnIndex { get; set; }
    public int? RemarkColumnIndex { get; set; }
    public int? HeaderRowStart { get; set; }
    public int? HeaderRowCount { get; set; }
    public int? DataStartRow { get; set; }
    public bool? FilterEmptySourceRows { get; set; }
    public List<FillResult> FillResults { get; set; } = [];
}

internal class StrictReuseGeneratedFile
{
    public int FileId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = [];
}

internal class SavedDownloadArtifact
{
    public string RelativePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
}

internal readonly record struct WriteBackSummary(int RequestedCells, int WrittenCells);

internal enum LlmStepOutcome
{
    Success = 0,
    Failed = 1,
    Timeout = 2
}

internal readonly record struct LlmStepExecutionResult(LlmStepOutcome Outcome, int RetriesUsed);
