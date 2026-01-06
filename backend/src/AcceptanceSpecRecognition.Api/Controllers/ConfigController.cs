using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AcceptanceSpecRecognition.Core.Interfaces;
using AcceptanceSpecRecognition.Core.Models;

namespace AcceptanceSpecRecognition.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigController : ControllerBase
{
    private readonly IConfigManager _configManager;
    private readonly IJsonStorageService _storage;
    private readonly IAuditLogger _auditLogger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICacheService _cacheService;

    private readonly string _keywordsPath = "./data/keywords.json";
    private readonly string _typoCorrectionsPath = "./data/typo_corrections.json";
    private readonly string _unitMappingsPath = "./data/unit_mappings.json";

    public ConfigController(
        IConfigManager configManager,
        IJsonStorageService storage,
        IAuditLogger auditLogger,
        IHttpClientFactory httpClientFactory,
        ICacheService cacheService)
    {
        _configManager = configManager;
        _storage = storage;
        _auditLogger = auditLogger;
        _httpClientFactory = httpClientFactory;
        _cacheService = cacheService;
    }

    /// <summary>
    /// 获取系统配置
    /// </summary>
    [HttpGet]
    public ActionResult<SystemConfig> GetConfig()
    {
        var config = _configManager.GetAll();
        
        // 创建响应对象，直接返回完整配置（包含 API Key）
        var response = new
        {
            version = config.Version,
            embedding = new
            {
                model = config.Embedding.Model,
                provider = config.Embedding.Provider,
                dimension = config.Embedding.Dimension,
                dimensions = config.Embedding.Dimensions,
                batchSize = config.Embedding.BatchSize,
                primaryLanguage = config.Embedding.PrimaryLanguage,
                mixedLanguageSupport = config.Embedding.MixedLanguageSupport,
                baseUrl = config.Embedding.BaseUrl,
                apiKey = config.Embedding.ApiKey ?? ""
            },
            llm = new
            {
                model = config.LLM.Model,
                provider = config.LLM.Provider,
                enableConflictCheck = config.LLM.EnableConflictCheck,
                enableScoreAdjustment = config.LLM.EnableScoreAdjustment,
                timeoutSeconds = config.LLM.TimeoutSeconds,
                maxTokens = config.LLM.MaxTokens,
                temperature = config.LLM.Temperature,
                baseUrl = config.LLM.BaseUrl,
                apiKey = config.LLM.ApiKey ?? ""
            },
            matching = config.Matching,
            highlighting = config.Highlighting,
            preprocessing = config.Preprocessing,
            batch = config.Batch,
            cache = config.Cache,
            updatedAt = config.UpdatedAt
        };
        
        return Ok(response);
    }

