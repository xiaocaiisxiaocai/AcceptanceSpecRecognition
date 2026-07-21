using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;
using CoreAiServiceConfigModel = AcceptanceSpecSystem.Core.AI.Models.AiServiceConfigModel;
using CoreAiServicePurpose = AcceptanceSpecSystem.Core.AI.Models.AiServicePurpose;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// AI服务配置API控制器
/// </summary>
[Route("api/ai-services")]
[Authorize]
public class AiServicesController : BaseApiController
{
    private readonly IAiServiceConfigurationAppService _configuration;
    private readonly IAiServiceSelectionAppService _selection;
    private readonly AiServiceReadinessRegistry _readinessRegistry;
    private readonly ISemanticKernelServiceFactory _semanticKernelFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AiServicesController> _logger;
    private readonly int _llmTestTimeoutSeconds;
    private readonly TimeSpan _llmTestTimeout;
    private readonly int _embeddingTestTimeoutSeconds;
    private readonly TimeSpan _embeddingTestTimeout;
    private readonly string _azureOpenAiApiVersion;

    public AiServicesController(
        IAiServiceConfigurationAppService configuration,
        IAiServiceSelectionAppService selection,
        AiServiceReadinessRegistry readinessRegistry,
        ISemanticKernelServiceFactory semanticKernelFactory,
        IHttpClientFactory httpClientFactory,
        IOptions<AiServiceTestOptions> aiServiceTestOptions,
        IOptions<SemanticKernelOptions> semanticKernelOptions,
        ILogger<AiServicesController> logger)
    {
        _configuration = configuration;
        _selection = selection;
        _readinessRegistry = readinessRegistry;
        _semanticKernelFactory = semanticKernelFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _llmTestTimeoutSeconds = Math.Clamp(aiServiceTestOptions.Value.LlmTimeoutSeconds, 1, 300);
        _llmTestTimeout = TimeSpan.FromSeconds(_llmTestTimeoutSeconds);
        _embeddingTestTimeoutSeconds = Math.Clamp(aiServiceTestOptions.Value.EmbeddingTimeoutSeconds, 1, 300);
        _embeddingTestTimeout = TimeSpan.FromSeconds(_embeddingTestTimeoutSeconds);
        _azureOpenAiApiVersion = string.IsNullOrWhiteSpace(semanticKernelOptions.Value.AzureOpenAIApiVersion)
            ? new SemanticKernelOptions().AzureOpenAIApiVersion
            : semanticKernelOptions.Value.AzureOpenAIApiVersion.Trim();
    }

