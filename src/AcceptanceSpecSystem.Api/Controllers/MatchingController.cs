using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Core.Documents;
using AcceptanceSpecSystem.Core.Documents.Interfaces;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 智能匹配API控制器
/// </summary>
[Route("api/matching")]
public class MatchingController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMatchingService _matchingService;
    private readonly DocumentServiceFactory _documentServiceFactory;
    private readonly IFileStorageService _fileStorage;
    private readonly ITextPreprocessingPipeline _textPipeline;
    private readonly ILlmReviewService _llmReviewService;
    private readonly ILlmSuggestionService _llmSuggestionService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MatchingController> _logger;

    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly JsonSerializerOptions FillTaskJsonOptions = new(JsonSerializerDefaults.Web);
    private const int FillTaskRetentionHours = 24;

    /// <summary>
    /// 创建匹配控制器实例
    /// </summary>
    public MatchingController(
        IUnitOfWork unitOfWork,
        IMatchingService matchingService,
        DocumentServiceFactory documentServiceFactory,
        IFileStorageService fileStorage,
        ITextPreprocessingPipeline textPipeline,
        ILlmReviewService llmReviewService,
        ILlmSuggestionService llmSuggestionService,
        IServiceScopeFactory scopeFactory,
        ILogger<MatchingController> logger)
    {
        _unitOfWork = unitOfWork;
        _matchingService = matchingService;
        _documentServiceFactory = documentServiceFactory;
        _fileStorage = fileStorage;
        _textPipeline = textPipeline;
        _llmReviewService = llmReviewService;
        _llmSuggestionService = llmSuggestionService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// 匹配预览
    /// </summary>
    /// <remarks>
    /// 对输入的文本列表进行匹配预览，仅返回每个项的最佳匹配结果
    /// </remarks>
    [HttpPost("preview")]
    [ProducesResponseType(typeof(ApiResponse<MatchPreviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MatchPreviewResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<MatchPreviewResponse>>> Preview([FromBody] MatchPreviewRequest request)
    {
        var sw = Stopwatch.StartNew();
        var config = ConvertToMatchingConfig(request.Config);

        // 兼容前端：如果未传Items，则尝试从文件表格提取项目/规格作为待匹配项
        if (request.Items == null || request.Items.Count == 0)
        {
            if (request.FileId.HasValue && request.TableIndex.HasValue)
            {
                if (!request.ProjectColumnIndex.HasValue || !request.SpecificationColumnIndex.HasValue)
                {
                    return Error<MatchPreviewResponse>(400, "请手动指定项目列与规格列索引");
                }

                var extracted = await ExtractMatchSourceItemsFromFileAsync(
                    request.FileId.Value,
                    request.TableIndex.Value,
                    request.ProjectColumnIndex.Value,
                    request.SpecificationColumnIndex.Value,
                    request.HeaderRowStart,
                    request.HeaderRowCount,
                    request.DataStartRow,
                    config.FilterEmptySourceRows);

                if (extracted.Count == 0)
                {
                    return Error<MatchPreviewResponse>(400, "未从表格中提取到可匹配的项目/规格数据");
                }

                request.Items = extracted;
            }
            else
            {
                return Error<MatchPreviewResponse>(400, "待匹配文本列表不能为空");
            }
        }

        // 获取候选验收规格
        var candidates = await GetCandidatesAsync(request.CustomerId, request.ProcessId, request.MachineModelId);
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

            return Success(new MatchPreviewResponse
            {
                Items = emptyItems,
                TotalMatched = 0,
                HighConfidenceCount = 0,
                MediumConfidenceCount = 0,
                LowConfidenceCount = 0,
                AmbiguousCount = 0
            }, "没有找到可匹配的验收规格");
        }

        // 创建文本处理会话（按 TextProcessingConfig 开关做预处理）
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
            return Error<MatchPreviewResponse>(400, $"Embedding 服务不可用: {ex.Reason}");
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
                NoMatchReason = noMatchReason
            };

            previewItems.Add(previewItem);

            if (previewItem.BestMatch != null)
            {
                var score = previewItem.BestMatch.Score;
                if (score >= 0.8) highCount++;
                else if (score >= 0.6) mediumCount++;
                else lowCount++;
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

        return Success(response);
    }

    /// <summary>
    /// LLM 复核/生成流式输出（SSE）
    /// </summary>
    [HttpPost("llm-stream")]
    public async Task LlmStream([FromBody] MatchLlmStreamRequest request)
    {
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.TryAdd("X-Accel-Buffering", "no");
        Response.ContentType = "text/event-stream";

        if (request.Items == null || request.Items.Count == 0)
        {
            await WriteSseEventAsync("error", new { message = "Items不能为空" }, CancellationToken.None);
            return;
        }

        var config = ConvertToMatchingConfig(request.Config);
        var cancellationToken = HttpContext.RequestAborted;

        // 并行处理：每行独立创建 DI 作用域（DbContext 非线程安全），SSE 写入用信号量串行化
        var sseWriteLock = new SemaphoreSlim(1, 1);
        var sw = Stopwatch.StartNew();

        var parallelism = config.LlmParallelism;
        var rowTimeoutSeconds = config.LlmRowTimeoutSeconds;
        var retryCount = config.LlmRetryCount;
        var circuitBreakFailures = config.LlmCircuitBreakFailures;
        var reviewCount = request.Items.Count(item => config.UseLlmReview && item.BestMatchSpecId.HasValue);
        var suggestionCount = request.Items.Count(item => ShouldGenerateSuggestion(config, item));
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
            request.Items.Count, reviewCount, suggestionCount, parallelism,
            config.UseLlmReview, config.UseLlmSuggestion, config.SuggestNoMatchRows, config.LlmSuggestionScoreThreshold,
            rowTimeoutSeconds, retryCount, circuitBreakFailures);

        try
        {
            await Parallel.ForEachAsync(
                request.Items,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = parallelism,
                    CancellationToken = cancellationToken
                },
                async (item, ct) =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var reviewService = scope.ServiceProvider.GetRequiredService<ILlmReviewService>();
                    var suggestionService = scope.ServiceProvider.GetRequiredService<ILlmSuggestionService>();
                    var location = FormatStreamItemLocation(item);

                    if (Volatile.Read(ref circuitOpened) == 1)
                    {
                        await WriteCircuitOpenEventsAsync(item, config, sseWriteLock, ct);
                        return;
                    }

                    // 同一行内：先复核，再生成建议（顺序执行）
                    if (config.UseLlmReview && item.BestMatchSpecId.HasValue)
                    {
                        _logger.LogDebug("[LLM-Stream] {Location}: 开始复核 (specId={SpecId}, score={Score:P1})",
                            location, item.BestMatchSpecId, item.BestMatchScore ?? 0);

                        var reviewResult = await ExecuteLlmStepWithPolicyAsync(
                            "review",
                            item,
                            rowTimeoutSeconds,
                            retryCount,
                            token => StreamLlmReviewAsync(item, config, token, unitOfWork, reviewService, sseWriteLock),
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
                        await WriteCircuitOpenEventsAsync(item, config, sseWriteLock, ct);
                        return;
                    }

                    if (ShouldGenerateSuggestion(config, item))
                    {
                        _logger.LogDebug("[LLM-Stream] {Location}: 开始生成建议 (specId={SpecId}, score={Score}, threshold={Threshold}, suggestNoMatch={SuggestNoMatch})",
                            location, item.BestMatchSpecId, item.BestMatchScore?.ToString("P1") ?? "无匹配",
                            config.LlmSuggestionScoreThreshold, config.SuggestNoMatchRows);

                        var suggestionResult = await ExecuteLlmStepWithPolicyAsync(
                            "suggestion",
                            item,
                            rowTimeoutSeconds,
                            retryCount,
                            token => StreamLlmSuggestionAsync(item, config, token, unitOfWork, suggestionService, sseWriteLock),
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

    /// <summary>
    /// 执行填充
    /// </summary>
    /// <remarks>
    /// 根据匹配结果，将验收标准填充到源文件中，返回填充后的文件下载链接
    /// </remarks>
    [HttpPost("execute")]
    [AuditOperation("execute", "matching-fill")]
    [ProducesResponseType(typeof(ApiResponse<ExecuteFillResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExecuteFillResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ExecuteFillResponse>>> ExecuteFill([FromBody] ExecuteFillRequest request)
    {
        if (request.Mappings == null || request.Mappings.Count == 0)
        {
            return Error<ExecuteFillResponse>(400, "填充映射不能为空");
        }

        var fileId = request.FileId ?? request.SourceFileId;
        var tableIndex = request.TableIndex ?? request.SourceTableIndex;

        if (!fileId.HasValue)
        {
            return Error<ExecuteFillResponse>(400, "源文件ID不能为空");
        }

        if (!tableIndex.HasValue)
        {
            return Error<ExecuteFillResponse>(400, "源表格索引不能为空");
        }

        // 获取源文件
        var wordFile = await _unitOfWork.WordFiles.GetByIdAsync(fileId.Value);
        if (wordFile == null)
        {
            return Error<ExecuteFillResponse>(400, "源文件不存在");
        }

        // 获取所有相关的验收规格
        var hasLlmSuggestions = request.Mappings.Any(m => m.UseLlmSuggestion);
        var specIds = request.Mappings
            .Where(m => !m.UseLlmSuggestion)
            .Select(m => m.SpecId ?? m.SelectedSpecId)
            .Where(id => id.HasValue && id.Value > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (specIds.Count == 0 && !hasLlmSuggestions)
        {
            return Error<ExecuteFillResponse>(400, "未提供有效的验收规格ID");
        }

        var specDict = new Dictionary<int, AcceptanceSpec>();
        if (specIds.Count > 0)
        {
            var specs = await _unitOfWork.AcceptanceSpecs.FindAsync(s => specIds.Contains(s.Id));
            specDict = specs.ToDictionary(s => s.Id);
        }

        // 获取文档解析器
        var parser = _documentServiceFactory.GetParser(GetDocumentType(wordFile.FileType));
        if (parser == null)
        {
            return Error<ExecuteFillResponse>(500, "文档解析器不可用");
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
                return Error<ExecuteFillResponse>(400, "表格索引超出范围");
            }
        }

        // 列索引必须由用户手动指定（不做关键字推断）
        if (!request.AcceptanceColumnIndex.HasValue)
        {
            return Error<ExecuteFillResponse>(400, "请手动指定验收列索引");
        }
        var acceptanceColumnIndex = request.AcceptanceColumnIndex.Value;
        var remarkColumnIndex = request.RemarkColumnIndex;

        // 执行填充
        int filledCount = 0;
        int skippedCount = 0;
        var fillResults = new List<FillResult>();

        foreach (var fillMapping in request.Mappings)
        {
            if (fillMapping.UseLlmSuggestion)
            {
                var acceptance = fillMapping.Acceptance?.Trim();
                var remark = fillMapping.Remark?.Trim();
                if (string.IsNullOrWhiteSpace(acceptance) && string.IsNullOrWhiteSpace(remark))
                {
                    skippedCount++;
                    continue;
                }

                fillResults.Add(new FillResult
                {
                    RowIndex = fillMapping.RowIndex,
                    SpecId = 0,
                    Acceptance = acceptance ?? "",
                    Remark = remark
                });
                filledCount++;
                continue;
            }

            var selectedSpecId = (fillMapping.SpecId ?? fillMapping.SelectedSpecId) ?? 0;
            if (selectedSpecId <= 0 || !specDict.TryGetValue(selectedSpecId, out var spec))
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
            CreatedAt = DateTime.Now
        };

        var isExcelSource = wordFile.FileType == UploadedFileType.ExcelXlsx;
        if (isExcelSource)
        {
            var writer = _documentServiceFactory.GetWriter(DocumentType.Excel);
            if (writer == null)
            {
                return Error<ExecuteFillResponse>(500, "Excel 文档写入器不可用");
            }

            try
            {
                var writeBackSummary = await ApplyFillResultToSourceFileAsync(wordFile, taskResult, writer);
                if (writeBackSummary.RequestedCells > 0 && writeBackSummary.WrittenCells == 0)
                {
                    return Error<ExecuteFillResponse>(400, "未写入任何单元格，请检查列索引和行配置是否正确");
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
                return Error<ExecuteFillResponse>(500, $"写回 Excel 失败: {ex.Message}");
            }
        }

        await SaveFillTaskSnapshotAsync(taskResult);

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

        return Success(response, isExcelSource
            ? $"填充完成：已填充{filledCount}行，跳过{skippedCount}行，已写回并可下载 Excel"
            : $"填充完成：已填充{filledCount}行，跳过{skippedCount}行");
    }

    /// <summary>
    /// 下载填充结果
    /// </summary>
    [HttpGet("download/{taskId}")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(string taskId)
    {
        var taskResult = await LoadFillTaskSnapshotAsync(taskId);
        if (taskResult == null)
        {
            return NotFound(ApiResponse.Error(404, "任务不存在或已过期"));
        }

        // 获取源文件
        var wordFile = await _unitOfWork.WordFiles.GetByIdAsync(taskResult.SourceFileId);
        if (wordFile == null)
        {
            return NotFound(ApiResponse.Error(404, "源文件不存在"));
        }

        // 获取文档写入器
        var writer = _documentServiceFactory.GetWriter(GetDocumentType(wordFile.FileType));
        if (writer == null)
        {
            return BadRequest(ApiResponse.Error(500, "文档写入器不可用"));
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
                return BadRequest(ApiResponse.Error(500, $"填充文档失败: {ex.Message}"));
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

        // 生成下载文件名
        var originalFileName = Path.GetFileNameWithoutExtension(wordFile.FileName);
        var fileExtension = GetDownloadFileExtension(wordFile.FileType);
        var contentType = GetDownloadContentType(wordFile.FileType);
        var downloadFileName = $"{originalFileName}_filled_{DateTime.Now:yyyyMMddHHmmss}{fileExtension}";

        _logger.LogInformation("下载填充结果: 任务{TaskId}, 文件{FileName}", taskId, downloadFileName);

        return File(resultContent, contentType, downloadFileName);
    }

    /// <summary>
    /// 批量匹配预览（多表格一次性预览）
    /// </summary>
    [HttpPost("batch-preview")]
    [ProducesResponseType(typeof(ApiResponse<BatchPreviewResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BatchPreviewResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<BatchPreviewResponse>>> BatchPreview([FromBody] BatchPreviewRequest request)
    {
        var sw = Stopwatch.StartNew();

        if (request.Tables == null || request.Tables.Count == 0)
        {
            return Error<BatchPreviewResponse>(400, "请至少选择一个表格");
        }

        if (request.FileId <= 0)
        {
            return Error<BatchPreviewResponse>(400, "文件ID不能为空");
        }

        // 一次获取候选集
        var candidates = await GetCandidatesAsync(request.CustomerId, request.ProcessId, request.MachineModelId);
        var config = ConvertToMatchingConfig(request.Config);

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
            return Error<BatchPreviewResponse>(400, "范围内无候选数据");
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
                return Error<BatchPreviewResponse>(400, $"Embedding 服务不可用: {ex.Reason}");
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
                    NoMatchReason = noMatchReason
                };

                tableResult.Items.Add(previewItem);

                if (previewItem.BestMatch != null)
                {
                    var score = previewItem.BestMatch.Score;
                    if (score >= 0.8) highCount++;
                    else if (score >= 0.6) mediumCount++;
                    else lowCount++;
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

        return Success(response);
    }

    /// <summary>
    /// 批量执行填充（多表格一次性填充）
    /// </summary>
    [HttpPost("batch-execute")]
    [AuditOperation("execute-batch", "matching-fill")]
    [ProducesResponseType(typeof(ApiResponse<ExecuteFillResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExecuteFillResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<ExecuteFillResponse>>> BatchExecuteFill([FromBody] BatchExecuteFillRequest request)
    {
        if (request.Tables == null || request.Tables.Count == 0)
        {
            return Error<ExecuteFillResponse>(400, "请至少提供一个表格的填充映射");
        }

        if (request.FileId <= 0)
        {
            return Error<ExecuteFillResponse>(400, "文件ID不能为空");
        }

        // 获取源文件
        var wordFile = await _unitOfWork.WordFiles.GetByIdAsync(request.FileId);
        if (wordFile == null)
        {
            return Error<ExecuteFillResponse>(400, "源文件不存在");
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

        var specDict = new Dictionary<int, Data.Entities.AcceptanceSpec>();
        if (allSpecIds.Count > 0)
        {
            var specs = await _unitOfWork.AcceptanceSpecs.FindAsync(s => allSpecIds.Contains(s.Id));
            specDict = specs.ToDictionary(s => s.Id);
        }

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
                if (mapping.UseLlmSuggestion)
                {
                    var acceptance = mapping.Acceptance?.Trim();
                    var remark = mapping.Remark?.Trim();
                    if (string.IsNullOrWhiteSpace(acceptance) && string.IsNullOrWhiteSpace(remark))
                    {
                        totalSkipped++;
                        continue;
                    }

                    entry.FillResults.Add(new FillResult
                    {
                        RowIndex = mapping.RowIndex,
                        SpecId = 0,
                        Acceptance = acceptance ?? "",
                        Remark = remark
                    });
                    totalFilled++;
                }
                else
                {
                    var selectedSpecId = (mapping.SpecId ?? mapping.SelectedSpecId) ?? 0;
                    if (selectedSpecId <= 0 || !specDict.TryGetValue(selectedSpecId, out var spec))
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
            CreatedAt = DateTime.Now
        };

        var isExcelSource = wordFile.FileType == UploadedFileType.ExcelXlsx;
        if (isExcelSource)
        {
            var writer = _documentServiceFactory.GetWriter(DocumentType.Excel);
            if (writer == null)
            {
                return Error<ExecuteFillResponse>(500, "Excel 文档写入器不可用");
            }

            try
            {
                var writeBackSummary = await ApplyFillResultToSourceFileAsync(wordFile, taskResult, writer);
                if (writeBackSummary.RequestedCells > 0 && writeBackSummary.WrittenCells == 0)
                {
                    return Error<ExecuteFillResponse>(400, "未写入任何单元格，请检查列索引和行配置是否正确");
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
                return Error<ExecuteFillResponse>(500, $"写回 Excel 失败: {ex.Message}");
            }
        }

        await SaveFillTaskSnapshotAsync(taskResult);

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

        return Success(response, isExcelSource
            ? $"批量填充完成：已填充{totalFilled}行，跳过{totalSkipped}行，已写回并可下载 Excel"
            : $"批量填充完成：已填充{totalFilled}行，跳过{totalSkipped}行");
    }

    /// <summary>
    /// 计算两个文本的相似度
    /// </summary>
    [HttpPost("similarity")]
    [ProducesResponseType(typeof(ApiResponse<SimilarityResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SimilarityResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<SimilarityResponse>>> ComputeSimilarity([FromBody] SimilarityRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text1) || string.IsNullOrWhiteSpace(request.Text2))
        {
            return Error<SimilarityResponse>(400, "文本不能为空");
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
            return Error<SimilarityResponse>(400, ex.Reason);
        }

        var response = new SimilarityResponse
        {
            TotalScore = scores.TryGetValue("Total", out var total) ? total : 0,
            Scores = scores
        };

        return Success(response);
    }

    /// <summary>
    /// 获取候选验收规格列表
    /// </summary>
    private async Task<List<MatchCandidate>> GetCandidatesAsync(int? customerId, int? processId, int? machineModelId)
    {
        IEnumerable<Data.Entities.AcceptanceSpec> specs;

        // 优先按“客户 + 制程”组合筛选（即“一整份验规”的范围）
        if (customerId.HasValue && processId.HasValue && machineModelId.HasValue)
        {
            specs = await _unitOfWork.AcceptanceSpecs.FindAsync(s =>
                s.CustomerId == customerId.Value &&
                s.ProcessId == processId.Value &&
                s.MachineModelId == machineModelId.Value);
        }
        else if (customerId.HasValue && processId.HasValue)
        {
            specs = await _unitOfWork.AcceptanceSpecs.FindAsync(s =>
                s.CustomerId == customerId.Value && s.ProcessId == processId.Value);
        }
        else if (customerId.HasValue && machineModelId.HasValue)
        {
            specs = await _unitOfWork.AcceptanceSpecs.FindAsync(s =>
                s.CustomerId == customerId.Value && s.MachineModelId == machineModelId.Value);
        }
        else if (processId.HasValue && machineModelId.HasValue)
        {
            specs = await _unitOfWork.AcceptanceSpecs.FindAsync(s =>
                s.ProcessId == processId.Value && s.MachineModelId == machineModelId.Value);
        }
        else if (customerId.HasValue)
        {
            specs = await _unitOfWork.AcceptanceSpecs.FindAsync(s => s.CustomerId == customerId.Value);
        }
        else if (processId.HasValue)
        {
            specs = await _unitOfWork.AcceptanceSpecs.FindAsync(s => s.ProcessId == processId.Value);
        }
        else if (machineModelId.HasValue)
        {
            specs = await _unitOfWork.AcceptanceSpecs.FindAsync(s => s.MachineModelId == machineModelId.Value);
        }
        else
        {
            specs = await _unitOfWork.AcceptanceSpecs.GetAllAsync();
        }

        // 同一范围内可能存在重复导入（项目+规格相同，但验收/备注完整度不同）。
        // 这里先做候选去重，优先保留“验收标准非空 > 备注非空 > 导入时间新 > ID大”的记录，
        // 避免匹配命中到信息缺失的旧记录。
        var dedupedSpecs = specs
            .GroupBy(s => BuildCandidateDedupKey(s.Project, s.Specification))
            .Select(g => g
                .OrderByDescending(s => HasText(s.Acceptance))
                .ThenByDescending(s => HasText(s.Remark))
                .ThenByDescending(s => s.ImportedAt)
                .ThenByDescending(s => s.Id)
                .First())
            .ToList();

        _logger.LogInformation(
            "匹配候选去重: 原始{RawCount}条 -> 去重后{DedupedCount}条 (customerId={CustomerId}, processId={ProcessId}, machineModelId={MachineModelId})",
            specs.Count(), dedupedSpecs.Count, customerId, processId, machineModelId);

        return dedupedSpecs.Select(s => new MatchCandidate
        {
            SpecId = s.Id,
            Project = s.Project,
            Specification = s.Specification,
            Acceptance = s.Acceptance,
            Remark = s.Remark
        }).ToList();
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

        return new MatchingConfig
        {
            MatchingStrategy = Enum.IsDefined(typeof(MatchingStrategy), dto.MatchingStrategy)
                ? dto.MatchingStrategy
                : MatchingStrategy.SingleStage,
            EmbeddingServiceId = dto.EmbeddingServiceId,
            LlmServiceId = dto.LlmServiceId,
            MinScoreThreshold = dto.MinScoreThreshold,
            RecallTopK = Math.Clamp(dto.RecallTopK, 1, 20),
            AmbiguityMargin = Math.Clamp(dto.AmbiguityMargin, 0, 1),
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

    private async Task<LlmStepOutcome> StreamLlmReviewAsync(
        MatchLlmStreamItem item,
        MatchingConfig config,
        CancellationToken cancellationToken,
        IUnitOfWork unitOfWork,
        ILlmReviewService reviewService,
        SemaphoreSlim sseWriteLock)
    {
        var specId = item.BestMatchSpecId ?? 0;
        if (specId <= 0)
            return LlmStepOutcome.Failed;

        var location = FormatStreamItemLocation(item);

        var spec = await unitOfWork.AcceptanceSpecs.GetByIdAsync(specId);
        if (spec == null)
        {
            _logger.LogWarning("[LLM复核] {Location}: 最佳匹配规格ID={SpecId}不存在", location, specId);
            await WriteSseEventLockedAsync(sseWriteLock, "review.error", new
            {
                tableIndex = item.TableIndex,
                rowIndex = item.RowIndex,
                message = "最佳匹配规格不存在"
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
            LlmServiceId = config.LlmServiceId
        };

        await WriteSseEventLockedAsync(sseWriteLock, "review.start", new
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
                await WriteSseEventLockedAsync(sseWriteLock, "review.delta", new
                {
                    tableIndex = item.TableIndex,
                    rowIndex = item.RowIndex,
                    chunk
                }, cancellationToken);
            }

            if (reviewService.TryParseReviewResult(buffer.ToString(), out var result))
            {
                _logger.LogDebug("[LLM复核] {Location}: 完成, score={Score}, reason={Reason}",
                    location, result.Score, result.Reason);
                await WriteSseEventLockedAsync(sseWriteLock, "review.done", new
                {
                    tableIndex = item.TableIndex,
                    rowIndex = item.RowIndex,
                    score = result.Score,
                    reason = result.Reason,
                    commentary = result.Commentary
                }, cancellationToken);
                return LlmStepOutcome.Success;
            }
            else
            {
                _logger.LogWarning("[LLM复核] {Location}: JSON解析失败, 原始输出: {Raw}", location, buffer.ToString());
                await WriteSseEventLockedAsync(sseWriteLock, "review.error", new
                {
                    tableIndex = item.TableIndex,
                    rowIndex = item.RowIndex,
                    message = "LLM复核输出解析失败"
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
            await WriteSseEventLockedAsync(sseWriteLock, "review.error", new
            {
                tableIndex = item.TableIndex,
                rowIndex = item.RowIndex,
                message = ex.Reason
            }, cancellationToken);
            return LlmStepOutcome.Failed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM复核失败");
            await WriteSseEventLockedAsync(sseWriteLock, "review.error", new
            {
                tableIndex = item.TableIndex,
                rowIndex = item.RowIndex,
                message = "LLM复核失败"
            }, cancellationToken);
            return LlmStepOutcome.Failed;
        }
    }

    private async Task<LlmStepOutcome> StreamLlmSuggestionAsync(
        MatchLlmStreamItem item,
        MatchingConfig config,
        CancellationToken cancellationToken,
        IUnitOfWork unitOfWork,
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
            var spec = await unitOfWork.AcceptanceSpecs.GetByIdAsync(item.BestMatchSpecId.Value);
            if (spec != null)
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

        await WriteSseEventLockedAsync(sseWriteLock, "suggestion.start", new
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
                await WriteSseEventLockedAsync(sseWriteLock, "suggestion.delta", new
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
                await WriteSseEventLockedAsync(sseWriteLock, "suggestion.done", new
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
                await WriteSseEventLockedAsync(sseWriteLock, "suggestion.error", new
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
            await WriteSseEventLockedAsync(sseWriteLock, "suggestion.error", new
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
            await WriteSseEventLockedAsync(sseWriteLock, "suggestion.error", new
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
        MatchLlmStreamItem item,
        MatchingConfig config,
        SemaphoreSlim sseWriteLock,
        CancellationToken cancellationToken)
    {
        const string message = "LLM 失败率过高，已触发熔断，请稍后重试";
        if (config.UseLlmReview && item.BestMatchSpecId.HasValue)
        {
            await WriteSseEventLockedAsync(sseWriteLock, "review.error", new
            {
                tableIndex = item.TableIndex,
                rowIndex = item.RowIndex,
                message
            }, cancellationToken);
        }

        if (ShouldGenerateSuggestion(config, item))
        {
            await WriteSseEventLockedAsync(sseWriteLock, "suggestion.error", new
            {
                tableIndex = item.TableIndex,
                rowIndex = item.RowIndex,
                message
            }, cancellationToken);
        }
    }

    private async Task<LlmStepExecutionResult> ExecuteLlmStepWithPolicyAsync(
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

                await WriteSseEventLockedAsync(sseWriteLock, $"{stepName}.error", new
                {
                    tableIndex = item.TableIndex,
                    rowIndex = item.RowIndex,
                    message = $"{GetLlmStepDisplayName(stepName)}超时（>{timeoutSeconds}s）"
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
                await WriteSseEventLockedAsync(sseWriteLock, $"{stepName}.error", new
                {
                    tableIndex = item.TableIndex,
                    rowIndex = item.RowIndex,
                    message = $"{GetLlmStepDisplayName(stepName)}失败（已达到重试上限）"
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

    private async Task WriteSseEventAsync(string eventName, object data, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(data, SseJsonOptions);
        await Response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// 安全写入 SSE 事件：连接已断开时静默忽略，不抛异常
    /// </summary>
    private async Task WriteSseEventSafeAsync(string eventName, object data, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;
        try
        {
            await WriteSseEventAsync(eventName, data, cancellationToken);
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
    private async Task WriteSseEventLockedAsync(
        SemaphoreSlim sseWriteLock,
        string eventName,
        object data,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;
        await sseWriteLock.WaitAsync(cancellationToken);
        try
        {
            await WriteSseEventAsync(eventName, data, cancellationToken);
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
        int? headerRowStart = null,
        int? headerRowCount = null,
        int? dataStartRow = null,
        bool filterEmptySourceRows = true)
    {
        var wordFile = await _unitOfWork.WordFiles.GetByIdAsync(fileId);
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
    private async Task SaveFillTaskSnapshotAsync(FillTaskResult taskResult)
    {
        var payload = JsonSerializer.Serialize(taskResult, FillTaskJsonOptions);
        var existed = await _unitOfWork.MatchingFillTasks.GetByTaskIdAsync(taskResult.TaskId);
        if (existed == null)
        {
            await _unitOfWork.MatchingFillTasks.AddAsync(new MatchingFillTask
            {
                TaskId = taskResult.TaskId,
                SourceFileId = taskResult.SourceFileId,
                PayloadJson = payload,
                CreatedAt = taskResult.CreatedAt
            });
        }
        else
        {
            existed.SourceFileId = taskResult.SourceFileId;
            existed.PayloadJson = payload;
            existed.CreatedAt = taskResult.CreatedAt;
            _unitOfWork.MatchingFillTasks.Update(existed);
        }

        // 轻量清理历史快照，避免任务表无限增长
        var expireTime = DateTime.Now.AddHours(-FillTaskRetentionHours);
        await _unitOfWork.MatchingFillTasks.DeleteBeforeAsync(expireTime);
        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// 读取填充任务快照
    /// </summary>
    private async Task<FillTaskResult?> LoadFillTaskSnapshotAsync(string taskId)
    {
        var entity = await _unitOfWork.MatchingFillTasks.GetByTaskIdAsync(taskId);
        if (entity == null || string.IsNullOrWhiteSpace(entity.PayloadJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<FillTaskResult>(entity.PayloadJson, FillTaskJsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "任务快照反序列化失败: {TaskId}", taskId);
            return null;
        }
    }
}

/// <summary>
/// 填充任务结果
/// </summary>
internal class FillTaskResult
{
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

internal readonly record struct WriteBackSummary(int RequestedCells, int WrittenCells);

internal enum LlmStepOutcome
{
    Success = 0,
    Failed = 1,
    Timeout = 2
}

internal readonly record struct LlmStepExecutionResult(LlmStepOutcome Outcome, int RetriesUsed);
