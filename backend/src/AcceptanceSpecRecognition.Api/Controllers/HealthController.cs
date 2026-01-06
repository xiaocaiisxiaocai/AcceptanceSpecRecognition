using Microsoft.AspNetCore.Mvc;
using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly IHealthCheckService _healthCheckService;
    private readonly IFeedbackLearningService _feedbackService;

    public HealthController(IHealthCheckService healthCheckService, IFeedbackLearningService feedbackService)
    {
        _healthCheckService = healthCheckService;
        _feedbackService = feedbackService;
    }

    /// <summary>
    /// 获取系统健康状态
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<HealthCheckResult>> GetHealth()
    {
        var result = await _healthCheckService.CheckAllAsync();
        
        if (result.Status == HealthStatus.Unhealthy)
        {
            return StatusCode(503, result);
        }
        
        return Ok(result);
    }

    /// <summary>
    /// 获取简单的健康检查（用于负载均衡器）
    /// </summary>
    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new { status = "ok", timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// 获取Embedding服务状态
    /// </summary>
    [HttpGet("embedding")]
    public async Task<ActionResult<ComponentHealth>> GetEmbeddingHealth()
    {
        var result = await _healthCheckService.CheckEmbeddingServiceAsync();
        return Ok(result);
    }

    /// <summary>
    /// 获取LLM服务状态
    /// </summary>
    [HttpGet("llm")]
    public async Task<ActionResult<ComponentHealth>> GetLLMHealth()
    {
        var result = await _healthCheckService.CheckLLMServiceAsync();
        return Ok(result);
    }

    /// <summary>
    /// 记录用户反馈
    /// </summary>
    [HttpPost("feedback")]
    public async Task<IActionResult> RecordFeedback([FromBody] UserFeedback feedback)
    {
        if (string.IsNullOrWhiteSpace(feedback.QueryText))
        {
            return BadRequest(new { error = "查询文本不能为空" });
        }

        await _feedbackService.RecordFeedbackAsync(feedback);
        return Ok(new { message = "反馈已记录", id = feedback.Id });
    }

    /// <summary>
    /// 获取同义词建议
    /// </summary>
    [HttpGet("suggestions/synonyms")]
    public async Task<ActionResult<List<SynonymSuggestion>>> GetSynonymSuggestions()
    {
        var suggestions = await _feedbackService.GenerateSynonymSuggestionsAsync();
        return Ok(suggestions);
    }

    /// <summary>
    /// 获取反馈统计
    /// </summary>
    [HttpGet("feedback/stats")]
    public async Task<ActionResult<FeedbackStatistics>> GetFeedbackStats()
    {
        var stats = await _feedbackService.GetStatisticsAsync();
        return Ok(stats);
    }
}
