using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.Services;
using CoreAiServicePurpose = AcceptanceSpecSystem.Core.AI.Models.AiServicePurpose;
using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Interfaces;
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
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 智能匹配共享工作流服务
/// </summary>
public class MatchingWorkflowService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMatchingService _matchingService;
    private readonly DocumentServiceFactory _documentServiceFactory;
    private readonly IFileStorageService _fileStorage;
    private readonly ITextPreprocessingPipeline _textPipeline;
    private readonly ILlmReviewService _llmReviewService;
    private readonly ILlmSuggestionService _llmSuggestionService;
    private readonly IAuthDataScopeService _authDataScopeService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IAiServiceSelector _aiServiceSelector;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MatchingWorkflowService> _logger;

    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly JsonSerializerOptions FillTaskJsonOptions = new(JsonSerializerDefaults.Web);
    private const int FillTaskRetentionHours = 24;
    private const int CurrentFillTaskPayloadVersion = 2;

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
    /// 创建匹配工作流服务实例
    /// </summary>
    public MatchingWorkflowService(
        IUnitOfWork unitOfWork,
        IMatchingService matchingService,
        DocumentServiceFactory documentServiceFactory,
        IFileStorageService fileStorage,
        ITextPreprocessingPipeline textPipeline,
        ILlmReviewService llmReviewService,
        ILlmSuggestionService llmSuggestionService,
        IAuthDataScopeService authDataScopeService,
        IEmbeddingService embeddingService,
        IAiServiceSelector aiServiceSelector,
        IServiceScopeFactory scopeFactory,
        ILogger<MatchingWorkflowService> logger)
    {
        _unitOfWork = unitOfWork;
        _matchingService = matchingService;
        _documentServiceFactory = documentServiceFactory;
        _fileStorage = fileStorage;
        _textPipeline = textPipeline;
        _llmReviewService = llmReviewService;
        _llmSuggestionService = llmSuggestionService;
        _authDataScopeService = authDataScopeService;
        _embeddingService = embeddingService;
        _aiServiceSelector = aiServiceSelector;
        _scopeFactory = scopeFactory;
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

    public async Task<MatchingOperationResult<MatchPreviewResponse>> PreviewAsync(ClaimsPrincipal user, MatchPreviewRequest request)
    {
        var sw = Stopwatch.StartNew();
        var config = ConvertToMatchingConfig(request.Config);
        var highConfidenceThreshold = NormalizeHighConfidenceThreshold(config.HighConfidenceThreshold);
        var scope = await ResolveSpecScopeAsync(user);
        if (scope == null)
        {
            throw Failure(401, "会话缺少用户上下文");
        }

        // 兼容前端：如果未传Items，则尝试从文件表格提取项目/规格作为待匹配项
        if (request.Items == null || request.Items.Count == 0)
        {
            if (request.FileId.HasValue && request.TableIndex.HasValue)
            {
                if (!request.ProjectColumnIndex.HasValue || !request.SpecificationColumnIndex.HasValue)
                {
                    throw Failure(400, "请手动指定项目列与规格列索引");
                }

                var extracted = await ExtractMatchSourceItemsFromFileAsync(
                    request.FileId.Value,
                    request.TableIndex.Value,
                    request.ProjectColumnIndex.Value,
                    request.SpecificationColumnIndex.Value,
                    scope,
                    request.HeaderRowStart,
                    request.HeaderRowCount,
                    request.DataStartRow,
                    config.FilterEmptySourceRows);

                if (extracted.Count == 0)
                {
                    throw Failure(400, "未从表格中提取到可匹配的项目/规格数据");
                }

                request.Items = extracted;
            }
            else
            {
                throw Failure(400, "待匹配文本列表不能为空");
            }
        }

        // 获取候选验收规格（含 EmbeddingCache 复用）
        var candidates = await GetCandidatesAsync(
            request.CustomerId,
            request.ProcessId,
            request.MachineModelId,
            scope,
            config.EmbeddingServiceId);
        if (candidates.Count == 0)
        {
            var emptyItems = new List<MatchPreviewItem>();
            foreach (var item in request.Items)
            {
                emptyItems.Add(new MatchPreviewItem
                {
                    RowIndex = item.RowIndex,
                    SourceProject = item.Project,
                    SourceSpecification = item.Specification,
                    BestMatch = null,
                    LlmSuggestion = null,
                    NoMatchReason = "范围内无候选数据"
                });
            }

            return Result(new MatchPreviewResponse
            {
                Items = emptyItems,
                TotalMatched = 0,
                HighConfidenceCount = 0,
                MediumConfidenceCount = 0,
                LowConfidenceCount = 0,
                AmbiguousCount = 0
            }, "没有找到可匹配的验收规格");
        }

        // 创建文本处理会话，仅执行最小安全归一化。
        var tpSession = await _textPipeline.CreateSessionAsync();

        // 预处理候选项（项目/规格），确保 CombinedText 使用处理后的内容
        var processedCandidates = candidates.Select(c => new MatchCandidate
        {
            SpecId = c.SpecId,
            Project = tpSession.Process(c.Project),
            Specification = tpSession.Process(c.Specification),
            Acceptance = c.Acceptance,
            Remark = c.Remark,
            Embedding = c.Embedding
        }).ToList();

        // 批量构建预处理后的源项，一次性调用 BatchMatchAsync
        var sourceItems = request.Items.Select(item => new MatchSource
        {
            Project = tpSession.Process(item.Project),
            Specification = tpSession.Process(item.Specification)
        }).ToList();

        var previewItems = new List<MatchPreviewItem>();
        int highCount = 0, mediumCount = 0, lowCount = 0;

        BatchMatchResult batchResult;
        try
        {
            batchResult = await _matchingService.BatchMatchAsync(sourceItems, processedCandidates, config);
        }
        catch (AiServiceUnavailableException ex)
        {
            throw Failure(400, $"Embedding 服务不可用: {ex.Reason}");
        }

        for (var idx = 0; idx < request.Items.Count; idx++)
        {
            var item = request.Items[idx];
            MatchResult? bestMatch = null;
            string? noMatchReason = null;

            if (idx < batchResult.Results.Count)
            {
                var mr = batchResult.Results[idx];
                if (mr.MatchedSpecId.HasValue)
                    bestMatch = mr;
                else
                    noMatchReason = processedCandidates.Count == 0 ? "范围内无候选数据" : "最佳得分低于阈值";
            }

            var previewItem = new MatchPreviewItem
            {
                RowIndex = item.RowIndex,
                SourceProject = item.Project,
                SourceSpecification = item.Specification,
                BestMatch = bestMatch != null ? ConvertToMatchResultDto(bestMatch) : null,
                LlmSuggestion = null,
                NoMatchReason = noMatchReason,
                ConfidenceLevel = GetConfidenceLevel(bestMatch, highConfidenceThreshold)
            };

            previewItems.Add(previewItem);

            if (previewItem.BestMatch != null)
            {
                switch (previewItem.ConfidenceLevel)
                {
                    case "high":
                        highCount++;
                        break;
                    case "medium":
                        mediumCount++;
                        break;
                    case "low":
                        lowCount++;
                        break;
                }
            }
        }

        var response = new MatchPreviewResponse
        {
            Items = previewItems,
            TotalMatched = previewItems.Count(i => i.HasMatch),
            HighConfidenceCount = highCount,
            MediumConfidenceCount = mediumCount,
            LowConfidenceCount = lowCount,
            AmbiguousCount = previewItems.Count(i => i.BestMatch?.IsAmbiguous == true)
        };

        sw.Stop();
        _logger.LogInformation(
            "匹配预览完成: 共{Total}项, 匹配{Matched}项, 高{High}/中{Medium}/低{Low}, 歧义{Ambiguous}, 耗时{Elapsed}ms",
            request.Items.Count, response.TotalMatched, highCount, mediumCount, lowCount, response.AmbiguousCount, sw.ElapsedMilliseconds);

        return Result(response);
    }

    public async Task LlmStreamAsync(ClaimsPrincipal user, HttpResponse response, MatchLlmStreamRequest request, CancellationToken cancellationToken)
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

        var config = ConvertToMatchingConfig(request.Config);
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

    public async Task<MatchingOperationResult<ExecuteFillResponse>> ExecuteFillAsync(ClaimsPrincipal user, ExecuteFillRequest request)
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

        var wordFile = await GetAccessibleWordFileAsync(fileId.Value, scope);
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

        // 获取文档解析器
        var parser = _documentServiceFactory.GetParser(GetDocumentType(wordFile.FileType));
        if (parser == null)
        {
            throw Failure(500, "文档解析器不可用");
        }

        // 提取表格数据
        TableData tableData;
        using (var stream = OpenWordFileReadStream(wordFile))
        {
            try
            {
                var mapping = new ColumnMapping
                {
                    HeaderRowIndex = 0,
                    DataStartRowIndex = 1
                };
                tableData = await parser.ExtractTableDataAsync(stream, tableIndex.Value, mapping);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw Failure(400, "表格索引超出范围");
            }
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
            var writer = _documentServiceFactory.GetWriter(DocumentType.Excel);
            if (writer == null)
            {
                throw Failure(500, "Excel 文档写入器不可用");
            }

            try
            {
                var writeBackSummary = await ApplyFillResultToSourceFileAsync(wordFile, taskResult, writer);
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

        await SaveFillTaskSnapshotAsync(user, taskResult);

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

    public async Task<MatchingDownloadResult> DownloadAsync(ClaimsPrincipal user, string taskId)
    {
        var taskResult = await LoadFillTaskSnapshotAsync(user, taskId);
        if (taskResult == null)
        {
            throw NotFoundFailure("任务不存在或已过期");
        }

        if (!string.IsNullOrWhiteSpace(taskResult.DownloadArtifactRelativePath))
        {
            try
            {
                var fullPath = _fileStorage.GetAbsolutePath(taskResult.DownloadArtifactRelativePath);
                if (!System.IO.File.Exists(fullPath))
                {
                    throw NotFoundFailure("下载文件不存在或已被清理");
                }

                var content = await System.IO.File.ReadAllBytesAsync(fullPath);
                var artifactContentType = string.IsNullOrWhiteSpace(taskResult.DownloadArtifactContentType)
                    ? "application/octet-stream"
                    : taskResult.DownloadArtifactContentType;
                var artifactDownloadFileName = string.IsNullOrWhiteSpace(taskResult.DownloadArtifactFileName)
                    ? Path.GetFileName(fullPath)
                    : taskResult.DownloadArtifactFileName;

                _logger.LogInformation("下载填充结果产物: 任务{TaskId}, 文件{FileName}", taskId, artifactDownloadFileName);
                return new MatchingDownloadResult(content, artifactContentType, artifactDownloadFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "下载填充结果产物失败: {TaskId}", taskId);
                throw Failure(500, $"下载结果失败: {ex.Message}");
            }
        }

        // 获取源文件
        var wordFile = await _unitOfWork.WordFiles.GetByIdAsync(taskResult.SourceFileId);
        if (wordFile == null)
        {
            throw NotFoundFailure("源文件不存在");
        }

        // 获取文档写入器
        var writer = _documentServiceFactory.GetWriter(GetDocumentType(wordFile.FileType));
        if (writer == null)
        {
            throw Failure(500, "文档写入器不可用");
        }

        // 构建写入操作列表
        byte[] resultContent;
        using (var resultStream = new MemoryStream())
        {
            // 复制原文件到可写流（优先文件系统，缺失时回退DB二进制）
            await using (var sourceStream = OpenWordFileReadStream(wordFile))
            {
                await sourceStream.CopyToAsync(resultStream);
            }
            resultStream.Position = 0;

            try
            {
                if (taskResult.IsBatchMode)
                {
                    // 批量模式：多表格一次性写入
                    var tableOperations = new Dictionary<int, List<CellWriteOperation>>();

                    foreach (var entry in taskResult.TableEntries)
                    {
                        var ops = new List<CellWriteOperation>();
                        foreach (var r in entry.FillResults)
                        {
                            ops.Add(new CellWriteOperation
                            {
                                RowIndex = r.RowIndex,
                                ColumnIndex = entry.AcceptanceColumnIndex,
                                Value = r.Acceptance,
                                PreserveFormatting = true
                            });

                            if (entry.RemarkColumnIndex.HasValue && !string.IsNullOrWhiteSpace(r.Remark))
                            {
                                if (entry.RemarkColumnIndex.Value != entry.AcceptanceColumnIndex)
                                {
                                    ops.Add(new CellWriteOperation
                                    {
                                        RowIndex = r.RowIndex,
                                        ColumnIndex = entry.RemarkColumnIndex.Value,
                                        Value = r.Remark!,
                                        PreserveFormatting = true
                                    });
                                }
                            }
                        }
                        tableOperations[entry.TableIndex] = ops;
                    }

                    await writer.WriteMultipleTablesAsync(resultStream, tableOperations);
                }
                else
                {
                    // 单表模式（原有逻辑）
                    var operations = new List<CellWriteOperation>();
                    foreach (var r in taskResult.FillResults)
                    {
                        operations.Add(new CellWriteOperation
                        {
                            RowIndex = r.RowIndex,
                            ColumnIndex = taskResult.AcceptanceColumnIndex ?? 0,
                            Value = r.Acceptance,
                            PreserveFormatting = true
                        });

                        if (taskResult.RemarkColumnIndex.HasValue && !string.IsNullOrWhiteSpace(r.Remark))
                        {
                            if (taskResult.RemarkColumnIndex.Value != (taskResult.AcceptanceColumnIndex ?? 0))
                            {
                                operations.Add(new CellWriteOperation
                                {
                                    RowIndex = r.RowIndex,
                                    ColumnIndex = taskResult.RemarkColumnIndex.Value,
                                    Value = r.Remark!,
                                    PreserveFormatting = true
                                });
                            }
                        }
                    }
                    await writer.WriteTableDataAsync(resultStream, taskResult.SourceTableIndex, operations);
                }

                resultContent = resultStream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "填充文档失败: {TaskId}", taskId);
                throw Failure(500, $"填充文档失败: {ex.Message}");
            }
        }

        // 下载后清理源文件（不再持久化存储）
        try
        {
            await _fileStorage.DeleteIfExistsAsync(wordFile.FilePath);
            wordFile.FilePath = null;
            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "填充下载后清理源文件失败: {TaskId}", taskId);
        }

        var fileExtension = GetDownloadFileExtension(wordFile.FileType);
        var contentType = GetDownloadContentType(wordFile.FileType);
        var downloadFileName = Path.GetFileName(wordFile.FileName);
        if (string.IsNullOrWhiteSpace(downloadFileName))
        {
            downloadFileName = $"filled{fileExtension}";
        }

        _logger.LogInformation("下载填充结果: 任务{TaskId}, 文件{FileName}", taskId, downloadFileName);

        return new MatchingDownloadResult(resultContent, contentType, downloadFileName);
    }

    public async Task<MatchingOperationResult<BatchPreviewResponse>> BatchPreviewAsync(ClaimsPrincipal user, BatchPreviewRequest request)
    {
        var sw = Stopwatch.StartNew();

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

        var scope = await ResolveSpecScopeAsync(user);
        if (scope == null)
        {
            throw Failure(401, "会话缺少用户上下文");
        }

        var config = ConvertToMatchingConfig(request.Config);

        // 一次获取候选集（含 EmbeddingCache 复用）
        var candidates = await GetCandidatesAsync(request.CustomerId, request.ProcessId, request.MachineModelId, scope, config.EmbeddingServiceId);
        var highConfidenceThreshold = NormalizeHighConfidenceThreshold(config.HighConfidenceThreshold);

        // 创建文本处理会话
        var tpSession = await _textPipeline.CreateSessionAsync();

        // 预处理候选项
        var processedCandidates = candidates.Select(c => new MatchCandidate
        {
            SpecId = c.SpecId,
            Project = tpSession.Process(c.Project),
            Specification = tpSession.Process(c.Specification),
            Acceptance = c.Acceptance,
            Remark = c.Remark,
            Embedding = c.Embedding
        }).ToList();

        var response = new BatchPreviewResponse();

        // Phase 1: 提取所有表格的源数据并预处理
        var allTableData = new List<(BatchTableConfig Config, List<MatchSourceItem> Items, List<MatchSource> Sources)>();
        foreach (var tableConfig in request.Tables)
        {
            var extracted = await ExtractMatchSourceItemsFromFileAsync(
                request.FileId,
                tableConfig.TableIndex,
                tableConfig.ProjectColumnIndex,
                tableConfig.SpecificationColumnIndex,
                scope,
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
        }

        // Phase 2: 合并所有表格的源项，对 BatchMatchAsync 只调用一次
        var allSources = allTableData.SelectMany(t => t.Sources).ToList();

        if (processedCandidates.Count == 0)
        {
            throw Failure(400, "范围内无候选数据");
        }

        BatchMatchResult batchResult;
        if (allSources.Count > 0)
        {
            try
            {
                batchResult = await _matchingService.BatchMatchAsync(allSources, processedCandidates, config);
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

        // Phase 3: 按表格分发匹配结果
        var resultOffset = 0;
        foreach (var (tableConfig, extracted, sources) in allTableData)
        {
            var tableResult = new BatchTablePreviewResult { TableIndex = tableConfig.TableIndex };
            int highCount = 0, mediumCount = 0, lowCount = 0;

            for (var idx = 0; idx < extracted.Count; idx++)
            {
                var item = extracted[idx];
                MatchResult? bestMatch = null;
                string? noMatchReason = null;

                if ((resultOffset + idx) < batchResult.Results.Count)
                {
                    var mr = batchResult.Results[resultOffset + idx];
                    if (mr.MatchedSpecId.HasValue)
                        bestMatch = mr;
                    else
                        noMatchReason = "最佳得分低于阈值";
                }

                var previewItem = new MatchPreviewItem
                {
                    RowIndex = item.RowIndex,
                    SourceProject = item.Project,
                    SourceSpecification = item.Specification,
                    BestMatch = bestMatch != null ? ConvertToMatchResultDto(bestMatch) : null,
                    LlmSuggestion = null,
                    NoMatchReason = noMatchReason,
                    ConfidenceLevel = GetConfidenceLevel(bestMatch, highConfidenceThreshold)
                };

                tableResult.Items.Add(previewItem);

                if (previewItem.BestMatch != null)
                {
                    switch (previewItem.ConfidenceLevel)
                    {
                        case "high":
                            highCount++;
                            break;
                        case "medium":
                            mediumCount++;
                            break;
                        case "low":
                            lowCount++;
                            break;
                    }
                }
            }

            resultOffset += extracted.Count;

            tableResult.TotalMatched = tableResult.Items.Count(i => i.HasMatch);
            tableResult.HighConfidenceCount = highCount;
            tableResult.MediumConfidenceCount = mediumCount;
            tableResult.LowConfidenceCount = lowCount;
            tableResult.AmbiguousCount = tableResult.Items.Count(i => i.BestMatch?.IsAmbiguous == true);

            response.Tables.Add(tableResult);
        }

        sw.Stop();
        _logger.LogInformation(
            "批量匹配预览完成: {TableCount}个表格, 总匹配{Total}, 高{High}/中{Medium}/低{Low}, 歧义{Ambiguous}, 耗时{Elapsed}ms",
            request.Tables.Count, response.TotalMatched,
            response.HighConfidenceCount, response.MediumConfidenceCount, response.LowConfidenceCount, response.AmbiguousCount,
            sw.ElapsedMilliseconds);

        return Result(response);
    }

    public async Task<MatchingOperationResult<ExecuteFillResponse>> BatchExecuteFillAsync(ClaimsPrincipal user, BatchExecuteFillRequest request)
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

        var wordFile = await GetAccessibleWordFileAsync(request.FileId, scope);
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
            var writer = _documentServiceFactory.GetWriter(DocumentType.Excel);
            if (writer == null)
            {
                throw Failure(500, "Excel 文档写入器不可用");
            }

            try
            {
                var writeBackSummary = await ApplyFillResultToSourceFileAsync(wordFile, taskResult, writer);
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

        await SaveFillTaskSnapshotAsync(user, taskResult);

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

    public async Task<MatchingOperationResult<StrictReusePreviewResponse>> PreviewStrictReuseAsync(ClaimsPrincipal user, StrictReusePreviewRequest request)
    {
        if (request.TargetFileIds == null || request.TargetFileIds.Count == 0)
        {
            throw Failure(400, "请至少提供一个目标文件");
        }

        var sourceTask = await LoadFillTaskSnapshotAsync(user, request.SourceTaskId);
        if (sourceTask?.StrictReuseSession == null || sourceTask.StrictReuseSession.Tables.Count == 0)
        {
            throw Failure(400, "当前填充任务不支持严格复用，请重新执行一次填充后再试");
        }

        var scope = await ResolveSpecScopeAsync(user);
        if (scope == null)
        {
            throw Failure(401, "会话缺少用户上下文");
        }

        var results = new List<StrictReusePreviewFileResult>();
        foreach (var fileId in request.TargetFileIds.Distinct())
        {
            var targetFile = await GetAccessibleWordFileAsync(fileId, scope);
            if (targetFile == null)
            {
                results.Add(new StrictReusePreviewFileResult
                {
                    FileId = fileId,
                    FileName = $"文件{fileId}",
                    CanApply = false,
                    Errors = ["目标文件不存在"]
                });
                continue;
            }

            var errors = await ValidateStrictReuseTargetFileAsync(targetFile, sourceTask.StrictReuseSession, scope);
            results.Add(new StrictReusePreviewFileResult
            {
                FileId = targetFile.Id,
                FileName = targetFile.FileName,
                CanApply = errors.Count == 0,
                Errors = errors
            });
        }

        return Result(new StrictReusePreviewResponse
        {
            SourceTaskId = sourceTask.TaskId,
            SourceFileName = sourceTask.StrictReuseSession.SourceFileName,
            SourceFileType = sourceTask.StrictReuseSession.SourceFileType,
            Files = results
        });
    }

    public async Task<MatchingOperationResult<StrictReuseExecuteResponse>> ExecuteStrictReuseAsync(ClaimsPrincipal user, StrictReuseExecuteRequest request)
    {
        if (request.TargetFileIds == null || request.TargetFileIds.Count == 0)
        {
            throw Failure(400, "请至少提供一个目标文件");
        }

        var sourceTask = await LoadFillTaskSnapshotAsync(user, request.SourceTaskId);
        if (sourceTask?.StrictReuseSession == null || sourceTask.StrictReuseSession.Tables.Count == 0)
        {
            throw Failure(400, "当前填充任务不支持严格复用，请重新执行一次填充后再试");
        }

        var scope = await ResolveSpecScopeAsync(user);
        if (scope == null)
        {
            throw Failure(401, "会话缺少用户上下文");
        }

        var executableTargets = new List<StrictReuseGeneratedFile>();
        var fileResults = new List<StrictReuseExecuteFileResult>();

        foreach (var fileId in request.TargetFileIds.Distinct())
        {
            var targetFile = await GetAccessibleWordFileAsync(fileId, scope);
            if (targetFile == null)
            {
                fileResults.Add(new StrictReuseExecuteFileResult
                {
                    FileId = fileId,
                    FileName = $"文件{fileId}",
                    Success = false,
                    Message = "目标文件不存在"
                });
                continue;
            }

            var errors = await ValidateStrictReuseTargetFileAsync(targetFile, sourceTask.StrictReuseSession, scope);
            if (errors.Count > 0)
            {
                fileResults.Add(new StrictReuseExecuteFileResult
                {
                    FileId = targetFile.Id,
                    FileName = targetFile.FileName,
                    Success = false,
                    Message = string.Join("；", errors)
                });
                continue;
            }

            try
            {
                var generated = await GenerateStrictReuseTargetFileAsync(targetFile, sourceTask.StrictReuseSession);
                executableTargets.Add(generated);
                fileResults.Add(new StrictReuseExecuteFileResult
                {
                    FileId = targetFile.Id,
                    FileName = targetFile.FileName,
                    Success = true,
                    Message = "复用成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "严格复用执行失败: sourceTask={SourceTaskId}, targetFile={FileId}", request.SourceTaskId, targetFile.Id);
                fileResults.Add(new StrictReuseExecuteFileResult
                {
                    FileId = targetFile.Id,
                    FileName = targetFile.FileName,
                    Success = false,
                    Message = $"复用失败: {ex.Message}"
                });
            }
        }

        if (executableTargets.Count == 0)
        {
            throw Failure(400, "没有可执行严格复用的目标文件");
        }

        var artifact = await SaveStrictReuseArtifactAsync(sourceTask.StrictReuseSession, executableTargets);
        var taskId = Guid.NewGuid().ToString("N");
        var taskResult = new FillTaskResult
        {
            TaskId = taskId,
            SourceFileId = sourceTask.SourceFileId,
            CreatedAt = DateTime.UtcNow,
            DownloadArtifactRelativePath = artifact.RelativePath,
            DownloadArtifactFileName = artifact.FileName,
            DownloadArtifactContentType = artifact.ContentType
        };

        await SaveFillTaskSnapshotAsync(user, taskResult);

        var response = new StrictReuseExecuteResponse
        {
            TaskId = taskId,
            SuccessCount = fileResults.Count(item => item.Success),
            FailedCount = fileResults.Count(item => !item.Success),
            DownloadUrl = $"/api/matching/download/{taskId}",
            DownloadFileName = artifact.FileName,
            Files = fileResults
        };

        return Result(response, response.FailedCount > 0
            ? $"严格复用完成：成功{response.SuccessCount}份，失败{response.FailedCount}份"
            : $"严格复用完成：成功{response.SuccessCount}份");
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
            var sourceRows = await ExtractMatchSourceItemsFromFileAsync(
                sourceFile.Id,
                table.TableIndex,
                table.ProjectColumnIndex!.Value,
                table.SpecificationColumnIndex!.Value,
                null,
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

    private async Task<List<string>> ValidateStrictReuseTargetFileAsync(WordFile targetFile, StrictReuseSession session, DataScopeResult scope)
    {
        var errors = new List<string>();
        if (targetFile.FileType != session.SourceFileType)
        {
            errors.Add("文件类型不一致");
            return errors;
        }

        var parser = _documentServiceFactory.GetParser(GetDocumentType(targetFile.FileType));
        if (parser == null)
        {
            errors.Add("目标文件解析器不可用");
            return errors;
        }

        IReadOnlyList<TableInfo> targetTables;
        try
        {
            using var metaStream = OpenWordFileReadStream(targetFile);
            targetTables = await parser.GetTablesAsync(metaStream);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "严格复用预检读取目标文件失败: fileId={FileId}", targetFile.Id);
            errors.Add($"读取目标文件失败: {ex.Message}");
            return errors;
        }

        foreach (var sourceTable in session.Tables.OrderBy(table => table.TableIndex))
        {
            if (sourceTable.TableIndex < 0 || sourceTable.TableIndex >= targetTables.Count)
            {
                errors.Add($"表格{sourceTable.TableIndex + 1}不存在");
                continue;
            }

            var targetTable = targetTables[sourceTable.TableIndex];
            var requiredMaxColumnIndex = new[]
            {
                sourceTable.ProjectColumnIndex,
                sourceTable.SpecificationColumnIndex,
                sourceTable.AcceptanceColumnIndex,
                sourceTable.RemarkColumnIndex ?? -1
            }.Max();

            if (requiredMaxColumnIndex >= targetTable.ColumnCount)
            {
                errors.Add($"表格{sourceTable.TableIndex + 1}列配置超出目标文件范围");
                continue;
            }

            var targetRows = await ExtractMatchSourceItemsFromFileAsync(
                targetFile.Id,
                sourceTable.TableIndex,
                sourceTable.ProjectColumnIndex,
                sourceTable.SpecificationColumnIndex,
                scope,
                sourceTable.HeaderRowStart,
                sourceTable.HeaderRowCount,
                sourceTable.DataStartRow,
                sourceTable.FilterEmptySourceRows);

            if (targetRows.Count != sourceTable.RowSignatures.Count)
            {
                errors.Add($"表格{sourceTable.TableIndex + 1}的数据区行数不一致");
                continue;
            }

            for (var index = 0; index < sourceTable.RowSignatures.Count; index++)
            {
                var expected = sourceTable.RowSignatures[index];
                var actual = targetRows[index];
                if (actual.RowIndex != expected.RowIndex ||
                    !StrictReuseTextEquals(actual.Project, expected.Project) ||
                    !StrictReuseTextEquals(actual.Specification, expected.Specification))
                {
                    errors.Add($"表格{sourceTable.TableIndex + 1}第{index + 1}行的项目/规格顺序不一致");
                    break;
                }
            }
        }

        return errors;
    }

    private async Task<StrictReuseGeneratedFile> GenerateStrictReuseTargetFileAsync(WordFile targetFile, StrictReuseSession session)
    {
        var writer = _documentServiceFactory.GetWriter(GetDocumentType(targetFile.FileType));
        if (writer == null)
        {
            throw new InvalidOperationException("文档写入器不可用");
        }

        using var resultStream = new MemoryStream();
        await using (var sourceStream = OpenWordFileReadStream(targetFile))
        {
            await sourceStream.CopyToAsync(resultStream);
        }
        resultStream.Position = 0;

        var tableOperations = session.Tables
            .Select(table => new
            {
                table.TableIndex,
                Operations = BuildCellWriteOperations(
                    table.FillResults,
                    table.AcceptanceColumnIndex,
                    table.RemarkColumnIndex)
            })
            .Where(item => item.Operations.Count > 0)
            .ToDictionary(item => item.TableIndex, item => item.Operations);

        if (tableOperations.Count == 0)
        {
            throw new InvalidOperationException("来源填充结果为空，无法执行严格复用");
        }

        var requestedCells = tableOperations.Sum(item => item.Value.Count);
        var writtenCells = await writer.WriteMultipleTablesAsync(resultStream, tableOperations);
        if (writtenCells != requestedCells)
        {
            throw new InvalidOperationException($"目标文件写回不完整，期望写入{requestedCells}个单元格，实际写入{writtenCells}个");
        }

        return new StrictReuseGeneratedFile
        {
            FileId = targetFile.Id,
            FileName = targetFile.FileName,
            ContentType = GetDownloadContentType(targetFile.FileType),
            Content = resultStream.ToArray()
        };
    }

    private async Task<SavedDownloadArtifact> SaveStrictReuseArtifactAsync(
        StrictReuseSession session,
        List<StrictReuseGeneratedFile> generatedFiles)
    {
        if (generatedFiles.Count == 0)
        {
            throw new InvalidOperationException("没有可保存的严格复用结果");
        }

        if (generatedFiles.Count == 1)
        {
            var file = generatedFiles[0];
            var relativePath = await _fileStorage.SaveFilledWordAsync(file.FileName, file.Content);
            return new SavedDownloadArtifact
            {
                RelativePath = relativePath,
                FileName = file.FileName,
                ContentType = file.ContentType
            };
        }

        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var usedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in generatedFiles.OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase))
            {
                var entryName = BuildUniqueArchiveEntryName(file.FileName, usedEntryNames);
                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(file.Content);
            }
        }

        var baseName = Path.GetFileNameWithoutExtension(session.SourceFileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "严格复用结果";
        }

        var downloadFileName = $"{baseName}_严格复用结果.zip";
        var relativePathForZip = await _fileStorage.SaveFilledWordAsync(downloadFileName, zipStream.ToArray());
        return new SavedDownloadArtifact
        {
            RelativePath = relativePathForZip,
            FileName = downloadFileName,
            ContentType = "application/zip"
        };
    }

    private static string BuildUniqueArchiveEntryName(string fileName, HashSet<string> usedEntryNames)
    {
        var normalizedFileName = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "filled.docx" : fileName.Trim());
        if (string.IsNullOrWhiteSpace(normalizedFileName))
        {
            normalizedFileName = "filled.docx";
        }

        if (usedEntryNames.Add(normalizedFileName))
        {
            return normalizedFileName;
        }

        var baseName = Path.GetFileNameWithoutExtension(normalizedFileName);
        var extension = Path.GetExtension(normalizedFileName);
        var counter = 2;
        while (true)
        {
            var candidate = $"{baseName} ({counter}){extension}";
            if (usedEntryNames.Add(candidate))
            {
                return candidate;
            }

            counter++;
        }
    }

    private static bool StrictReuseTextEquals(string? left, string? right)
    {
        return string.Equals(
            NormalizeForDedup(left),
            NormalizeForDedup(right),
            StringComparison.Ordinal);
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

    public async Task<MatchingOperationResult<SimilarityResponse>> ComputeSimilarityAsync(SimilarityRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text1) || string.IsNullOrWhiteSpace(request.Text2))
        {
            throw Failure(400, "文本不能为空");
        }

        var tpSession = await _textPipeline.CreateSessionAsync();
        var t1 = tpSession.Process(request.Text1);
        var t2 = tpSession.Process(request.Text2);

        var config = ConvertToMatchingConfig(request.Config);
        Dictionary<string, double> scores;
        try
        {
            scores = await _matchingService.ComputeSimilarityAsync(t1, t2, config);
        }
        catch (AiServiceUnavailableException ex)
        {
            throw Failure(400, ex.Reason);
        }

        var response = new SimilarityResponse
        {
            TotalScore = scores.TryGetValue("Total", out var total) ? total : 0,
            Scores = scores
        };

        return Result(response);
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
        var rawCount = await baseQuery.CountAsync();
        var scopedSpecs = await ApplySpecScopeToQuery(baseQuery, scope)
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
            rawCount, scopedSpecs.Count, dedupedSpecs.Count, customerId, processId, machineModelId);

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
            newEmbeddings = await _embeddingService.GenerateEmbeddingsAsync(missingTexts, embeddingServiceId);
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
    private static MatchingConfig ConvertToMatchingConfig(MatchConfigDto? dto)
    {
        if (dto == null)
        {
            return new MatchingConfig();
        }

        var strategy = Enum.IsDefined(dto.MatchingStrategy)
            ? dto.MatchingStrategy
            : MatchingStrategy.SingleStage;

        if (dto.UseLlmEntityResolution && strategy != MatchingStrategy.MultiStage)
        {
            throw Failure(400, "LLM 实体判别仅支持多阶段匹配策略，请切换为多阶段后再启用");
        }

        return new MatchingConfig
        {
            MatchingStrategy = strategy,
            EmbeddingServiceId = dto.EmbeddingServiceId,
            LlmServiceId = dto.LlmServiceId,
            MinScoreThreshold = dto.MinScoreThreshold,
            HighConfidenceThreshold = NormalizeHighConfidenceThreshold(dto.HighConfidenceThreshold),
            RecallTopK = Math.Clamp(dto.RecallTopK, 1, 20),
            AmbiguityMargin = Math.Clamp(dto.AmbiguityMargin, 0, 1),
            UseLlmEntityResolution = dto.UseLlmEntityResolution,
            LlmEntityResolutionTopCandidates = Math.Clamp(dto.LlmEntityResolutionTopCandidates, 1, 10),
            LlmEntityPositiveConfidenceThreshold = Math.Clamp(dto.LlmEntityPositiveConfidenceThreshold, 0, 1),
            LlmEntityConflictReviewConfidenceThreshold = Math.Clamp(dto.LlmEntityConflictReviewConfidenceThreshold, 0, 1),
            LlmEntityConflictRejectConfidenceThreshold = Math.Clamp(dto.LlmEntityConflictRejectConfidenceThreshold, 0, 1),
            UseLlmReview = dto.UseLlmReview,
            UseLlmSuggestion = dto.UseLlmSuggestion,
            SuggestNoMatchRows = dto.SuggestNoMatchRows,
            LlmSuggestionScoreThreshold = dto.LlmSuggestionScoreThreshold,
            LlmParallelism = Math.Clamp(dto.LlmParallelism, 1, 10),
            LlmRowTimeoutSeconds = Math.Clamp(dto.LlmRowTimeoutSeconds, 5, 300),
            LlmRetryCount = Math.Clamp(dto.LlmRetryCount, 0, 3),
            LlmCircuitBreakFailures = Math.Clamp(dto.LlmCircuitBreakFailures, 3, 200),
            FilterEmptySourceRows = dto.FilterEmptySourceRows
        };
    }

    private static bool CanApplyMatchedSpec(FillMapping mapping, double highConfidenceThreshold)
    {
        if (mapping.ManualConfirmed)
        {
            return true;
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

        if (result.Score >= NormalizeHighConfidenceThreshold(highConfidenceThreshold))
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
                    RerankSummary = candidate.RerankSummary
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

    private async Task<WordFile?> GetAccessibleWordFileAsync(int fileId, DataScopeResult scope)
    {
        var wordFile = await _unitOfWork.WordFiles.GetByIdAsync(fileId);
        if (wordFile == null)
        {
            return null;
        }

        return WordFileDataScopeHelper.CanAccess(wordFile, scope) ? wordFile : null;
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

    private async Task<List<MatchSourceItem>> ExtractMatchSourceItemsFromFileAsync(
        int fileId,
        int tableIndex,
        int projectColumnIndex,
        int specificationColumnIndex,
        DataScopeResult? scope = null,
        int? headerRowStart = null,
        int? headerRowCount = null,
        int? dataStartRow = null,
        bool filterEmptySourceRows = true)
    {
        var wordFile = scope == null
            ? await _unitOfWork.WordFiles.GetByIdAsync(fileId)
            : await GetAccessibleWordFileAsync(fileId, scope);
        if (wordFile == null)
        {
            return [];
        }

        var parser = _documentServiceFactory.GetParser(GetDocumentType(wordFile.FileType));
        if (parser == null)
        {
            return [];
        }

        using var stream = OpenWordFileReadStream(wordFile);
        TableData tableData;
        int excelDataStartRowIndexForWriteBack = 1;
        try
        {
            var mapping = new ColumnMapping
            {
                HeaderRowIndex = 0,
                HeaderRowCount = 1,
                DataStartRowIndex = 1
            };

            if (wordFile.FileType == UploadedFileType.ExcelXlsx)
            {
                IReadOnlyList<TableInfo> tables;
                using (var metaStream = OpenWordFileReadStream(wordFile))
                {
                    tables = await parser.GetTablesAsync(metaStream);
                }

                if (tableIndex < 0 || tableIndex >= tables.Count)
                {
                    return [];
                }

                var sheetInfo = tables[tableIndex];
                var usedStartRow = Math.Max(1, sheetInfo.UsedRangeStartRow);

                var normalizedHeaderRowStart = headerRowStart.GetValueOrDefault(usedStartRow);
                if (normalizedHeaderRowStart < usedStartRow)
                {
                    normalizedHeaderRowStart = usedStartRow;
                }

                var normalizedHeaderRowCount = headerRowCount.GetValueOrDefault(1);
                if (normalizedHeaderRowCount < 0)
                {
                    normalizedHeaderRowCount = 0;
                }

                var minDataStartRow = normalizedHeaderRowStart + normalizedHeaderRowCount;
                var normalizedDataStartRow = dataStartRow.GetValueOrDefault(minDataStartRow);
                if (normalizedDataStartRow < minDataStartRow)
                {
                    normalizedDataStartRow = minDataStartRow;
                }

                mapping = new ColumnMapping
                {
                    HeaderRowIndex = Math.Max(0, normalizedHeaderRowStart - usedStartRow),
                    HeaderRowCount = Math.Max(1, normalizedHeaderRowCount == 0 ? 1 : normalizedHeaderRowCount),
                    DataStartRowIndex = Math.Max(0, normalizedDataStartRow - usedStartRow)
                };
                excelDataStartRowIndexForWriteBack = mapping.DataStartRowIndex;
            }

            tableData = await parser.ExtractTableDataAsync(stream, tableIndex, mapping);
        }
        catch
        {
            return [];
        }

        if (tableData.ColumnCount < 2)
        {
            return [];
        }

        if (projectColumnIndex < 0 || projectColumnIndex >= tableData.ColumnCount)
        {
            return [];
        }

        if (specificationColumnIndex < 0 || specificationColumnIndex >= tableData.ColumnCount)
        {
            return [];
        }

        // 提取数据行（rowIndex 使用文档中的真实行号，便于回写）
        var items = new List<MatchSourceItem>();
        foreach (var row in tableData.Rows)
        {
            var project = row.GetValue(projectColumnIndex) ?? "";
            var spec = row.GetValue(specificationColumnIndex) ?? "";

            if (filterEmptySourceRows &&
                string.IsNullOrWhiteSpace(project) &&
                string.IsNullOrWhiteSpace(spec))
            {
                continue;
            }

            // Excel 解析器的数据行索引从 0 开始（对应 DataStartRowIndex），
            // 回写时需要加回 DataStartRowIndex，才能定位到 UsedRange 内的真实行。
            var writeBackRowIndex = row.Index;
            if (wordFile.FileType == UploadedFileType.ExcelXlsx)
            {
                writeBackRowIndex += excelDataStartRowIndexForWriteBack;
            }

            items.Add(new MatchSourceItem
            {
                RowIndex = writeBackRowIndex,
                Project = project.Trim(),
                Specification = spec.Trim()
            });
        }

        return items;
    }

    /// <summary>
    /// 将填充结果直接写回源文件（用于 Excel 回写模式）。
    /// </summary>
    private async Task<WriteBackSummary> ApplyFillResultToSourceFileAsync(WordFile wordFile, FillTaskResult taskResult, IDocumentWriter writer)
    {
        using var resultStream = new MemoryStream();
        await using (var sourceStream = OpenWordFileReadStream(wordFile))
        {
            await sourceStream.CopyToAsync(resultStream);
        }
        resultStream.Position = 0;
        var requestedCells = 0;
        var writtenCells = 0;

        if (taskResult.IsBatchMode)
        {
            var tableOperations = new Dictionary<int, List<CellWriteOperation>>();
            foreach (var entry in taskResult.TableEntries)
            {
                var operations = BuildCellWriteOperations(entry.FillResults, entry.AcceptanceColumnIndex, entry.RemarkColumnIndex);
                if (operations.Count > 0)
                {
                    requestedCells += operations.Count;
                    tableOperations[entry.TableIndex] = operations;
                }
            }

            if (tableOperations.Count > 0)
            {
                writtenCells += await writer.WriteMultipleTablesAsync(resultStream, tableOperations);
            }
        }
        else
        {
            var operations = BuildCellWriteOperations(
                taskResult.FillResults,
                taskResult.AcceptanceColumnIndex ?? 0,
                taskResult.RemarkColumnIndex);

            if (operations.Count > 0)
            {
                requestedCells += operations.Count;
                writtenCells += await writer.WriteTableDataAsync(resultStream, taskResult.SourceTableIndex, operations);
            }
        }

        if (writtenCells > 0)
        {
            var updatedContent = resultStream.ToArray();
            await PersistUpdatedSourceFileAsync(wordFile, updatedContent);
        }

        return new WriteBackSummary(requestedCells, writtenCells);
    }

    /// <summary>
    /// 构建单表/多表通用的单元格写入操作列表。
    /// </summary>
    private static List<CellWriteOperation> BuildCellWriteOperations(
        List<FillResult> fillResults,
        int acceptanceColumnIndex,
        int? remarkColumnIndex)
    {
        var operations = new List<CellWriteOperation>();
        foreach (var fillResult in fillResults)
        {
            operations.Add(new CellWriteOperation
            {
                RowIndex = fillResult.RowIndex,
                ColumnIndex = acceptanceColumnIndex,
                Value = fillResult.Acceptance,
                PreserveFormatting = true
            });

            if (remarkColumnIndex.HasValue &&
                remarkColumnIndex.Value != acceptanceColumnIndex &&
                !string.IsNullOrWhiteSpace(fillResult.Remark))
            {
                operations.Add(new CellWriteOperation
                {
                    RowIndex = fillResult.RowIndex,
                    ColumnIndex = remarkColumnIndex.Value,
                    Value = fillResult.Remark!,
                    PreserveFormatting = true
                });
            }
        }

        return operations;
    }

    /// <summary>
    /// 持久化更新后的源文件内容（文件系统优先，DB二进制兜底）。
    /// </summary>
    private async Task PersistUpdatedSourceFileAsync(WordFile wordFile, byte[] updatedContent)
    {
        if (!string.IsNullOrWhiteSpace(wordFile.FilePath))
        {
            var fullPath = _fileStorage.GetAbsolutePath(wordFile.FilePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await System.IO.File.WriteAllBytesAsync(fullPath, updatedContent);
        }
        else
        {
            wordFile.FilePath = wordFile.FileType == UploadedFileType.ExcelXlsx
                ? await _fileStorage.SaveUploadedExcelAsync(wordFile.FileName, updatedContent)
                : await _fileStorage.SaveUploadedWordAsync(wordFile.FileName, updatedContent);
        }

        // 与现有兼容模型保持一致：同步更新 DB 二进制和哈希。
        wordFile.FileContent = updatedContent;
        wordFile.FileHash = FileStorageService.ComputeSha256(updatedContent);
    }

    private static DocumentType GetDocumentType(UploadedFileType fileType)
    {
        return fileType == UploadedFileType.ExcelXlsx
            ? DocumentType.Excel
            : DocumentType.Word;
    }

    private static string GetDownloadFileExtension(UploadedFileType fileType)
    {
        return fileType == UploadedFileType.ExcelXlsx ? ".xlsx" : ".docx";
    }

    private static string GetDownloadContentType(UploadedFileType fileType)
    {
        return fileType == UploadedFileType.ExcelXlsx
            ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            : "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    }

    /// <summary>
    /// 打开Word文件读取流：优先文件系统路径，缺失时回退到DB二进制（兼容旧数据）
    /// </summary>
    private Stream OpenWordFileReadStream(WordFile wordFile)
    {
        if (!string.IsNullOrWhiteSpace(wordFile.FilePath))
        {
            var fullPath = _fileStorage.GetAbsolutePath(wordFile.FilePath);
            if (System.IO.File.Exists(fullPath))
            {
                return System.IO.File.OpenRead(fullPath);
            }
        }

        if (wordFile.FileContent != null && wordFile.FileContent.Length > 0)
        {
            return new MemoryStream(wordFile.FileContent);
        }

        throw new InvalidOperationException("文件内容不可用（未找到物理文件且数据库内容为空）");
    }

    /// <summary>
    /// 保存填充任务快照（MySQL 持久化，避免 IIS 回收丢失）
    /// </summary>
    private async Task SaveFillTaskSnapshotAsync(ClaimsPrincipal user, FillTaskResult taskResult)
    {
        var owner = ResolveTaskOwner(user);
        taskResult.PayloadVersion = CurrentFillTaskPayloadVersion;
        var payload = JsonSerializer.Serialize(taskResult, FillTaskJsonOptions);
        var existed = await _unitOfWork.MatchingFillTasks.GetByTaskIdAsync(taskResult.TaskId);
        if (existed == null)
        {
            await _unitOfWork.MatchingFillTasks.AddAsync(new MatchingFillTask
            {
                TaskId = taskResult.TaskId,
                SourceFileId = taskResult.SourceFileId,
                CreatedByUserId = owner.UserId,
                CompanyId = owner.CompanyId,
                PayloadJson = payload,
                CreatedAt = taskResult.CreatedAt
            });
        }
        else
        {
            existed.SourceFileId = taskResult.SourceFileId;
            existed.CreatedByUserId = owner.UserId;
            existed.CompanyId = owner.CompanyId;
            existed.PayloadJson = payload;
            existed.CreatedAt = taskResult.CreatedAt;
            _unitOfWork.MatchingFillTasks.Update(existed);
        }

        var expireTime = DateTime.UtcNow.AddHours(-FillTaskRetentionHours);
        await CleanupExpiredFillTaskArtifactsAsync(expireTime);
        await _unitOfWork.SaveChangesAsync();
    }

    private async Task CleanupExpiredFillTaskArtifactsAsync(DateTime expireTime)
    {
        var expiredTasks = await _unitOfWork.MatchingFillTasks
            .Query(asNoTracking: false)
            .Where(task => task.CreatedAt < expireTime)
            .ToListAsync();

        if (expiredTasks.Count == 0)
        {
            return;
        }

        foreach (var expiredTask in expiredTasks)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(expiredTask.PayloadJson))
                {
                    continue;
                }

                var snapshot = DeserializeFillTaskResult(expiredTask.PayloadJson);
                if (!string.IsNullOrWhiteSpace(snapshot?.DownloadArtifactRelativePath))
                {
                    await _fileStorage.DeleteIfExistsAsync(snapshot.DownloadArtifactRelativePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理过期填充任务产物失败: {TaskId}", expiredTask.TaskId);
            }
        }

        _unitOfWork.MatchingFillTasks.RemoveRange(expiredTasks);
    }

    /// <summary>
    /// 读取填充任务快照
    /// </summary>
    private async Task<FillTaskResult?> LoadFillTaskSnapshotAsync(ClaimsPrincipal user, string taskId)
    {
        var entity = await _unitOfWork.MatchingFillTasks.GetByTaskIdAsync(taskId);
        if (entity == null || string.IsNullOrWhiteSpace(entity.PayloadJson))
            return null;

        EnsureTaskOwnership(user, entity);

        try
        {
            return DeserializeFillTaskResult(entity.PayloadJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "任务快照反序列化失败: {TaskId}", taskId);
            return null;
        }
    }

    private static FillTaskResult? DeserializeFillTaskResult(string payload)
    {
        var result = JsonSerializer.Deserialize<FillTaskResult>(payload, FillTaskJsonOptions);
        if (result == null)
            return null;

        if (result.PayloadVersion <= 0)
        {
            result.PayloadVersion = 1;
        }

        return result;
    }

    private static (int UserId, int CompanyId) ResolveTaskOwner(ClaimsPrincipal user)
    {
        var userId = AuthClaimHelper.GetUserId(user);
        var companyId = AuthClaimHelper.GetCompanyId(user);
        if (!userId.HasValue || !companyId.HasValue)
        {
            throw Failure(401, "会话缺少用户上下文");
        }

        return (userId.Value, companyId.Value);
    }

    private static void EnsureTaskOwnership(ClaimsPrincipal user, MatchingFillTask entity)
    {
        if (!entity.CreatedByUserId.HasValue || !entity.CompanyId.HasValue)
        {
            throw NotFoundFailure("任务不存在或已过期");
        }

        var owner = ResolveTaskOwner(user);
        if (entity.CreatedByUserId != owner.UserId || entity.CompanyId != owner.CompanyId)
        {
            throw NotFoundFailure("任务不存在或已过期");
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