    /// <summary>
    /// 按用途返回当前运行可用的自动选择结果。响应不包含 Endpoint 或 ApiKey。
    /// </summary>
    [HttpGet("selection")]
    [ProducesResponseType(typeof(ApiResponse<AiServiceSelectionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AiServiceSelectionDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AiServiceSelectionDto>>> GetSelection(
        [FromQuery] AiServicePurpose purpose,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Success(await _selection.GetSelectionAsync(purpose, cancellationToken));
        }
        catch (ApplicationServiceException ex)
        {
            return Error<AiServiceSelectionDto>(ex.Code, ex.Message);
        }
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
        [FromQuery] AiServiceType? serviceType = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _configuration.GetPagedAsync(page, pageSize, keyword, serviceType, cancellationToken);
        return Success(new PagedData<AiServiceConfigDto>
        {
            Items = result.Items,
            Total = result.Total,
            Page = result.Page,
            PageSize = result.PageSize
        });
    }

    /// <summary>
    /// 获取AI服务配置详情
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<AiServiceConfigDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AiServiceConfigDetailDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AiServiceConfigDetailDto>>> GetById(
        int id,
        CancellationToken cancellationToken = default)
    {
        var item = await _configuration.GetByIdAsync(id, cancellationToken);
        if (item == null)
            return NotFoundResult<AiServiceConfigDetailDto>("配置不存在");
        return Success(item);
    }

    /// <summary>
    /// 新增AI服务配置
    /// </summary>
    [HttpPost]
    [AuditOperation("create", "ai-service")]
    [ProducesResponseType(typeof(ApiResponse<AiServiceConfigDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AiServiceConfigDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AiServiceConfigDto>>> Create(
        [FromBody] CreateAiServiceRequest request,
        CancellationToken cancellationToken = default)
    {
        try { return Success(await _configuration.CreateAsync(request, cancellationToken), "创建成功"); }
        catch (ApplicationServiceException ex) { return Error<AiServiceConfigDto>(ex.Code, ex.Message); }
    }

    /// <summary>
    /// 更新AI服务配置
    /// </summary>
    [HttpPut("{id}")]
    [AuditOperation("update", "ai-service")]
    [ProducesResponseType(typeof(ApiResponse<AiServiceConfigDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AiServiceConfigDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AiServiceConfigDto>>> Update(
        int id,
        [FromBody] UpdateAiServiceRequest request,
        CancellationToken cancellationToken = default)
    {
        try { return Success(await _configuration.UpdateAsync(id, request, cancellationToken), "更新成功"); }
        catch (ApplicationServiceException ex) { return Error<AiServiceConfigDto>(ex.Code, ex.Message); }
    }

    /// <summary>
    /// 启用或禁用AI服务配置
    /// </summary>
    [HttpPut("{id}/disabled")]
    [AuditOperation("update", "ai-service")]
    [ProducesResponseType(typeof(ApiResponse<AiServiceConfigDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AiServiceConfigDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AiServiceConfigDto>>> SetDisabled(
        int id,
        [FromBody] SetAiServiceDisabledRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var item = await _configuration.SetDisabledAsync(id, request.IsDisabled, cancellationToken);
            return Success(item, request.IsDisabled ? "已禁用" : "已启用");
        }
        catch (ApplicationServiceException ex) { return Error<AiServiceConfigDto>(ex.Code, ex.Message); }
    }

    /// <summary>
    /// 删除AI服务配置
    /// </summary>
    [HttpDelete("{id}")]
    [AuditOperation("delete", "ai-service")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse>> Delete(
        int id,
        CancellationToken cancellationToken = default)
    {
        try { await _configuration.DeleteAsync(id, cancellationToken); return Success("删除成功"); }
        catch (ApplicationServiceException ex) { return Error(ex.Code, ex.Message); }
    }

    /// <summary>
    /// 测试AI服务连接
    /// </summary>
    [HttpPost("{id}/test")]
    [AuditOperation("test", "ai-service")]
    [EnableRateLimiting("ai-heavy")]
    [ProducesResponseType(typeof(ApiResponse<AiServiceTestResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AiServiceTestResultDto>>> TestConnection(
        int id,
        [FromQuery] AiServiceConnectionTestMode mode = AiServiceConnectionTestMode.Full,
        CancellationToken cancellationToken = default)
    {
        var entity = await _configuration.GetProbeConfigAsync(id, cancellationToken);
        if (entity == null)
            return Error<AiServiceTestResultDto>(400, "配置不存在");
        if (entity.IsDisabled)
            return Error<AiServiceTestResultDto>(400, "配置已禁用");
        if (IsLegacyDualPurpose(entity.Purpose))
            return Error<AiServiceTestResultDto>(400, BuildLegacyDualPurposeMessage());

        var effectivePurpose = entity.Purpose;
        var purposeError = ValidatePurpose(effectivePurpose);
        if (purposeError != null)
            return Error<AiServiceTestResultDto>(400, purposeError);

        var readinessGeneration = _readinessRegistry.CaptureGeneration(entity.Id);
        var sw = Stopwatch.StartNew();
        try
        {
            var messages = new List<string>();
            var success = true;
            IReadOnlyCollection<string>? ollamaModels = null;
            long? serviceElapsedMs = null;
            var isFullMode = mode == AiServiceConnectionTestMode.Full;
            var targetModel = effectivePurpose == AiServicePurpose.Llm
                ? NormalizeOptional(entity.LlmModel)
                : NormalizeOptional(entity.EmbeddingModel);
            var targetEndpoint = NormalizeOptional(entity.Endpoint);
            var hostPort = ResolveHostPort();

            if (effectivePurpose.HasFlag(AiServicePurpose.Llm))
            {
                var serviceSw = Stopwatch.StartNew();
                try
                {
                    using var timeoutCts = CreateTestTimeoutTokenSource(cancellationToken, _llmTestTimeout);
                    if (!isFullMode)
                    {
                        ollamaModels ??= await FetchRemoteModelsAsync(entity, timeoutCts.Token);
                        if (ContainsConfiguredModel(ollamaModels, entity.LlmModel))
                        {
                            messages.Add($"LLM: OK（快速测试，模型可见: {entity.LlmModel}）");
                        }
                        else
                        {
                            success = false;
                            messages.Add($"LLM: 快速测试未找到已配置模型（{entity.LlmModel}）");
                        }
                    }
                    else if (entity.ServiceType == AiServiceType.Ollama)
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
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    success = false;
                    messages.Add(BuildTimeoutMessage("LLM", _llmTestTimeoutSeconds, isFullMode));
                    _logger.LogWarning(
                        "AI服务连接测试超时: {Id} {Name}, service=LLM, mode={Mode}, timeoutSec={TimeoutSec}",
                        entity.Id,
                        entity.Name,
                        isFullMode ? "full" : "quick",
                        _llmTestTimeoutSeconds);
                }
                catch (Exception ex)
                {
                    success = false;
                    _logger.LogWarning(ex, "AI服务连接测试失败: {Id} {Name}, service=LLM, mode={Mode}", entity.Id, entity.Name, isFullMode ? "full" : "quick");
                    messages.Add(BuildTestFailureMessage("LLM", ex, isFullMode));
                }
                finally
                {
                    serviceSw.Stop();
                    serviceElapsedMs = serviceSw.ElapsedMilliseconds;
                }
            }

            if (effectivePurpose.HasFlag(AiServicePurpose.Embedding))
            {
                var serviceSw = Stopwatch.StartNew();
                try
                {
                    using var timeoutCts = CreateTestTimeoutTokenSource(cancellationToken, _embeddingTestTimeout);
                    if (!isFullMode)
                    {
                        ollamaModels ??= await FetchRemoteModelsAsync(entity, timeoutCts.Token);
                        if (ContainsConfiguredModel(ollamaModels, entity.EmbeddingModel))
                        {
                            messages.Add($"Embedding: OK（快速测试，模型可见: {entity.EmbeddingModel}）");
                        }
                        else
                        {
                            success = false;
                            messages.Add($"Embedding: 快速测试未找到已配置模型（{entity.EmbeddingModel}）");
                        }
                    }
                    else if (entity.ServiceType == AiServiceType.Ollama)
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
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    success = false;
                    messages.Add(BuildTimeoutMessage("Embedding", _embeddingTestTimeoutSeconds, isFullMode));
                    _logger.LogWarning(
                        "AI服务连接测试超时: {Id} {Name}, service=Embedding, mode={Mode}, timeoutSec={TimeoutSec}",
                        entity.Id,
                        entity.Name,
                        isFullMode ? "full" : "quick",
                        _embeddingTestTimeoutSeconds);
                }
                catch (Exception ex)
                {
                    success = false;
                    _logger.LogWarning(ex, "AI服务连接测试失败: {Id} {Name}, service=Embedding, mode={Mode}", entity.Id, entity.Name, isFullMode ? "full" : "quick");
                    messages.Add(BuildTestFailureMessage("Embedding", ex, isFullMode));
                }
                finally
                {
                    serviceSw.Stop();
                    serviceElapsedMs = serviceSw.ElapsedMilliseconds;
                }
            }

            sw.Stop();
            var runtimePurpose = ToCorePurpose(effectivePurpose);
            if (success)
                _readinessRegistry.ReportAvailableIfCurrent(entity.Id, runtimePurpose, readinessGeneration);
            else
                _readinessRegistry.ReportUnavailableIfCurrent(entity.Id, runtimePurpose, readinessGeneration);
            return Success(new AiServiceTestResultDto
            {
                Success = success,
                HttpStatusCode = null,
                ElapsedMs = sw.ElapsedMilliseconds,
                ServiceElapsedMs = serviceElapsedMs,
                TargetModel = targetModel,
                TargetEndpoint = targetEndpoint,
                HostPort = hostPort,
                Message = messages.Count > 0 ? string.Join("; ", messages) : "未执行测试"
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _readinessRegistry.ReportUnavailableIfCurrent(
                entity.Id,
                ToCorePurpose(effectivePurpose),
                readinessGeneration);
            _logger.LogWarning(ex, "AI服务连接测试失败: {Id} {Name}", entity.Id, entity.Name);
            return Success(new AiServiceTestResultDto
            {
                Success = false,
                HttpStatusCode = null,
                ElapsedMs = sw.ElapsedMilliseconds,
                ServiceElapsedMs = null,
                TargetModel = effectivePurpose == AiServicePurpose.Llm
                    ? NormalizeOptional(entity.LlmModel)
                    : NormalizeOptional(entity.EmbeddingModel),
                TargetEndpoint = NormalizeOptional(entity.Endpoint),
                HostPort = ResolveHostPort(),
                Message = "AI服务连接测试失败，请稍后重试或查看后台日志"
            }, "连接测试完成");
        }
    }

    private static CancellationTokenSource CreateTestTimeoutTokenSource(CancellationToken cancellationToken, TimeSpan timeout)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        return cts;
    }

    private static string BuildTimeoutMessage(string serviceName, int timeoutSeconds, bool isFullMode)
    {
        return isFullMode
            ? $"{serviceName}: 测试超时（{timeoutSeconds}秒）"
            : $"{serviceName}: 快速测试超时（{timeoutSeconds}秒）";
    }

    private static string BuildTestFailureMessage(string serviceName, Exception exception, bool isFullMode)
    {
        var prefix = isFullMode ? serviceName : $"{serviceName}: 快速测试";
        if (IsSafeClientValidationMessage(exception.Message))
        {
            return $"{prefix}: {exception.Message}";
        }

        if (TryBuildFriendlyRemoteErrorMessage(exception, out var friendlyMessage))
        {
            return $"{prefix}: {friendlyMessage}";
        }

        return $"{prefix}: 远端接口异常，请稍后重试";
    }

    private static bool IsSafeClientValidationMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("Endpoint", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("ApiKey", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("模型", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryBuildFriendlyRemoteErrorMessage(Exception exception, out string message)
    {
        var statusCode = TryGetHttpStatusCode(exception);
        if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden || ContainsAuthenticationFailureKeyword(exception))
        {
            message = "远端接口鉴权失败，请检查 ApiKey 是否正确";
            return true;
        }

        if (statusCode == HttpStatusCode.NotFound)
        {
            message = "远端接口地址无效，请检查 Endpoint 是否正确";
            return true;
        }

        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            message = "远端接口限流或额度受限，请稍后重试";
            return true;
        }

        if (statusCode.HasValue && (int)statusCode.Value >= 500)
        {
            message = $"远端接口服务异常（HTTP {(int)statusCode.Value}）";
            return true;
        }

        if (statusCode.HasValue)
        {
            message = $"远端接口异常（HTTP {(int)statusCode.Value}）";
            return true;
        }

        message = string.Empty;
        return false;
    }

    private static HttpStatusCode? TryGetHttpStatusCode(Exception exception)
    {
        foreach (var current in EnumerateExceptionChain(exception))
        {
            if (current is HttpRequestException httpRequestException && httpRequestException.StatusCode.HasValue)
            {
                return httpRequestException.StatusCode.Value;
            }

            var reflectedStatusCode = TryReadStatusCodeFromException(current);
            if (reflectedStatusCode.HasValue)
            {
                return reflectedStatusCode.Value;
            }

            var parsedStatusCode = TryParseStatusCodeFromMessage(current.Message);
            if (parsedStatusCode.HasValue)
            {
                return parsedStatusCode.Value;
            }
        }

        return null;
    }

    private static IEnumerable<Exception> EnumerateExceptionChain(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private static HttpStatusCode? TryReadStatusCodeFromException(Exception exception)
    {
        var exceptionType = exception.GetType();
        var statusCodeProperty = exceptionType.GetProperty("StatusCode");
        if (statusCodeProperty?.GetValue(exception) is HttpStatusCode statusCode)
        {
            return statusCode;
        }

        if (statusCodeProperty?.GetValue(exception) is int integerStatusCode &&
            Enum.IsDefined(typeof(HttpStatusCode), integerStatusCode))
        {
            return (HttpStatusCode)integerStatusCode;
        }

        var statusProperty = exceptionType.GetProperty("Status");
        if (statusProperty?.GetValue(exception) is int integerStatus &&
            integerStatus >= 100 &&
            integerStatus <= 599)
        {
            return (HttpStatusCode)integerStatus;
        }

        return null;
    }

    private static HttpStatusCode? TryParseStatusCodeFromMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        foreach (var marker in new[] { "HTTP ", "返回 ", "Status: " })
        {
            var markerIndex = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                continue;
            }

            var numberStartIndex = markerIndex + marker.Length;
            if (numberStartIndex + 3 > message.Length)
            {
                continue;
            }

            var statusCodeText = message.Substring(numberStartIndex, 3);
            if (int.TryParse(statusCodeText, out var integerStatusCode) &&
                integerStatusCode >= 100 &&
                integerStatusCode <= 599)
            {
                return (HttpStatusCode)integerStatusCode;
            }
        }

        return null;
    }

    private static bool ContainsAuthenticationFailureKeyword(Exception exception)
    {
        foreach (var current in EnumerateExceptionChain(exception))
        {
            var message = current.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            if (message.Contains("invalid authentication", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("invalid token", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsConfiguredModel(IEnumerable<string> models, string? configuredModel)
    {
        if (string.IsNullOrWhiteSpace(configuredModel))
            return false;

        var expected = configuredModel.Trim();
        return models.Any(model => string.Equals(model, expected, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyCollection<string>> FetchRemoteModelsAsync(
        AiServiceProbeConfig config,
        CancellationToken cancellationToken)
    {
        return config.ServiceType switch
        {
            AiServiceType.OpenAI or AiServiceType.CustomOpenAICompatible or AiServiceType.LMStudio
                => await FetchOpenAiCompatibleModelsAsync(config, cancellationToken),
            AiServiceType.AzureOpenAI => await FetchAzureDeploymentModelsAsync(config, cancellationToken),
            AiServiceType.Ollama => await FetchOllamaModelsAsync(config, cancellationToken),
            _ => []
        };
    }

    private string ResolveHostPort()
    {
        var hostValue = HttpContext.Request.Host.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(hostValue))
        {
            return hostValue;
        }

        if (HttpContext.Connection.LocalPort > 0)
        {
            var host = HttpContext.Connection.LocalIpAddress?.ToString();
            if (string.IsNullOrWhiteSpace(host))
            {
                host = "localhost";
            }

            return $"{host}:{HttpContext.Connection.LocalPort}";
        }

        return "unknown";
    }

    /// <summary>
    /// 获取模型列表（远程探测）
    /// </summary>
    [HttpGet("{id}/models")]
    [AuditOperation("models", "ai-service")]
    [EnableRateLimiting("ai-heavy")]
    [ProducesResponseType(typeof(ApiResponse<AiServiceModelsResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AiServiceModelsResultDto>>> GetModels(
        int id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _configuration.GetProbeConfigAsync(id, cancellationToken);
        if (entity == null)
            return Error<AiServiceModelsResultDto>(400, "配置不存在");
        if (entity.IsDisabled)
            return Error<AiServiceModelsResultDto>(400, "配置已禁用");
        if (IsLegacyDualPurpose(entity.Purpose))
            return Error<AiServiceModelsResultDto>(400, BuildLegacyDualPurposeMessage());

        var result = await ProbeModelsAsync(entity, cancellationToken);
        return Success(result, result.Message ?? "模型探测完成");
    }

    private async Task<AiServiceModelsResultDto> ProbeModelsAsync(AiServiceProbeConfig config, CancellationToken cancellationToken)
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
            var effectivePurpose = config.Purpose;
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

            if (effectivePurpose.HasFlag(AiServicePurpose.Llm))
                result.LlmModels = models.ToList();
            if (effectivePurpose.HasFlag(AiServicePurpose.Embedding))
                result.EmbeddingModels = models.ToList();

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "远端模型探测失败: {Id} {Name}", config.Id, config.Name);
            return new AiServiceModelsResultDto
            {
                Message = "远端模型探测失败，请稍后重试或联系管理员"
            };
        }
    }

    private async Task<IReadOnlyList<string>> FetchOpenAiCompatibleModelsAsync(
        AiServiceProbeConfig config,
        CancellationToken cancellationToken)
    {
        var endpoint = NormalizeOpenAiBaseUrl(config.Endpoint!, config.ServiceType);
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
        AiServiceProbeConfig config,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("Azure OpenAI 需要配置 ApiKey 才能探测模型");

        var endpoint = AiEndpointNormalizer.NormalizeRequiredEndpoint(config.Endpoint, "Endpoint").TrimEnd('/');
        var url = $"{endpoint}/openai/deployments?api-version={Uri.EscapeDataString(_azureOpenAiApiVersion)}";
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
        AiServiceProbeConfig config,
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

    private static string NormalizeOpenAiBaseUrl(string endpoint, AiServiceType serviceType)
    {
        var baseUrl = AiEndpointNormalizer.NormalizeRequiredEndpoint(
            endpoint,
            allowPrivateNetwork: serviceType == AiServiceType.LMStudio).TrimEnd('/');
        if (baseUrl.EndsWith("/v1/v1", StringComparison.OrdinalIgnoreCase))
            baseUrl = baseUrl[..^3];
        if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return baseUrl;
        return $"{baseUrl}/v1";
    }

    private static string NormalizeOllamaBaseUrl(string endpoint)
    {
        var baseUrl = AiEndpointNormalizer.NormalizeRequiredEndpoint(
            endpoint,
            allowPrivateNetwork: true).TrimEnd('/');
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

    private static string? TryNormalizeEndpoint(
        string? value,
        AiServiceType serviceType,
        out string? normalizedEndpoint)
    {
        try
        {
            normalizedEndpoint = AiEndpointNormalizer.NormalizeOptionalEndpoint(
                value,
                allowPrivateNetwork: serviceType == AiServiceType.Ollama || serviceType == AiServiceType.LMStudio);
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

    private static string BuildLegacyDualPurposeMessage()
    {
        return AiServiceConfigurationAppService.LegacyMessage;
    }

    private static bool IsLegacyDualPurpose(AiServicePurpose purpose) =>
        purpose.HasFlag(AiServicePurpose.Llm) && purpose.HasFlag(AiServicePurpose.Embedding);

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

    private static CoreAiServiceConfigModel ToCoreModel(AiServiceProbeConfig entity)
    {
        var effectivePurpose = entity.Purpose;
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
            Purpose = effectivePurpose switch
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
            EmbeddingModel = effectivePurpose.HasFlag(AcceptanceSpecSystem.Data.Entities.AiServicePurpose.Embedding)
                ? entity.EmbeddingModel
                : null,
            LlmModel = effectivePurpose.HasFlag(AcceptanceSpecSystem.Data.Entities.AiServicePurpose.Llm)
                ? entity.LlmModel
                : null,
            DisableThinking = entity.DisableThinking,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    private static CoreAiServicePurpose ToCorePurpose(AiServicePurpose purpose) => purpose switch
    {
        AiServicePurpose.Llm => CoreAiServicePurpose.Llm,
        AiServicePurpose.Embedding => CoreAiServicePurpose.Embedding,
        _ => CoreAiServicePurpose.None
    };

}
