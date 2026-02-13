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
using System.Collections.Concurrent;
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
    private readonly ILogger<MatchingController> _logger;

    // 存储填充任务结果（生产环境应使用Redis或数据库）
    private static readonly ConcurrentDictionary<string, FillTaskResult> _fillTaskResults = new();
    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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
        ILogger<MatchingController> logger)
    {
        _unitOfWork = unitOfWork;
        _matchingService = matchingService;
        _documentServiceFactory = documentServiceFactory;
        _fileStorage = fileStorage;
        _textPipeline = textPipeline;
        _llmReviewService = llmReviewService;
        _llmSuggestionService = llmSuggestionService;
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
                    request.SpecificationColumnIndex.Value);

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

        // 转换匹配配置
        var config = ConvertToMatchingConfig(request.Config);

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
                LowConfidenceCount = 0
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
            Embedding = c.Embedding
        }).ToList();

        // 执行匹配
        var previewItems = new List<MatchPreviewItem>();
        int highCount = 0, mediumCount = 0, lowCount = 0;

        foreach (var item in request.Items)
        {
            var processedProject = tpSession.Process(item.Project);
            var processedSpec = tpSession.Process(item.Specification);
            var sourceText = $"{processedProject} {processedSpec}".Trim();

            MatchResult? bestMatch = null;
            string? noMatchReason = null;
            try
            {
                var matches = await _matchingService.FindMatchesAsync(sourceText, processedCandidates, config);
                bestMatch = matches.FirstOrDefault();
                if (bestMatch == null)
                {
                    noMatchReason = processedCandidates.Count == 0
                        ? "范围内无候选数据"
                        : "最佳得分低于阈值";
                }
            }
            catch (AiServiceUnavailableException ex)
            {
                noMatchReason = ex.Reason;
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

            // 统计置信度
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
            LowConfidenceCount = lowCount
        };

        _logger.LogInformation(
            "匹配预览完成: 共{Total}项, 匹配{Matched}项, 高置信度{High}, 中置信度{Medium}, 低置信度{Low}",
            request.Items.Count, response.TotalMatched, highCount, mediumCount, lowCount);

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
            await WriteSseEventAsync("error", new { message = "Items不能为空" }, HttpContext.RequestAborted);
            return;
        }

        var config = ConvertToMatchingConfig(request.Config);
        var cancellationToken = HttpContext.RequestAborted;

        foreach (var item in request.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (config.UseLlmReview && item.BestMatchSpecId.HasValue)
            {
                await StreamLlmReviewAsync(item, config, cancellationToken);
            }

            if (config.UseLlmSuggestion &&
                (!item.BestMatchSpecId.HasValue ||
                 (item.BestMatchScore ?? 0) < config.LlmSuggestionScoreThreshold))
            {
                await StreamLlmSuggestionAsync(item, config, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 执行填充
    /// </summary>
    /// <remarks>
    /// 根据匹配结果，将验收标准填充到源文件中，返回填充后的文件下载链接
    /// </remarks>
    [HttpPost("execute")]
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
        var parser = _documentServiceFactory.GetParser(DocumentType.Word);
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

        // 存储填充结果（包含原文件和填充信息）
        _fillTaskResults[taskId] = new FillTaskResult
        {
            TaskId = taskId,
            SourceFileId = fileId.Value,
            SourceTableIndex = tableIndex.Value,
            AcceptanceColumnIndex = acceptanceColumnIndex,
            RemarkColumnIndex = remarkColumnIndex,
            FillResults = fillResults,
            CreatedAt = DateTime.Now
        };

        // 清理过期的任务结果（保留1小时）
        CleanupExpiredTasks();

        var response = new ExecuteFillResponse
        {
            TaskId = taskId,
            FilledCount = filledCount,
            SkippedCount = skippedCount,
            DownloadUrl = $"/api/matching/download/{taskId}"
        };

        _logger.LogInformation(
            "执行填充完成: 任务{TaskId}, 填充{Filled}行, 跳过{Skipped}行",
            taskId, filledCount, skippedCount);

        return Success(response, $"填充完成：已填充{filledCount}行，跳过{skippedCount}行");
    }

    /// <summary>
    /// 下载填充结果
    /// </summary>
    [HttpGet("download/{taskId}")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(string taskId)
    {
        if (!_fillTaskResults.TryGetValue(taskId, out var taskResult))
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
        var writer = _documentServiceFactory.GetWriter(DocumentType.Word);
        if (writer == null)
        {
            return BadRequest(ApiResponse.Error(500, "文档写入器不可用"));
        }

        // 构建写入操作列表
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
                // 避免与验收列重复写
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

        // 复制原文件并执行写入
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
                // 执行写入操作
                await writer.WriteTableDataAsync(resultStream, taskResult.SourceTableIndex, operations);
                resultContent = resultStream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "填充文档失败: {TaskId}", taskId);
                return BadRequest(ApiResponse.Error(500, $"填充文档失败: {ex.Message}"));
            }
        }

        // 落盘保存填充结果（文件系统存储）
        try
        {
            var relative = await _fileStorage.SaveFilledWordAsync(wordFile.FileName, resultContent);
            taskResult.FilledFilePath = relative;
        }
        catch (Exception ex)
        {
            // 不影响下载（只记录日志）
            _logger.LogWarning(ex, "保存填充结果到文件系统失败: {TaskId}", taskId);
        }

        // 写入操作历史（填充）
        try
        {
            var history = new OperationHistory
            {
                OperationType = OperationType.Fill,
                TargetFile = taskResult.FilledFilePath,
                Details = JsonSerializer.Serialize(new
                {
                    taskId,
                    sourceFileId = taskResult.SourceFileId,
                    sourceTableIndex = taskResult.SourceTableIndex,
                    filledCount = taskResult.FillResults.Count,
                    acceptanceColumnIndex = taskResult.AcceptanceColumnIndex,
                    remarkColumnIndex = taskResult.RemarkColumnIndex
                }),
                CanUndo = false,
                CreatedAt = DateTime.Now
            };
            await _unitOfWork.OperationHistories.AddAsync(history);
            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入填充操作历史失败: {TaskId}", taskId);
        }

        // 生成下载文件名
        var originalFileName = Path.GetFileNameWithoutExtension(wordFile.FileName);
        var downloadFileName = $"{originalFileName}_filled_{DateTime.Now:yyyyMMddHHmmss}.docx";

        _logger.LogInformation("下载填充结果: 任务{TaskId}, 文件{FileName}", taskId, downloadFileName);

        return File(resultContent, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", downloadFileName);
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

        return specs.Select(s => new MatchCandidate
        {
            SpecId = s.Id,
            Project = s.Project,
            Specification = s.Specification,
            Acceptance = s.Acceptance
        }).ToList();
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
            EmbeddingServiceId = dto.EmbeddingServiceId,
            LlmServiceId = dto.LlmServiceId,
            MinScoreThreshold = dto.MinScoreThreshold,
            UseLlmReview = dto.UseLlmReview,
            UseLlmSuggestion = dto.UseLlmSuggestion,
            LlmSuggestionScoreThreshold = dto.LlmSuggestionScoreThreshold
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
            Score = result.Score,
            ScoreDetails = result.ScoreDetails,
            LlmScore = result.LlmScore,
            LlmReason = result.LlmReason,
            LlmCommentary = result.LlmCommentary,
            IsLlmReviewed = result.IsLlmReviewed
        };
    }

    private async Task StreamLlmReviewAsync(MatchLlmStreamItem item, MatchingConfig config, CancellationToken cancellationToken)
    {
        var specId = item.BestMatchSpecId ?? 0;
        if (specId <= 0)
            return;

        var spec = await _unitOfWork.AcceptanceSpecs.GetByIdAsync(specId);
        if (spec == null)
        {
            await WriteSseEventAsync("review.error", new
            {
                rowIndex = item.RowIndex,
                message = "最佳匹配规格不存在"
            }, cancellationToken);
            return;
        }

        var reviewRequest = new LlmReviewRequest
        {
            SourceProject = item.SourceProject,
            SourceSpecification = item.SourceSpecification,
            BestMatchProject = spec.Project,
            BestMatchSpecification = spec.Specification,
            BestMatchAcceptance = spec.Acceptance,
            BaseScore = (item.BestMatchScore ?? 0) * 100,
            ScoreDetails = item.ScoreDetails ?? new Dictionary<string, double>(),
            LlmServiceId = config.LlmServiceId
        };

        await WriteSseEventAsync("review.start", new { rowIndex = item.RowIndex }, cancellationToken);

        var buffer = new StringBuilder();
        try
        {
            await foreach (var chunk in _llmReviewService.ReviewStreamAsync(reviewRequest, cancellationToken))
            {
                buffer.Append(chunk);
                await WriteSseEventAsync("review.delta", new
                {
                    rowIndex = item.RowIndex,
                    chunk
                }, cancellationToken);
            }

            if (_llmReviewService.TryParseReviewResult(buffer.ToString(), out var result))
            {
                await WriteSseEventAsync("review.done", new
                {
                    rowIndex = item.RowIndex,
                    score = result.Score,
                    reason = result.Reason,
                    commentary = result.Commentary
                }, cancellationToken);
            }
            else
            {
                await WriteSseEventAsync("review.error", new
                {
                    rowIndex = item.RowIndex,
                    message = "LLM复核输出解析失败"
                }, cancellationToken);
            }
        }
        catch (AiServiceUnavailableException ex)
        {
            _logger.LogWarning(ex, "LLM复核失败");
            await WriteSseEventAsync("review.error", new
            {
                rowIndex = item.RowIndex,
                message = ex.Reason
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM复核失败");
            await WriteSseEventAsync("review.error", new
            {
                rowIndex = item.RowIndex,
                message = "LLM复核失败"
            }, cancellationToken);
        }
    }

    private async Task StreamLlmSuggestionAsync(MatchLlmStreamItem item, MatchingConfig config, CancellationToken cancellationToken)
    {
        var request = new LlmSuggestionRequest
        {
            SourceProject = item.SourceProject,
            SourceSpecification = item.SourceSpecification,
            LlmServiceId = config.LlmServiceId
        };

        await WriteSseEventAsync("suggestion.start", new { rowIndex = item.RowIndex }, cancellationToken);

        var buffer = new StringBuilder();
        try
        {
            await foreach (var chunk in _llmSuggestionService.GenerateSuggestionStreamAsync(request, cancellationToken))
            {
                buffer.Append(chunk);
                await WriteSseEventAsync("suggestion.delta", new
                {
                    rowIndex = item.RowIndex,
                    chunk
                }, cancellationToken);
            }

            if (_llmSuggestionService.TryParseSuggestionResult(buffer.ToString(), out var result))
            {
                await WriteSseEventAsync("suggestion.done", new
                {
                    rowIndex = item.RowIndex,
                    acceptance = result.Acceptance,
                    remark = result.Remark,
                    reason = result.Reason
                }, cancellationToken);
            }
            else
            {
                await WriteSseEventAsync("suggestion.error", new
                {
                    rowIndex = item.RowIndex,
                    message = "LLM生成输出解析失败"
                }, cancellationToken);
            }
        }
        catch (AiServiceUnavailableException ex)
        {
            _logger.LogWarning(ex, "LLM生成建议失败");
            await WriteSseEventAsync("suggestion.error", new
            {
                rowIndex = item.RowIndex,
                message = ex.Reason
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM生成建议失败");
            await WriteSseEventAsync("suggestion.error", new
            {
                rowIndex = item.RowIndex,
                message = "LLM生成建议失败"
            }, cancellationToken);
        }
    }

    private async Task WriteSseEventAsync(string eventName, object data, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(data, SseJsonOptions);
        await Response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private async Task<List<MatchSourceItem>> ExtractMatchSourceItemsFromFileAsync(
        int fileId,
        int tableIndex,
        int projectColumnIndex,
        int specificationColumnIndex)
    {
        var wordFile = await _unitOfWork.WordFiles.GetByIdAsync(fileId);
        if (wordFile == null)
        {
            return [];
        }

        var parser = _documentServiceFactory.GetParser(DocumentType.Word);
        if (parser == null)
        {
            return [];
        }

        using var stream = OpenWordFileReadStream(wordFile);
        TableData tableData;
        try
        {
            tableData = await parser.ExtractTableDataAsync(stream, tableIndex, new ColumnMapping
            {
                HeaderRowIndex = 0,
                DataStartRowIndex = 1
            });
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

            if (string.IsNullOrWhiteSpace(project) && string.IsNullOrWhiteSpace(spec))
            {
                continue;
            }

            items.Add(new MatchSourceItem
            {
                RowIndex = row.Index,
                Project = project.Trim(),
                Specification = spec.Trim()
            });
        }

        return items;
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
    /// 清理过期的任务结果
    /// </summary>
    private static void CleanupExpiredTasks()
    {
        var expireTime = DateTime.Now.AddHours(-1);
        var expiredKeys = _fillTaskResults
            .Where(kvp => kvp.Value.CreatedAt < expireTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _fillTaskResults.TryRemove(key, out _);
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