    /// <summary>
    /// 更新系统配置
    /// </summary>
    [HttpPut]
    public async Task<ActionResult> UpdateConfig([FromBody] UpdateConfigRequest request)
    {
        var currentConfig = _configManager.GetAll();
        
        if (request.Matching != null)
        {
            await _configManager.UpdateMatchingConfigAsync(request.Matching);
        }

        if (request.Preprocessing != null)
        {
            await _configManager.UpdatePreprocessingConfigAsync(request.Preprocessing);
        }

        if (request.Embedding != null)
        {
            // 合并配置，只更新前端传递的字段
            var embeddingConfig = currentConfig.Embedding;
            
            // 更新 BaseUrl
            if (!string.IsNullOrEmpty(request.Embedding.BaseUrl))
            {
                embeddingConfig.BaseUrl = request.Embedding.BaseUrl;
            }
            
            // 更新 Model
            if (!string.IsNullOrEmpty(request.Embedding.Model))
            {
                embeddingConfig.Model = request.Embedding.Model;
            }
            
            // 更新 ApiKey（只有当不是 *** 且不为空时才更新）
            if (!string.IsNullOrEmpty(request.Embedding.ApiKey) && request.Embedding.ApiKey != "***")
            {
                embeddingConfig.ApiKey = request.Embedding.ApiKey;
            }
            
            // 更新 Dimension
            if (request.Embedding.Dimension > 0)
            {
                embeddingConfig.Dimension = request.Embedding.Dimension;
                embeddingConfig.Dimensions = request.Embedding.Dimension;
            }
            
            currentConfig.Embedding = embeddingConfig;
            await _configManager.UpdateConfigAsync(currentConfig);
        }

        if (request.LLM != null)
        {
            // 合并配置，只更新前端传递的字段
            var llmConfig = currentConfig.LLM;
            
            // 更新 BaseUrl
            if (!string.IsNullOrEmpty(request.LLM.BaseUrl))
            {
                llmConfig.BaseUrl = request.LLM.BaseUrl;
            }
            
            // 更新 Model
            if (!string.IsNullOrEmpty(request.LLM.Model))
            {
                llmConfig.Model = request.LLM.Model;
            }
            
            // 更新 ApiKey（只有当不是 *** 且不为空时才更新）
            if (!string.IsNullOrEmpty(request.LLM.ApiKey) && request.LLM.ApiKey != "***")
            {
                llmConfig.ApiKey = request.LLM.ApiKey;
            }
            
            // 更新 Temperature
            if (request.LLM.Temperature > 0)
            {
                llmConfig.Temperature = request.LLM.Temperature;
            }
            
            // 更新 MaxTokens
            if (request.LLM.MaxTokens > 0)
            {
                llmConfig.MaxTokens = request.LLM.MaxTokens;
            }
            
            currentConfig.LLM = llmConfig;
            await _configManager.UpdateConfigAsync(currentConfig);
        }

        if (request.Cache != null)
        {
            // 检查是否有缓存被禁用，需要清除对应缓存
            var oldConfig = currentConfig.Cache;
            var needsClearCache = false;

            // 如果任何缓存从启用变为禁用，需要清除缓存
            if ((oldConfig.EnableVectorCache && !request.Cache.EnableVectorCache) ||
                (oldConfig.EnableLLMCache && !request.Cache.EnableLLMCache) ||
                (oldConfig.EnableResultCache && !request.Cache.EnableResultCache))
            {
                needsClearCache = true;
            }

            // 更新缓存配置
            currentConfig.Cache.EnableVectorCache = request.Cache.EnableVectorCache;
            currentConfig.Cache.EnableLLMCache = request.Cache.EnableLLMCache;
            currentConfig.Cache.EnableResultCache = request.Cache.EnableResultCache;

            if (request.Cache.VectorCacheTtlMinutes > 0)
            {
                currentConfig.Cache.VectorCacheTtlMinutes = request.Cache.VectorCacheTtlMinutes;
            }
            if (request.Cache.LLMCacheTtlMinutes > 0)
            {
                currentConfig.Cache.LLMCacheTtlMinutes = request.Cache.LLMCacheTtlMinutes;
            }
            if (request.Cache.ResultCacheTtlMinutes > 0)
            {
                currentConfig.Cache.ResultCacheTtlMinutes = request.Cache.ResultCacheTtlMinutes;
            }

            await _configManager.UpdateConfigAsync(currentConfig);

            // 如果有缓存被禁用，自动清除所有缓存
            if (needsClearCache)
            {
                await _cacheService.ClearAllAsync();
            }
        }

        await _auditLogger.LogConfigChangeAsync(new ConfigChangeLogEntry
        {
            ConfigSection = "system",
            Changes = "配置已更新"
        });

        return Ok(new { message = "配置更新成功" });
    }

    /// <summary>
    /// 获取关键字库
    /// </summary>
    [HttpGet("keywords")]
    public async Task<ActionResult<KeywordLibrary>> GetKeywords()
    {
        var keywords = await _storage.ReadAsync<KeywordLibrary>(_keywordsPath);
        return Ok(keywords ?? new KeywordLibrary());
    }

    /// <summary>
    /// 更新关键字库
    /// </summary>
    [HttpPut("keywords")]
    public async Task<ActionResult> UpdateKeywords([FromBody] KeywordLibrary keywords)
    {
        keywords.UpdatedAt = DateTime.UtcNow;
        await _storage.WriteAsync(_keywordsPath, keywords);

        await _auditLogger.LogConfigChangeAsync(new ConfigChangeLogEntry
        {
            ConfigSection = "keywords",
            Changes = $"关键字库已更新，共 {keywords.Keywords.Count} 个关键字"
        });

        return Ok(new { message = "关键字库更新成功" });
    }

    /// <summary>
    /// 清除所有缓存
    /// </summary>
    [HttpDelete("cache")]
    public async Task<ActionResult> ClearCache()
    {
        await _cacheService.ClearAllAsync();
        var stats = _cacheService.GetStatistics();

        await _auditLogger.LogConfigChangeAsync(new ConfigChangeLogEntry
        {
            ConfigSection = "cache",
            Changes = "缓存已清除"
        });

        return Ok(new {
            message = "缓存已清除",
            statistics = stats
        });
    }

    /// <summary>
    /// 获取缓存统计信息
    /// </summary>
    [HttpGet("cache/stats")]
    public ActionResult GetCacheStats()
    {
        var stats = _cacheService.GetStatistics();
        return Ok(stats);
    }

    /// <summary>
    /// 获取错别字映射
    /// </summary>
    [HttpGet("typos")]
    public async Task<ActionResult<TypoCorrectionLibrary>> GetTypoCorrections()
    {
        var typos = await _storage.ReadAsync<TypoCorrectionLibrary>(_typoCorrectionsPath);
        return Ok(typos ?? new TypoCorrectionLibrary());
    }

