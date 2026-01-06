using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MatchController : ControllerBase
{
    private readonly IMatchingEngine _matchingEngine;
    private readonly IBatchProcessor _batchProcessor;
    private readonly IAuditLogger _auditLogger;
    private readonly IConfigManager _configManager;
    private readonly ILLMService _llmService;
    private readonly ILogger<MatchController> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }  // 枚举序列化为字符串
    };

    public MatchController(
        IMatchingEngine matchingEngine,
        IBatchProcessor batchProcessor,
        IAuditLogger auditLogger,
        IConfigManager configManager,
        ILLMService llmService,
        ILogger<MatchController> logger)
    {
        _matchingEngine = matchingEngine;
        _batchProcessor = batchProcessor;
        _auditLogger = auditLogger;
        _configManager = configManager;
        _llmService = llmService;
        _logger = logger;
    }

    /// <summary>
    /// 单条匹配查询
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<MatchResult>> Match([FromBody] MatchQuery query)
    {
        var config = _configManager.GetAll();
        var maxTextLength = config.Batch.MaxTextLength;

        if (string.IsNullOrWhiteSpace(query.Project) && string.IsNullOrWhiteSpace(query.TechnicalSpec))
        {
            return BadRequest("项目名称或技术指标至少需要填写一项");
        }

        // 输入长度校验
        if (query.Project?.Length > maxTextLength)
        {
            return BadRequest($"项目名称长度不能超过 {maxTextLength} 字符");
        }
        if (query.TechnicalSpec?.Length > maxTextLength)
        {
            return BadRequest($"技术指标长度不能超过 {maxTextLength} 字符");
        }

        var result = await _matchingEngine.MatchAsync(query);

        await _auditLogger.LogQueryAsync(new QueryLogEntry
        {
            QueryText = $"{query.Project} {query.TechnicalSpec}".Trim(),
            ResultCount = result.BestMatch != null ? 1 : 0,
            TopScore = result.SimilarityScore,
            Confidence = result.Confidence.ToString(),
            MatchMode = result.MatchMode,
            DurationMs = result.DurationMs
        });

        return Ok(result);
    }

    /// <summary>
    /// 批量匹配查询
    /// </summary>
    [HttpPost("batch")]
    public async Task<ActionResult<BatchResult>> MatchBatch([FromBody] BatchRequest request)
    {
        var config = _configManager.GetAll();
        var maxBatchSize = config.Batch.MaxBatchSize;
        var maxTextLength = config.Batch.MaxTextLength;

        if (request.Queries == null || request.Queries.Count == 0)
        {
            return BadRequest("查询列表不能为空");
        }

        // 批量大小校验
        if (request.Queries.Count > maxBatchSize)
        {
            return BadRequest($"批量查询数量不能超过 {maxBatchSize} 条");
        }

        // 逐条校验输入长度
        for (int i = 0; i < request.Queries.Count; i++)
        {
            var q = request.Queries[i];
            if (q.Project?.Length > maxTextLength || q.TechnicalSpec?.Length > maxTextLength)
            {
                return BadRequest($"第 {i + 1} 条查询的输入长度超过限制（最大 {maxTextLength} 字符）");
            }
        }

        var result = await _batchProcessor.ProcessBatchAsync(request);

        // 记录批量匹配的审计日志
        foreach (var matchResult in result.Results)
        {
            await _auditLogger.LogQueryAsync(new QueryLogEntry
            {
                QueryText = $"{matchResult.Query.Project} {matchResult.Query.TechnicalSpec}".Trim(),
                ResultCount = matchResult.BestMatch != null ? 1 : 0,
                TopScore = matchResult.SimilarityScore,
                Confidence = matchResult.Confidence.ToString(),
                MatchMode = matchResult.MatchMode,
                DurationMs = matchResult.DurationMs
            });
        }

        return Ok(result);
    }

    /// <summary>
    /// 获取批量处理进度
    /// </summary>
    [HttpGet("batch/{taskId}/progress")]
    public ActionResult<BatchProgress> GetBatchProgress(string taskId)
    {
        var progress = _batchProcessor.GetProgress(taskId);
        if (progress == null)
        {
            return NotFound("任务不存在");
        }
        return Ok(progress);
    }

    /// <summary>
    /// 取消批量处理任务
    /// </summary>
    [HttpPost("batch/{taskId}/cancel")]
    public ActionResult CancelBatch(string taskId)
    {
        var success = _batchProcessor.CancelTask(taskId);
        if (!success)
        {
            return NotFound("任务不存在或已完成");
        }
        return Ok(new { message = "任务已取消" });
    }

    /// <summary>
    /// 确认匹配结果
    /// </summary>
    [HttpPost("confirm")]
    public async Task<ActionResult> ConfirmMatch([FromBody] ConfirmMatchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RecordId))
        {
            return BadRequest("记录ID不能为空");
        }

        await _auditLogger.LogUserActionAsync(new UserActionLogEntry
        {
            Action = request.Accepted ? "confirm_match" : "reject_match",
            RecordId = request.RecordId,
            Details = request.Feedback ?? ""
        });

        return Ok(new { message = "确认成功" });
    }

    /// <summary>
    /// 流式匹配查询（SSE）- 实时输出 LLM 思考过程
    /// </summary>
    [HttpPost("stream")]
    public async Task MatchStream([FromBody] MatchQuery query, CancellationToken cancellationToken)
    {
        var config = _configManager.GetAll();
        var maxTextLength = config.Batch.MaxTextLength;
        var startTime = DateTime.UtcNow;

        // 设置 SSE 响应头
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");
        Response.Headers.Append("X-Accel-Buffering", "no"); // 禁用 nginx 缓冲

        try
        {
            // 输入校验
            if (string.IsNullOrWhiteSpace(query.Project) && string.IsNullOrWhiteSpace(query.TechnicalSpec))
            {
                await SendEventAsync("error", new { error = "项目名称或技术指标至少需要填写一项" });
                return;
            }

            if (query.Project?.Length > maxTextLength || query.TechnicalSpec?.Length > maxTextLength)
            {
                await SendEventAsync("error", new { error = $"输入长度不能超过 {maxTextLength} 字符" });
                return;
            }

            // 阶段1: 预处理和 Embedding 匹配
            await SendEventAsync("status", new { stage = "preprocessing", message = "正在预处理文本..." });

            var matchResult = await _matchingEngine.MatchAsync(query);

            // 发送预处理完成事件
            var preprocessResult = new PreprocessMatchResult
            {
                PreprocessedText = $"{query.Project} {query.TechnicalSpec}".Trim(),
                BestMatch = matchResult.BestMatch,
                BestScore = matchResult.SimilarityScore,
                Candidates = matchResult.BestMatch != null
                    ? new List<MatchCandidate> { matchResult.BestMatch }
                    : new List<MatchCandidate>()
            };
            await SendEventAsync("preprocess", preprocessResult);

            // 阶段2: LLM 流式分析（如果启用且有候选结果）
            if (config.Matching.EnableLLM && matchResult.BestMatch != null)
            {
                await SendEventAsync("status", new { stage = "thinking", message = "AI 正在分析..." });

                var candidates = new List<MatchCandidate> { matchResult.BestMatch };

                await foreach (var evt in _llmService.AnalyzeMatchesStreamingAsync(query, candidates, cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    await SendEventAsync(evt.EventType, evt);
                }
            }

            // 阶段3: 发送最终结果
            var durationMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            matchResult.DurationMs = durationMs;
            matchResult.MatchMode = config.Matching.EnableLLM ? "LLM+Embedding" : "Embedding";

            await SendEventAsync("result", matchResult);

            // 记录审计日志
            await _auditLogger.LogQueryAsync(new QueryLogEntry
            {
                QueryText = $"{query.Project} {query.TechnicalSpec}".Trim(),
                ResultCount = matchResult.BestMatch != null ? 1 : 0,
                TopScore = matchResult.SimilarityScore,
                Confidence = matchResult.Confidence.ToString(),
                MatchMode = matchResult.MatchMode,
                DurationMs = durationMs
            });

            // 发送完成事件
            await SendEventAsync("done", new { durationMs });
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("流式匹配请求被取消");
            await SendEventAsync("error", new { error = "请求已取消" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "流式匹配发生错误");
            await SendEventAsync("error", new { error = ex.Message });
        }
    }

    /// <summary>
    /// 发送 SSE 事件
    /// </summary>
    private async Task SendEventAsync<T>(string eventType, T data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await Response.WriteAsync($"event: {eventType}\n");
        await Response.WriteAsync($"data: {json}\n\n");
        await Response.Body.FlushAsync();
    }
}

public class ConfirmMatchRequest
{
    public string RecordId { get; set; } = "";
    public bool Accepted { get; set; }
    public string? Feedback { get; set; }
    public string? CorrectedActualSpec { get; set; }
    public string? CorrectedRemark { get; set; }
}
