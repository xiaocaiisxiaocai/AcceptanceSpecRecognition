using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecRecognition.Core.Services;

/// <summary>
/// 大模型服务实现 - 支持OpenAI兼容API，包含重试机制
/// </summary>
public class LLMService : ILLMService
{
    private readonly IConfigManager _configManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LLMService> _logger;

    public LLMService(
        IConfigManager configManager,
        IHttpClientFactory httpClientFactory,
        ICacheService cacheService,
        ILogger<LLMService> logger)
    {
        _configManager = configManager;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// 统一分析方法 - 一次调用完成冲突检测、语义等价分析、置信度评估
    /// </summary>
    public async Task<UnifiedAnalysisResult> AnalyzeUnifiedAsync(string query, string candidate)
    {
        var config = _configManager.GetAll();
        var llmConfig = config.LLM;

        // 获取API Key
        var apiKey = !string.IsNullOrEmpty(llmConfig.ApiKey)
            ? llmConfig.ApiKey
            : Environment.GetEnvironmentVariable("OPENAI_API_KEY")
              ?? Environment.GetEnvironmentVariable("LLM_API_KEY");

        // 如果没有API Key，使用规则 fallback
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogInformation("无 API Key，使用规则 fallback");
            return GetRuleBasedResult(query, candidate);
        }

        try
        {
            var prompt = BuildUnifiedPrompt(query, candidate);
            var response = await CallLLMAsync(prompt, llmConfig, apiKey);
            return ParseUnifiedResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM 统一分析失败，使用规则 fallback");
            return GetRuleBasedResult(query, candidate);
        }
    }

    /// <summary>
    /// 构建统一分析 Prompt（从配置读取模板）
    /// </summary>
    private string BuildUnifiedPrompt(string query, string candidate)
    {
        var config = _configManager.GetAll();
        var template = config.Prompts.UnifiedAnalysisPrompt;

        return template
            .Replace("{query}", query)
            .Replace("{candidate}", candidate);
    }