    /// <summary>
    /// 更新错别字映射
    /// </summary>
    [HttpPut("typos")]
    public async Task<ActionResult> UpdateTypoCorrections([FromBody] TypoCorrectionLibrary typos)
    {
        typos.UpdatedAt = DateTime.UtcNow;
        await _storage.WriteAsync(_typoCorrectionsPath, typos);

        await _auditLogger.LogConfigChangeAsync(new ConfigChangeLogEntry
        {
            ConfigSection = "typos",
            Changes = $"错别字映射已更新，共 {typos.Corrections.Count} 条"
        });

        return Ok(new { message = "错别字映射更新成功" });
    }

    /// <summary>
    /// 获取单位映射（只读）
    /// </summary>
    [HttpGet("units")]
    public async Task<ActionResult<UnitMappingLibrary>> GetUnitMappings()
    {
        var units = await _storage.ReadAsync<UnitMappingLibrary>(_unitMappingsPath);
        return Ok(units ?? new UnitMappingLibrary());
    }

    /// <summary>
    /// 测试Embedding配置连接
    /// </summary>
    [HttpPost("test/embedding")]
    public async Task<ActionResult<TestConnectionResult>> TestEmbeddingConnection([FromBody] TestEmbeddingRequest request)
    {
        try
        {
            var baseUrl = request.BaseUrl.TrimEnd('/');
            var apiUrl = $"{baseUrl}/embeddings";

            // 如果没有传递 ApiKey，使用已保存的配置
            var apiKey = request.ApiKey;
            if (string.IsNullOrEmpty(apiKey) || apiKey == "***")
            {
                var currentConfig = _configManager.GetAll();
                apiKey = currentConfig.Embedding.ApiKey;
            }

            // 使用HttpClientFactory创建HttpClient
            var httpClient = _httpClientFactory.CreateClient("ConfigTestClient");

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            if (!string.IsNullOrEmpty(apiKey))
            {
                requestMessage.Headers.Add("Authorization", $"Bearer {apiKey}");
            }
            requestMessage.Content = JsonContent.Create(new
            {
                model = request.Model,
                input = "测试连接"
            });

            var response = await httpClient.SendAsync(requestMessage);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<EmbeddingTestResponse>();
                var dimension = result?.Data?.FirstOrDefault()?.Embedding?.Length ?? 0;
                
                return Ok(new TestConnectionResult
                {
                    Success = true,
                    Message = $"连接成功！模型: {result?.Model ?? request.Model}, 向量维度: {dimension}",
                    Details = new Dictionary<string, object>
                    {
                        { "model", result?.Model ?? request.Model },
                        { "dimension", dimension },
                        { "usage", result?.Usage ?? new object() }
                    }
                });
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return Ok(new TestConnectionResult
                {
                    Success = false,
                    Message = $"连接失败: HTTP {(int)response.StatusCode} - {response.ReasonPhrase}",
                    Details = new Dictionary<string, object>
                    {
                        { "statusCode", (int)response.StatusCode },
                        { "error", errorContent }
                    }
                });
            }
        }
        catch (HttpRequestException ex)
        {
            return Ok(new TestConnectionResult
            {
                Success = false,
                Message = $"网络错误: {ex.Message}"
            });
        }
        catch (TaskCanceledException)
        {
            return Ok(new TestConnectionResult
            {
                Success = false,
                Message = "请求超时，请检查API地址是否正确"
            });
        }
        catch (Exception ex)
        {
            return Ok(new TestConnectionResult
            {
                Success = false,
                Message = $"测试失败: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// 测试LLM配置连接
    /// </summary>
    [HttpPost("test/llm")]
    public async Task<ActionResult<TestConnectionResult>> TestLLMConnection([FromBody] TestLLMRequest request)
    {
        try
        {
            var baseUrl = request.BaseUrl.TrimEnd('/');
            var apiUrl = $"{baseUrl}/chat/completions";

            // 如果没有传递 ApiKey，使用已保存的配置
            var apiKey = request.ApiKey;
            if (string.IsNullOrEmpty(apiKey) || apiKey == "***")
            {
                var currentConfig = _configManager.GetAll();
                apiKey = currentConfig.LLM.ApiKey;
            }

            // 使用HttpClientFactory创建HttpClient
            var httpClient = _httpClientFactory.CreateClient("ConfigTestClient");

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            if (!string.IsNullOrEmpty(apiKey))
            {
                requestMessage.Headers.Add("Authorization", $"Bearer {apiKey}");
            }
            requestMessage.Content = JsonContent.Create(new
            {
                model = request.Model,
                messages = new[]
                {
                    new { role = "user", content = "请回复：连接测试成功" }
                },
                temperature = request.Temperature ?? 0.1,
                max_tokens = 50
            });

            var response = await httpClient.SendAsync(requestMessage);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<LLMTestResponse>();
                var content = result?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
                
                return Ok(new TestConnectionResult
                {
                    Success = true,
                    Message = $"连接成功！模型: {result?.Model ?? request.Model}",
                    Details = new Dictionary<string, object>
                    {
                        { "model", result?.Model ?? request.Model },
                        { "response", content },
                        { "usage", result?.Usage ?? new object() }
                    }
                });
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return Ok(new TestConnectionResult
                {
                    Success = false,
                    Message = $"连接失败: HTTP {(int)response.StatusCode} - {response.ReasonPhrase}",
                    Details = new Dictionary<string, object>
                    {
                        { "statusCode", (int)response.StatusCode },
                        { "error", errorContent }
                    }
                });
            }
        }
        catch (HttpRequestException ex)
        {
            return Ok(new TestConnectionResult
            {
                Success = false,
                Message = $"网络错误: {ex.Message}"
            });
        }
        catch (TaskCanceledException)
        {
            return Ok(new TestConnectionResult
            {
                Success = false,
                Message = "请求超时，请检查API地址是否正确"
            });
        }
        catch (Exception ex)
        {
            return Ok(new TestConnectionResult
            {
                Success = false,
                Message = $"测试失败: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// 获取系统提示词配置
    /// </summary>
    [HttpGet("prompts")]
    public ActionResult<PromptConfig> GetPrompts()
    {
        var config = _configManager.GetAll();
        return Ok(config.Prompts);
    }

    /// <summary>
    /// 更新系统提示词配置
    /// </summary>
    [HttpPut("prompts")]
    public async Task<ActionResult> UpdatePrompts([FromBody] UpdatePromptsRequest request)
    {
        var currentConfig = _configManager.GetAll();

        if (!string.IsNullOrEmpty(request.UnifiedAnalysisPrompt))
        {
            currentConfig.Prompts.UnifiedAnalysisPrompt = request.UnifiedAnalysisPrompt;
        }

        currentConfig.Prompts.UpdatedAt = DateTime.UtcNow;
        await _configManager.UpdateConfigAsync(currentConfig);

        await _auditLogger.LogConfigChangeAsync(new ConfigChangeLogEntry
        {
            ConfigSection = "prompts",
            Changes = "系统提示词已更新"
        });

        return Ok(new { message = "提示词更新成功" });
    }

    /// <summary>
    /// 重置提示词为默认值
    /// </summary>
    [HttpPost("prompts/reset")]
    public async Task<ActionResult> ResetPrompts()
    {
        var currentConfig = _configManager.GetAll();
        currentConfig.Prompts = new PromptConfig();
        currentConfig.Prompts.UpdatedAt = DateTime.UtcNow;
        await _configManager.UpdateConfigAsync(currentConfig);

        await _auditLogger.LogConfigChangeAsync(new ConfigChangeLogEntry
        {
            ConfigSection = "prompts",
            Changes = "系统提示词已重置为默认值"
        });

        return Ok(new { message = "提示词已重置为默认值" });
    }
}

public class UpdateConfigRequest
{
    public MatchingConfig? Matching { get; set; }
    public PreprocessingConfig? Preprocessing { get; set; }
    public EmbeddingConfig? Embedding { get; set; }
    public LLMConfig? LLM { get; set; }
    public CacheConfig? Cache { get; set; }
}

public class TestEmbeddingRequest
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}

public class TestLLMRequest
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public float? Temperature { get; set; }
}

public class UpdatePromptsRequest
{
    public string? UnifiedAnalysisPrompt { get; set; }
}

public class TestConnectionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object>? Details { get; set; }
}

// Embedding测试响应模型
public class EmbeddingTestResponse
{
    [JsonPropertyName("data")]
    public List<EmbeddingTestData>? Data { get; set; }
    
    [JsonPropertyName("model")]
    public string? Model { get; set; }
    
    [JsonPropertyName("usage")]
    public object? Usage { get; set; }
}

public class EmbeddingTestData
{
    [JsonPropertyName("embedding")]
    public float[]? Embedding { get; set; }
}

// LLM测试响应模型
public class LLMTestResponse
{
    [JsonPropertyName("choices")]
    public List<LLMTestChoice>? Choices { get; set; }
    
    [JsonPropertyName("model")]
    public string? Model { get; set; }
    
    [JsonPropertyName("usage")]
    public object? Usage { get; set; }
}

public class LLMTestChoice
{
    [JsonPropertyName("message")]
    public LLMTestMessage? Message { get; set; }
}

public class LLMTestMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}
