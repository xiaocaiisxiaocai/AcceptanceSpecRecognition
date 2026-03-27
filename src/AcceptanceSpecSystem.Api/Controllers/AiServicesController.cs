using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Options;
using CoreAiServiceConfigModel = AcceptanceSpecSystem.Core.AI.Models.AiServiceConfigModel;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// AI服务配置API控制器
/// </summary>
[Route("api/ai-services")]
[Authorize]
public class AiServicesController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISemanticKernelServiceFactory _semanticKernelFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AiServicesController> _logger;
    private readonly int _testTimeoutSeconds;
    private readonly TimeSpan _testTimeout;

    public AiServicesController(
        IUnitOfWork unitOfWork,
        ISemanticKernelServiceFactory semanticKernelFactory,
        IHttpClientFactory httpClientFactory,
        IOptions<AiServiceTestOptions> aiServiceTestOptions,
        ILogger<AiServicesController> logger)
    {
        _unitOfWork = unitOfWork;
        _semanticKernelFactory = semanticKernelFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _testTimeoutSeconds = Math.Clamp(aiServiceTestOptions.Value.TimeoutSeconds, 1, 300);
        _testTimeout = TimeSpan.FromSeconds(_testTimeoutSeconds);
    }

    /// <summary>
    /// 获取AI服务配置列表
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedData<AiServiceConfigDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedData<AiServiceConfigDto>>>> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] AiServiceType? serviceType = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _unitOfWork.AiServiceConfigs.Query();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var key = keyword.Trim();
            query = query.Where(c =>
                c.Name.Contains(key) ||
                (c.Endpoint != null && c.Endpoint.Contains(key)));
        }

        if (serviceType.HasValue)
        {
            query = query.Where(c => c.ServiceType == serviceType.Value);
        }

        var total = await query.CountAsync();
        var rows = await query
            .OrderBy(c => c.Priority)
            .ThenByDescending(c => c.UpdatedAt ?? c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        var items = rows.Select(ToDto).ToList();

        return Success(new PagedData<AiServiceConfigDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取AI服务配置详情
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<AiServiceConfigDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AiServiceConfigDetailDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AiServiceConfigDetailDto>>> GetById(int id)
    {
        var entity = await _unitOfWork.AiServiceConfigs.GetByIdAsync(id);
        if (entity == null)
            return NotFoundResult<AiServiceConfigDetailDto>("配置不存在");

        return Success(ToDetailDto(entity));
    }

    /// <summary>
    /// 新增AI服务配置
    /// </summary>
    [HttpPost]
    [AuditOperation("create", "ai-service")]
    [ProducesResponseType(typeof(ApiResponse<AiServiceConfigDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AiServiceConfigDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AiServiceConfigDto>>> Create([FromBody] CreateAiServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Error<AiServiceConfigDto>(400, "名称不能为空");
        var purposeError = ValidatePurpose(request.Purpose);
        if (purposeError != null)
            return Error<AiServiceConfigDto>(400, purposeError);
        var modelError = ValidateModelForPurpose(request.Purpose, request.LlmModel, request.EmbeddingModel);
        if (modelError != null)
            return Error<AiServiceConfigDto>(400, modelError);
        var endpointError = TryNormalizeEndpoint(request.Endpoint, out var normalizedEndpoint);
        if (endpointError != null)
            return Error<AiServiceConfigDto>(400, endpointError);

        var exists = await _unitOfWork.AiServiceConfigs.GetByNameAsync(request.Name.Trim());
        if (exists != null)
            return Error<AiServiceConfigDto>(400, "名称已存在");

        var embeddingModel = NormalizeOptional(request.EmbeddingModel);
        var llmModel = NormalizeOptional(request.LlmModel);
        if (request.Purpose == AiServicePurpose.Llm)
            embeddingModel = null;
        if (request.Purpose == AiServicePurpose.Embedding)
            llmModel = null;

        var entity = new AiServiceConfig
        {
            Name = request.Name.Trim(),
            ServiceType = request.ServiceType,
            Purpose = request.Purpose,
            Priority = request.Priority,
            ApiKey = NormalizeOptional(request.ApiKey),
            Endpoint = normalizedEndpoint,
            EmbeddingModel = embeddingModel,
            LlmModel = llmModel,
            DisableThinking = request.DisableThinking,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.AiServiceConfigs.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("创建AI服务配置: {Id} {Name} {Type}", entity.Id, entity.Name, entity.ServiceType);
        return Success(ToDto(entity), "创建成功");
    }

    /// <summary>
    /// 更新AI服务配置
    /// </summary>
    [HttpPut("{id}")]
    [AuditOperation("update", "ai-service")]
    [ProducesResponseType(typeof(ApiResponse<AiServiceConfigDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AiServiceConfigDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AiServiceConfigDto>>> Update(int id, [FromBody] UpdateAiServiceRequest request)
    {
        var entity = await _unitOfWork.AiServiceConfigs.GetByIdAsync(id);
        if (entity == null)
            return Error<AiServiceConfigDto>(400, "配置不存在");

        if (string.IsNullOrWhiteSpace(request.Name))
            return Error<AiServiceConfigDto>(400, "名称不能为空");
        var purposeError = ValidatePurpose(request.Purpose);
        if (purposeError != null)
            return Error<AiServiceConfigDto>(400, purposeError);
        var modelError = ValidateModelForPurpose(request.Purpose, request.LlmModel, request.EmbeddingModel);
        if (modelError != null)
            return Error<AiServiceConfigDto>(400, modelError);
        var endpointError = TryNormalizeEndpoint(request.Endpoint, out var normalizedEndpoint);
        if (endpointError != null)
            return Error<AiServiceConfigDto>(400, endpointError);

        var newName = request.Name.Trim();
        if (!string.Equals(entity.Name, newName, StringComparison.OrdinalIgnoreCase))
        {
            var exists = await _unitOfWork.AiServiceConfigs.GetByNameAsync(newName);
            if (exists != null && exists.Id != id)
                return Error<AiServiceConfigDto>(400, "名称已存在");
        }

        entity.Name = newName;
        entity.ServiceType = request.ServiceType;
        entity.Purpose = request.Purpose;
        entity.Priority = request.Priority;
        entity.Endpoint = normalizedEndpoint;
        entity.DisableThinking = request.DisableThinking;

        var embeddingModel = NormalizeOptional(request.EmbeddingModel);
        var llmModel = NormalizeOptional(request.LlmModel);
        if (request.Purpose == AiServicePurpose.Llm)
            embeddingModel = null;
        if (request.Purpose == AiServicePurpose.Embedding)
            llmModel = null;
        entity.EmbeddingModel = embeddingModel;
        entity.LlmModel = llmModel;
        if (request.ApiKey != null)
        {
            // 允许更新/清空 ApiKey：传空字符串即清空
            entity.ApiKey = NormalizeOptional(request.ApiKey);
        }

        entity.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.AiServiceConfigs.Update(entity);

        await _unitOfWork.SaveChangesAsync();

        return Success(ToDto(entity), "更新成功");
    }

    /// <summary>
    /// 删除AI服务配置
    /// </summary>
    [HttpDelete("{id}")]
    [AuditOperation("delete", "ai-service")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse>> Delete(int id)
    {
        var entity = await _unitOfWork.AiServiceConfigs.GetByIdAsync(id);
        if (entity == null)
            return Error(400, "配置不存在");

        _unitOfWork.AiServiceConfigs.Remove(entity);
        await _unitOfWork.SaveChangesAsync();

        return Success("删除成功");
    }

    /// <summary>
    /// 测试AI服务连接
    /// </summary>
    [HttpPost("{id}/test")]
    [ProducesResponseType(typeof(ApiResponse<AiServiceTestResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AiServiceTestResultDto>>> TestConnection(int id)
    {
        var entity = await _unitOfWork.AiServiceConfigs.GetByIdAsync(id);
        if (entity == null)
            return Error<AiServiceTestResultDto>(400, "配置不存在");

        var purposeError = ValidatePurpose(entity.Purpose);
        if (purposeError != null)
            return Error<AiServiceTestResultDto>(400, purposeError);

        var sw = Stopwatch.StartNew();
        try
        {
            var messages = new List<string>();
            var success = true;
            IReadOnlyCollection<string>? ollamaModels = null;

            if (entity.Purpose.HasFlag(AiServicePurpose.Llm))
            {
                try
                {
                    using var timeoutCts = CreateTestTimeoutTokenSource();
                    if (entity.ServiceType == AiServiceType.Ollama)
                    {
                        ollamaModels ??= await FetchOllamaModelsAsync(entity, timeoutCts.Token);
                        if (ContainsConfiguredModel(ollamaModels, entity.LlmModel))
                        {
                            messages.Add($"LLM: OK（模型已存在: {entity.LlmModel}）");
                        }
                        else
                        {
                            success = false;
                            messages.Add($"LLM: 未找到已配置模型（{entity.LlmModel}）");
                        }
                    }
                    else
                    {
                        var chat = _semanticKernelFactory.CreateChatCompletionService(ToCoreModel(entity));
                        var history = new ChatHistory();
                        history.AddUserMessage("ping");
                        await chat.GetChatMessageContentAsync(history, cancellationToken: timeoutCts.Token);
                        messages.Add("LLM: OK");
                    }
                }
                catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
                {
                    success = false;
                    messages.Add(BuildTimeoutMessage("LLM"));
                    _logger.LogWarning(
                        "AI服务连接测试超时: {Id} {Name}, service=LLM, timeoutSec={TimeoutSec}",
                        entity.Id,
                        entity.Name,
                        _testTimeoutSeconds);
                }
                catch (Exception ex)
                {
                    success = false;
                    messages.Add($"LLM: {ex.Message}");
                }
            }

            if (entity.Purpose.HasFlag(AiServicePurpose.Embedding))
            {
                try
                {
                    using var timeoutCts = CreateTestTimeoutTokenSource();
                    if (entity.ServiceType == AiServiceType.Ollama)
                    {
                        ollamaModels ??= await FetchOllamaModelsAsync(entity, timeoutCts.Token);
                        if (ContainsConfiguredModel(ollamaModels, entity.EmbeddingModel))
                        {
                            messages.Add($"Embedding: OK（模型已存在: {entity.EmbeddingModel}）");
                        }
                        else
                        {
                            success = false;
                            messages.Add($"Embedding: 未找到已配置模型（{entity.EmbeddingModel}）");
                        }
                    }
                    else
                    {
                        var embedding = _semanticKernelFactory.CreateEmbeddingGenerator(ToCoreModel(entity));
                        var vector = await embedding.GenerateVectorAsync("ping", cancellationToken: timeoutCts.Token);
                        messages.Add($"Embedding: OK (dim={vector.ToArray().Length})");
                    }
                }
                catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
                {
                    success = false;
                    messages.Add(BuildTimeoutMessage("Embedding"));
                    _logger.LogWarning(
                        "AI服务连接测试超时: {Id} {Name}, service=Embedding, timeoutSec={TimeoutSec}",
                        entity.Id,
                        entity.Name,
                        _testTimeoutSeconds);
                }
                catch (Exception ex)
                {
                    success = false;
                    messages.Add($"Embedding: {ex.Message}");
                }
            }

            sw.Stop();
            return Success(new AiServiceTestResultDto
            {
                Success = success,
                HttpStatusCode = null,
                ElapsedMs = sw.ElapsedMilliseconds,
                Message = messages.Count > 0 ? string.Join("; ", messages) : "未执行测试"
            });
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "AI服务连接测试失败: {Id} {Name}", entity.Id, entity.Name);
            return Success(new AiServiceTestResultDto
            {
                Success = false,
                HttpStatusCode = null,
                ElapsedMs = sw.ElapsedMilliseconds,
                Message = ex.Message
            }, "连接测试完成");
        }
    }

    private CancellationTokenSource CreateTestTimeoutTokenSource()
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
        cts.CancelAfter(_testTimeout);
        return cts;
    }

    private string BuildTimeoutMessage(string serviceName)
    {
        return $"{serviceName}: 测试超时（{_testTimeoutSeconds}秒）";
    }

    private static bool ContainsConfiguredModel(IEnumerable<string> models, string? configuredModel)
    {
        if (string.IsNullOrWhiteSpace(configuredModel))
            return false;

        var expected = configuredModel.Trim();
        return models.Any(model => string.Equals(model, expected, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 获取模型列表（远程探测）
    /// </summary>
    [HttpGet("{id}/models")]
    [ProducesResponseType(typeof(ApiResponse<AiServiceModelsResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AiServiceModelsResultDto>>> GetModels(int id)
    {
        var entity = await _unitOfWork.AiServiceConfigs.GetByIdAsync(id);
        if (entity == null)
            return Error<AiServiceModelsResultDto>(400, "配置不存在");

        var result = await ProbeModelsAsync(entity, HttpContext.RequestAborted);
        return Success(result, result.Message ?? "模型探测完成");
    }

    private static AiServiceConfigDto ToDto(AiServiceConfig c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        ServiceType = c.ServiceType,
        Purpose = c.Purpose,
        Priority = c.Priority,
        Endpoint = c.Endpoint,
        EmbeddingModel = c.EmbeddingModel,
        LlmModel = c.LlmModel,
        DisableThinking = c.DisableThinking,
        HasApiKey = !string.IsNullOrWhiteSpace(c.ApiKey),
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt
    };

    private static AiServiceConfigDetailDto ToDetailDto(AiServiceConfig c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        ServiceType = c.ServiceType,
        Purpose = c.Purpose,
        Priority = c.Priority,
        Endpoint = c.Endpoint,
        EmbeddingModel = c.EmbeddingModel,
        LlmModel = c.LlmModel,
        DisableThinking = c.DisableThinking,
        HasApiKey = !string.IsNullOrWhiteSpace(c.ApiKey),
        ApiKey = MaskApiKey(c.ApiKey),
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt
    };

    private static string? MaskApiKey(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return apiKey;

        var value = apiKey.Trim();
        if (value.Length <= 4)
            return new string('*', value.Length);
        if (value.Length <= 8)
            return $"{value[..2]}***{value[^2..]}";

        return $"{value[..4]}***{value[^4..]}";
    }

    private async Task<AiServiceModelsResultDto> ProbeModelsAsync(AiServiceConfig config, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.Endpoint))
        {
            return new AiServiceModelsResultDto
            {
                Message = "未配置 Endpoint，无法探测模型列表"
            };
        }

        try
        {
            var models = config.ServiceType switch
            {
                AiServiceType.OpenAI or AiServiceType.CustomOpenAICompatible or AiServiceType.LMStudio
                    => await FetchOpenAiCompatibleModelsAsync(config, cancellationToken),
                AiServiceType.AzureOpenAI => await FetchAzureDeploymentModelsAsync(config, cancellationToken),
                AiServiceType.Ollama => await FetchOllamaModelsAsync(config, cancellationToken),
                _ => []
            };

            var result = new AiServiceModelsResultDto
            {
                Message = models.Count == 0 ? "远端未返回可用模型" : $"远端返回 {models.Count} 个模型（未区分 LLM/Embedding）"
            };

            if (config.Purpose.HasFlag(AiServicePurpose.Llm))
                result.LlmModels = models.ToList();
            if (config.Purpose.HasFlag(AiServicePurpose.Embedding))
                result.EmbeddingModels = models.ToList();

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "远端模型探测失败: {Id} {Name}", config.Id, config.Name);
            return new AiServiceModelsResultDto
            {
                Message = $"远端模型探测失败: {ex.Message}"
            };
        }
    }

    private async Task<IReadOnlyList<string>> FetchOpenAiCompatibleModelsAsync(
        AiServiceConfig config,
        CancellationToken cancellationToken)
    {
        var endpoint = NormalizeOpenAiBaseUrl(config.Endpoint!);
        var url = $"{endpoint}/models";
        using var client = CreateHttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);
        }

        var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI兼容接口返回 {((int)response.StatusCode)}: {TrimMessage(body)}");

        return ParseModelsFromOpenAiResponse(body);
    }

    private async Task<IReadOnlyList<string>> FetchAzureDeploymentModelsAsync(
        AiServiceConfig config,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("Azure OpenAI 需要配置 ApiKey 才能探测模型");

        var endpoint = config.Endpoint!.Trim().TrimEnd('/');
        var url = $"{endpoint}/openai/deployments?api-version=2024-02-15-preview";
        using var client = CreateHttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("api-key", config.ApiKey);

        var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Azure OpenAI 返回 {((int)response.StatusCode)}: {TrimMessage(body)}");

        return ParseModelsFromAzureResponse(body);
    }

    private async Task<IReadOnlyList<string>> FetchOllamaModelsAsync(
        AiServiceConfig config,
        CancellationToken cancellationToken)
    {
        var endpoint = NormalizeOllamaBaseUrl(config.Endpoint!);
        var url = $"{endpoint}/api/tags";
        using var client = CreateHttpClient();
        var response = await client.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Ollama 返回 {((int)response.StatusCode)}: {TrimMessage(body)}");

        return ParseModelsFromOllamaResponse(body);
    }

    private HttpClient CreateHttpClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    private static string NormalizeOpenAiBaseUrl(string endpoint)
    {
        var baseUrl = AiEndpointNormalizer.NormalizeRequiredEndpoint(endpoint).TrimEnd('/');
        if (baseUrl.EndsWith("/v1/v1", StringComparison.OrdinalIgnoreCase))
            baseUrl = baseUrl[..^3];
        if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return baseUrl;
        return $"{baseUrl}/v1";
    }

    private static string NormalizeOllamaBaseUrl(string endpoint)
    {
        var baseUrl = AiEndpointNormalizer.NormalizeRequiredEndpoint(endpoint).TrimEnd('/');
        if (baseUrl.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            baseUrl = baseUrl[..^4];
        if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            baseUrl = baseUrl[..^3];
        return baseUrl.TrimEnd('/');
    }

    private static IReadOnlyList<string> ParseModelsFromOpenAiResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return [];

            var list = new List<string>();
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    var value = id.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        list.Add(value);
                }
            }
            return list;
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ParseModelsFromAzureResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return [];

            var list = new List<string>();
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    var value = id.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        list.Add(value);
                    continue;
                }

                if (item.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String)
                {
                    var value = model.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        list.Add(value);
                }
            }
            return list;
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ParseModelsFromOllamaResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("models", out var data) || data.ValueKind != JsonValueKind.Array)
                return [];

            var list = new List<string>();
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                {
                    var value = name.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        list.Add(value);
                }
            }
            return list;
        }
        catch
        {
            return [];
        }
    }

    private static string TrimMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        return text.Length <= 300 ? text : text[..300] + "...";
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? TryNormalizeEndpoint(string? value, out string? normalizedEndpoint)
    {
        try
        {
            normalizedEndpoint = AiEndpointNormalizer.NormalizeOptionalEndpoint(value);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            normalizedEndpoint = null;
            return ex.Message;
        }
    }

    private static string? ValidatePurpose(AiServicePurpose purpose)
    {
        if (purpose == AiServicePurpose.None)
            return "用途不能为空";

        if (purpose != AiServicePurpose.Llm && purpose != AiServicePurpose.Embedding)
            return "LLM 与 Embedding 需要分开配置，请选择单一用途";

        return null;
    }

    private static string? ValidateModelForPurpose(
        AiServicePurpose purpose,
        string? llmModel,
        string? embeddingModel)
    {
        if (purpose == AiServicePurpose.Llm && string.IsNullOrWhiteSpace(llmModel))
            return "LLM 模型不能为空";
        if (purpose == AiServicePurpose.Embedding && string.IsNullOrWhiteSpace(embeddingModel))
            return "Embedding 模型不能为空";
        return null;
    }

    private static CoreAiServiceConfigModel ToCoreModel(AiServiceConfig entity)
    {
        return new CoreAiServiceConfigModel
        {
            Id = entity.Id,
            Name = entity.Name,
            ServiceType = entity.ServiceType switch
            {
                AcceptanceSpecSystem.Data.Entities.AiServiceType.OpenAI => AcceptanceSpecSystem.Core.AI.Models.AiServiceType.OpenAI,
                AcceptanceSpecSystem.Data.Entities.AiServiceType.AzureOpenAI => AcceptanceSpecSystem.Core.AI.Models.AiServiceType.AzureOpenAI,
                AcceptanceSpecSystem.Data.Entities.AiServiceType.Ollama => AcceptanceSpecSystem.Core.AI.Models.AiServiceType.Ollama,
                AcceptanceSpecSystem.Data.Entities.AiServiceType.LMStudio => AcceptanceSpecSystem.Core.AI.Models.AiServiceType.LMStudio,
                _ => AcceptanceSpecSystem.Core.AI.Models.AiServiceType.CustomOpenAICompatible
            },
            Purpose = entity.Purpose switch
            {
                AcceptanceSpecSystem.Data.Entities.AiServicePurpose.Llm => AcceptanceSpecSystem.Core.AI.Models.AiServicePurpose.Llm,
                AcceptanceSpecSystem.Data.Entities.AiServicePurpose.Embedding => AcceptanceSpecSystem.Core.AI.Models.AiServicePurpose.Embedding,
                AcceptanceSpecSystem.Data.Entities.AiServicePurpose.Llm | AcceptanceSpecSystem.Data.Entities.AiServicePurpose.Embedding
                    => AcceptanceSpecSystem.Core.AI.Models.AiServicePurpose.Llm | AcceptanceSpecSystem.Core.AI.Models.AiServicePurpose.Embedding,
                _ => AcceptanceSpecSystem.Core.AI.Models.AiServicePurpose.None
            },
            Priority = entity.Priority,
            ApiKey = entity.ApiKey,
            Endpoint = entity.Endpoint,
            EmbeddingModel = entity.EmbeddingModel,
            LlmModel = entity.LlmModel,
            DisableThinking = entity.DisableThinking,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

}