    /// <summary>
    /// 解析统一分析响应
    /// </summary>
    private UnifiedAnalysisResult ParseUnifiedResponse(string response)
    {
        try
        {
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var parsed = JsonSerializer.Deserialize<UnifiedAnalysisResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed != null)
                {
                    // 限制调整系数范围
                    var factor = Math.Clamp(parsed.ScoreAdjustmentFactor, 0.3f, 1.3f);

                    return new UnifiedAnalysisResult
                    {
                        HasConflict = parsed.HasConflict,
                        ConflictType = parsed.ConflictType ?? "none",
                        ConflictDescription = parsed.ConflictDescription ?? string.Empty,
                        IsEquivalent = parsed.IsEquivalent,
                        EquivalenceMappings = parsed.EquivalenceMappings?.Select(m => new EquivalenceMapping
                        {
                            QueryTerm = m.QueryTerm ?? string.Empty,
                            CandidateTerm = m.CandidateTerm ?? string.Empty,
                            Type = m.Type ?? string.Empty
                        }).ToList() ?? new List<EquivalenceMapping>(),
                        ScoreAdjustmentFactor = factor,
                        Confidence = parsed.Confidence,
                        Reasoning = parsed.Reasoning ?? string.Empty
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解析统一分析响应失败");
        }

        return GetDefaultResult();
    }

    /// <summary>
    /// 基于规则的 fallback 分析（无 LLM 时使用）
    /// </summary>
    private UnifiedAnalysisResult GetRuleBasedResult(string query, string candidate)
    {
        // DC/AC 冲突检查
        var queryHasDC = query.Contains("DC") || query.Contains("直流");
        var queryHasAC = query.Contains("AC") || query.Contains("交流");
        var candHasDC = candidate.Contains("DC") || candidate.Contains("直流");
        var candHasAC = candidate.Contains("AC") || candidate.Contains("交流");

        if ((queryHasDC && candHasAC) || (queryHasAC && candHasDC))
        {
            return new UnifiedAnalysisResult
            {
                HasConflict = true,
                ConflictType = "electrical",
                ConflictDescription = "DC/AC 电气类型冲突",
                IsEquivalent = false,
                ScoreAdjustmentFactor = 0.4f,
                Confidence = 0.3f,
                Reasoning = "DC/AC 互斥冲突"
            };
        }

        // 单相/三相冲突检查
        var queryHas1P = query.Contains("1P") || query.Contains("单相");
        var queryHas3P = query.Contains("3P") || query.Contains("三相");
        var candHas1P = candidate.Contains("1P") || candidate.Contains("单相");
        var candHas3P = candidate.Contains("3P") || candidate.Contains("三相");

        if ((queryHas1P && candHas3P) || (queryHas3P && candHas1P))
        {
            return new UnifiedAnalysisResult
            {
                HasConflict = true,
                ConflictType = "electrical",
                ConflictDescription = "单相/三相冲突",
                IsEquivalent = false,
                ScoreAdjustmentFactor = 0.4f,
                Confidence = 0.3f,
                Reasoning = "单相/三相互斥冲突"
            };
        }

        // NPN/PNP 冲突检查
        var queryHasNPN = query.Contains("NPN");
        var queryHasPNP = query.Contains("PNP");
        var candHasNPN = candidate.Contains("NPN");
        var candHasPNP = candidate.Contains("PNP");

        if ((queryHasNPN && candHasPNP) || (queryHasPNP && candHasNPN))
        {
            return new UnifiedAnalysisResult
            {
                HasConflict = true,
                ConflictType = "electrical",
                ConflictDescription = "NPN/PNP 类型冲突",
                IsEquivalent = false,
                ScoreAdjustmentFactor = 0.4f,
                Confidence = 0.3f,
                Reasoning = "NPN/PNP 互斥冲突"
            };
        }

        // 无冲突，返回默认结果
        return GetDefaultResult();
    }

    /// <summary>
    /// 默认分析结果
    /// </summary>
    private UnifiedAnalysisResult GetDefaultResult()
    {
        return new UnifiedAnalysisResult
        {
            HasConflict = false,
            ConflictType = "none",
            ConflictDescription = string.Empty,
            IsEquivalent = false,
            EquivalenceMappings = new List<EquivalenceMapping>(),
            ScoreAdjustmentFactor = 1.0f,
            Confidence = 0.5f,
            Reasoning = "无法进行 LLM 分析，使用默认结果"
        };
    }

    public LLMModelInfo GetModelInfo()
    {
        var config = _configManager.GetAll();
        return new LLMModelInfo
        {
            Name = config.LLM.Model,
            Provider = config.LLM.Provider
        };
    }

    #region 流式分析实现

    /// <summary>
    /// 流式分析 Prompt 模板 - 要求 LLM 输出结构化的思考步骤
    /// </summary>
    private const string StreamingAnalysisPromptTemplate = @"你是一个工业验收规范专家，精通中文工业术语、电气设备规格和品牌名称（包括中英文对照）。

## 任务
分析以下查询和候选匹配结果，输出详细的思考过程。

## 输入
- 项目：{query.Project}
- 技术指标：{query.TechnicalSpec}

候选结果：
{candidatesText}

## 输出要求
请严格按照以下格式逐步输出思考过程，每个步骤输出一个完整的 JSON 对象，每个 JSON 之间用 --- 分隔：

### 步骤1：属性提取
分析查询和候选中的关键属性（电压、电流、接口类型、品牌型号等）
---
{""step"": ""extract"", ""title"": ""属性提取"", ""content"": {""queryAttributes"": {""voltage"": ""..."", ""type"": ""..."", ""brand"": ""...""}, ""candidateAttributes"": [{""index"": 0, ""voltage"": ""..."", ""type"": ""..."", ""brand"": ""...""}]}}
---

### 步骤2：语义等价分析
**重点**：识别跨语言的品牌名等价关系（如 Omron=欧姆龙, Siemens=西门子, Schneider=施耐德, Mitsubishi=三菱, Panasonic=松下 等），以及技术术语同义词（如 PLC=可编程逻辑控制器）
---
{""step"": ""equivalence"", ""title"": ""语义等价分析"", ""content"": {""isEquivalent"": true, ""mappings"": [{""queryTerm"": ""Omron"", ""candidateTerm"": ""欧姆龙"", ""equivalenceType"": ""brand""}], ""reasoning"": ""Omron是欧姆龙的英文品牌名，两者完全等价""}}
---

### 步骤3：语义对比
逐项对比查询与候选的属性匹配情况
---
{""step"": ""compare"", ""title"": ""语义对比"", ""content"": {""comparisons"": [{""attribute"": ""电压"", ""query"": ""DC24V"", ""candidate"": ""DC24V"", ""result"": ""match""}, {""attribute"": ""品牌"", ""query"": ""Omron"", ""candidate"": ""欧姆龙"", ""result"": ""equivalent""}]}}
---

### 步骤4：冲突检测
检测是否存在 DC/AC、单相/三相、NPN/PNP 等互斥冲突
---
{""step"": ""conflict"", ""title"": ""冲突检测"", ""content"": {""hasConflict"": false, ""conflicts"": [], ""description"": ""未检测到冲突""}}
---

### 步骤5：置信度推理
基于匹配情况计算置信度，**特别注意**：如果发现品牌名等价关系，应该显著提高置信度
---
{""step"": ""confidence"", ""title"": ""置信度推理"", ""content"": {""factors"": [""品牌名完全等价(Omron=欧姆龙)"", ""型号系列匹配(CP系列)""], ""calculation"": ""品牌等价+型号匹配，置信度应高于阈值"", ""confidence"": 0.95, ""scoreAdjustmentFactor"": 1.2}}
---

### 步骤6：最终结论
输出最佳匹配索引（从0开始）和推理说明
---
{""step"": ""conclusion"", ""title"": ""最终结论"", ""content"": {""bestMatchIndex"": 0, ""reasoning"": ""查询中的Omron与候选中的欧姆龙是同一品牌，CP系列型号匹配"", ""adjustedScores"": [0.95, 0.72], ""llmConfirmed"": true}}
---

现在开始分析，按照上述格式逐步输出（注意：每个 JSON 必须完整，用 --- 分隔）：";

    /// <summary>
    /// 流式分析候选结果，输出详细思考过程
    /// </summary>
    public async IAsyncEnumerable<StreamingAnalysisEvent> AnalyzeMatchesStreamingAsync(
        MatchQuery query,
        List<MatchCandidate> candidates,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var config = _configManager.GetAll();
        var llmConfig = config.LLM;

        // 获取API Key
        var apiKey = !string.IsNullOrEmpty(llmConfig.ApiKey)
            ? llmConfig.ApiKey
            : Environment.GetEnvironmentVariable("OPENAI_API_KEY")
              ?? Environment.GetEnvironmentVariable("LLM_API_KEY");

        // 如果没有配置API Key或禁用LLM，直接返回结果
        if (string.IsNullOrEmpty(apiKey) || !config.Matching.EnableLLM)
        {
            _logger.LogInformation("LLM 未启用或无 API Key，返回默认结果");
            yield return new StreamingAnalysisEvent
            {
                EventType = "result",
                Result = GetDefaultStreamingResult(candidates)
            };
            yield return new StreamingAnalysisEvent { EventType = "done" };
            yield break;
        }

        var prompt = BuildStreamingAnalysisPrompt(query, candidates);
        var buffer = new StringBuilder();
        LLMAnalysisResult? finalResult = null;

        await foreach (var chunk in CallLLMStreamingAsync(prompt, llmConfig, apiKey, cancellationToken))
        {
            buffer.Append(chunk);

            // 尝试解析完整的思考步骤
            var events = TryParseThinkingSteps(buffer);
            foreach (var evt in events)
            {
                if (evt.ThinkingStep?.Step == "conclusion" && evt.ThinkingStep.Content != null)
                {
                    // 从 conclusion 步骤中提取最终结果
                    finalResult = ExtractResultFromConclusion(evt.ThinkingStep.Content, candidates);
                }
                yield return evt;
            }
        }

        // 输出最终结果
        yield return new StreamingAnalysisEvent
        {
            EventType = "result",
            Result = finalResult ?? GetDefaultStreamingResult(candidates)
        };

        yield return new StreamingAnalysisEvent { EventType = "done" };
    }

    /// <summary>
    /// 构建流式分析 Prompt
    /// </summary>
    private string BuildStreamingAnalysisPrompt(MatchQuery query, List<MatchCandidate> candidates)
    {
        var candidatesText = string.Join("\n", candidates.Select((c, i) =>
            $"{i}. 项目: {c.Record.Project}, 技术指标: {c.Record.TechnicalSpec}, " +
            $"实际规格: {c.Record.ActualSpec}, 相似度: {c.SimilarityScore:F3}"));

        return StreamingAnalysisPromptTemplate
            .Replace("{query.Project}", query.Project)
            .Replace("{query.TechnicalSpec}", query.TechnicalSpec)
            .Replace("{candidatesText}", candidatesText);
    }

    /// <summary>
    /// 调用 OpenAI 流式 API
    /// </summary>
    private async IAsyncEnumerable<string> CallLLMStreamingAsync(
        string prompt,
        LLMConfig llmConfig,
        string apiKey,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var baseUrl = llmConfig.BaseUrl.TrimEnd('/');
        var apiUrl = $"{baseUrl}/chat/completions";

        var httpClient = _httpClientFactory.CreateClient("LLMClient");
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, apiUrl);
        requestMessage.Headers.Add("Authorization", $"Bearer {apiKey}");
        requestMessage.Content = JsonContent.Create(new
        {
            model = llmConfig.Model,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            temperature = llmConfig.Temperature,
            max_tokens = llmConfig.MaxTokens,
            stream = true
        });

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "流式 LLM API 调用失败");
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line))
                continue;

            if (!line.StartsWith("data: "))
                continue;

            var data = line.Substring(6);
            if (data == "[DONE]")
                break;

            string? content = null;
            try
            {
                var chunk = JsonSerializer.Deserialize<OpenAIStreamingResponse>(data, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                content = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "解析流式响应失败: {Data}", data);
            }

            if (!string.IsNullOrEmpty(content))
            {
                yield return content;
            }
        }
    }

    /// <summary>
    /// 尝试从缓冲区解析思考步骤
    /// </summary>
    private List<StreamingAnalysisEvent> TryParseThinkingSteps(StringBuilder buffer)
    {
        var events = new List<StreamingAnalysisEvent>();
        var content = buffer.ToString();

        // 使用 --- 分隔符查找完整的 JSON 对象
        var parts = content.Split(new[] { "---" }, StringSplitOptions.RemoveEmptyEntries);

        // 保留最后一个可能不完整的部分
        var processedLength = 0;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i].Trim();
            processedLength += parts[i].Length + 3; // +3 for "---"

            if (string.IsNullOrWhiteSpace(part))
                continue;

            // 尝试提取 JSON
            var jsonStart = part.IndexOf('{');
            var jsonEnd = part.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = part.Substring(jsonStart, jsonEnd - jsonStart + 1);
                try
                {
                    var step = JsonSerializer.Deserialize<ThinkingStep>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (step != null && !string.IsNullOrEmpty(step.Step))
                    {
                        step.Timestamp = DateTime.UtcNow;
                        events.Add(new StreamingAnalysisEvent
                        {
                            EventType = "thinking",
                            ThinkingStep = step
                        });
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogDebug(ex, "解析思考步骤失败: {Json}", json);
                }
            }
        }

        // 清除已处理的内容
        if (events.Count > 0 && processedLength > 0)
        {
            buffer.Remove(0, Math.Min(processedLength, buffer.Length));
        }

        return events;
    }

    /// <summary>
    /// 从 conclusion 步骤中提取最终结果
    /// </summary>
    private LLMAnalysisResult? ExtractResultFromConclusion(object content, List<MatchCandidate> candidates)
    {
        try
        {
            var json = JsonSerializer.Serialize(content);
            var conclusion = JsonSerializer.Deserialize<ConclusionContent>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (conclusion != null)
            {
                return new LLMAnalysisResult
                {
                    BestMatchIndex = conclusion.BestMatchIndex,
                    Confidence = conclusion.AdjustedScores?.FirstOrDefault() ?? candidates.FirstOrDefault()?.SimilarityScore ?? 0,
                    Reasoning = conclusion.Reasoning ?? "基于流式分析",
                    Conflicts = new List<ConflictInfo>(),
                    AdjustedScores = conclusion.AdjustedScores ?? candidates.Select(c => c.SimilarityScore).ToList()
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "提取 conclusion 结果失败");
        }

        return null;
    }

    /// <summary>
    /// 默认流式分析结果
    /// </summary>
    private LLMAnalysisResult GetDefaultStreamingResult(List<MatchCandidate> candidates)
    {
        var bestIndex = 0;
        var bestScore = 0f;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].SimilarityScore > bestScore)
            {
                bestScore = candidates[i].SimilarityScore;
                bestIndex = i;
            }
        }

        return new LLMAnalysisResult
        {
            BestMatchIndex = bestIndex,
            Confidence = bestScore,
            Reasoning = "基于Embedding相似度选择",
            Conflicts = new List<ConflictInfo>(),
            AdjustedScores = candidates.Select(c => c.SimilarityScore).ToList()
        };
    }

    #endregion

    #region LLM API 调用

    /// <summary>
    /// 调用 LLM API（非流式）
    /// </summary>
    private async Task<string> CallLLMAsync(string prompt, LLMConfig llmConfig, string apiKey)
    {
        var config = _configManager.GetAll();
        var maxRetries = config.Batch.ApiMaxRetries;
        var baseDelayMs = config.Batch.ApiRetryBaseDelayMs;

        var baseUrl = llmConfig.BaseUrl.TrimEnd('/');
        var apiUrl = $"{baseUrl}/chat/completions";
        Exception? lastException = null;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient("LLMClient");

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                requestMessage.Headers.Add("Authorization", $"Bearer {apiKey}");
                requestMessage.Content = JsonContent.Create(new
                {
                    model = llmConfig.Model,
                    messages = new[]
                    {
                        new { role = "user", content = prompt }
                    },
                    temperature = llmConfig.Temperature,
                    max_tokens = llmConfig.MaxTokens
                });

                var response = await httpClient.SendAsync(requestMessage);

                // 对于可重试的状态码进行重试
                if (IsRetryableStatusCode(response.StatusCode) && attempt < maxRetries)
                {
                    var delayMs = baseDelayMs * (int)Math.Pow(2, attempt);
                    _logger.LogWarning("LLM API 返回 {StatusCode}，第 {Attempt} 次重试...",
                        response.StatusCode, attempt + 1);
                    await Task.Delay(delayMs);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<OpenAIChatResponse>(new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                return result?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                lastException = ex;
                var delayMs = baseDelayMs * (int)Math.Pow(2, attempt);
                _logger.LogWarning(ex, "LLM API 网络错误，第 {Attempt} 次重试...", attempt + 1);
                await Task.Delay(delayMs);
            }
            catch (TaskCanceledException ex) when (attempt < maxRetries)
            {
                lastException = ex;
                var delayMs = baseDelayMs * (int)Math.Pow(2, attempt);
                _logger.LogWarning("LLM API 超时，第 {Attempt} 次重试...", attempt + 1);
                await Task.Delay(delayMs);
            }
            catch (Exception ex)
            {
                lastException = ex;
                break;
            }
        }

        _logger.LogWarning(lastException, "LLM API 调用失败（已重试 {MaxRetries} 次）", maxRetries);
        throw lastException ?? new Exception("LLM API 调用失败");
    }

    /// <summary>
    /// 判断是否为可重试的 HTTP 状态码
    /// </summary>
    private static bool IsRetryableStatusCode(System.Net.HttpStatusCode statusCode)
    {
        return statusCode == System.Net.HttpStatusCode.TooManyRequests ||
               statusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
               statusCode == System.Net.HttpStatusCode.GatewayTimeout ||
               statusCode == System.Net.HttpStatusCode.InternalServerError;
    }

    #endregion

    #region 内部类型定义

    /// <summary>
    /// 统一分析响应（用于 JSON 反序列化）
    /// </summary>
    private class UnifiedAnalysisResponse
    {
        [JsonPropertyName("hasConflict")]
        public bool HasConflict { get; set; }

        [JsonPropertyName("conflictType")]
        public string? ConflictType { get; set; }

        [JsonPropertyName("conflictDescription")]
        public string? ConflictDescription { get; set; }

        [JsonPropertyName("isEquivalent")]
        public bool IsEquivalent { get; set; }

        [JsonPropertyName("equivalenceMappings")]
        public List<EquivalenceMappingResponse>? EquivalenceMappings { get; set; }

        [JsonPropertyName("scoreAdjustmentFactor")]
        public float ScoreAdjustmentFactor { get; set; } = 1.0f;

        [JsonPropertyName("confidence")]
        public float Confidence { get; set; }

        [JsonPropertyName("reasoning")]
        public string? Reasoning { get; set; }
    }

    private class EquivalenceMappingResponse
    {
        [JsonPropertyName("queryTerm")]
        public string? QueryTerm { get; set; }

        [JsonPropertyName("candidateTerm")]
        public string? CandidateTerm { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    private class ConclusionContent
    {
        [JsonPropertyName("bestMatchIndex")]
        public int BestMatchIndex { get; set; }

        [JsonPropertyName("reasoning")]
        public string? Reasoning { get; set; }

        [JsonPropertyName("adjustedScores")]
        public List<float>? AdjustedScores { get; set; }
    }

    private class OpenAIStreamingResponse
    {
        [JsonPropertyName("choices")]
        public List<StreamingChoice>? Choices { get; set; }
    }

    private class StreamingChoice
    {
        [JsonPropertyName("delta")]
        public StreamingDelta? Delta { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    private class StreamingDelta
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    private class OpenAIChatResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("usage")]
        public ChatUsageInfo? Usage { get; set; }
    }

    private class Choice
    {
        [JsonPropertyName("message")]
        public Message? Message { get; set; }

        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    private class Message
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    private class ChatUsageInfo
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }

    #endregion
}
