using System.Diagnostics;
using System.Net.Http.Headers;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// AI服务配置API控制器
/// </summary>
[Route("api/ai-services")]
public class AiServicesController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AiServicesController> _logger;

    public AiServicesController(IUnitOfWork unitOfWork, IHttpClientFactory httpClientFactory, ILogger<AiServicesController> logger)
    {
        _unitOfWork = unitOfWork;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
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
        var all = await _unitOfWork.AiServiceConfigs.GetAllAsync();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            all = all.Where(c =>
                    c.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    (c.Endpoint?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        if (serviceType.HasValue)
        {
            all = all.Where(c => c.ServiceType == serviceType.Value).ToList();
        }

        var total = all.Count;
        var items = all
            .OrderByDescending(c => c.IsDefault)
            .ThenByDescending(c => c.UpdatedAt ?? c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToDto)
            .ToList();

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
    [ProducesResponseType(typeof(ApiResponse<AiServiceConfigDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AiServiceConfigDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AiServiceConfigDto>>> GetById(int id)
    {
        var entity = await _unitOfWork.AiServiceConfigs.GetByIdAsync(id);
        if (entity == null)
            return NotFoundResult<AiServiceConfigDto>("配置不存在");

        return Success(ToDto(entity));
    }

    /// <summary>
    /// 新增AI服务配置
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<AiServiceConfigDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AiServiceConfigDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AiServiceConfigDto>>> Create([FromBody] CreateAiServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Error<AiServiceConfigDto>(400, "名称不能为空");

        var exists = await _unitOfWork.AiServiceConfigs.GetByNameAsync(request.Name.Trim());
        if (exists != null)
            return Error<AiServiceConfigDto>(400, "名称已存在");

        var entity = new AiServiceConfig
        {
            Name = request.Name.Trim(),
            ServiceType = request.ServiceType,
            ApiKey = string.IsNullOrWhiteSpace(request.ApiKey) ? null : request.ApiKey.Trim(),
            Endpoint = string.IsNullOrWhiteSpace(request.Endpoint) ? null : request.Endpoint.Trim(),
            EmbeddingModel = string.IsNullOrWhiteSpace(request.EmbeddingModel) ? null : request.EmbeddingModel.Trim(),
            LlmModel = string.IsNullOrWhiteSpace(request.LlmModel) ? null : request.LlmModel.Trim(),
            IsDefault = false,
            CreatedAt = DateTime.Now
        };

        await _unitOfWork.AiServiceConfigs.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        if (request.IsDefault)
        {
            await _unitOfWork.AiServiceConfigs.SetDefaultAsync(entity.Id);
            await _unitOfWork.SaveChangesAsync();
            entity.IsDefault = true;
        }

        _logger.LogInformation("创建AI服务配置: {Id} {Name} {Type}", entity.Id, entity.Name, entity.ServiceType);
        return Success(ToDto(entity), "创建成功");
    }

    /// <summary>
    /// 更新AI服务配置
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ApiResponse<AiServiceConfigDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AiServiceConfigDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AiServiceConfigDto>>> Update(int id, [FromBody] UpdateAiServiceRequest request)
    {
        var entity = await _unitOfWork.AiServiceConfigs.GetByIdAsync(id);
        if (entity == null)
            return Error<AiServiceConfigDto>(400, "配置不存在");

        if (string.IsNullOrWhiteSpace(request.Name))
            return Error<AiServiceConfigDto>(400, "名称不能为空");

        var newName = request.Name.Trim();
        if (!string.Equals(entity.Name, newName, StringComparison.OrdinalIgnoreCase))
        {
            var exists = await _unitOfWork.AiServiceConfigs.GetByNameAsync(newName);
            if (exists != null && exists.Id != id)
                return Error<AiServiceConfigDto>(400, "名称已存在");
        }

        entity.Name = newName;
        entity.ServiceType = request.ServiceType;
        entity.Endpoint = string.IsNullOrWhiteSpace(request.Endpoint) ? null : request.Endpoint.Trim();
        entity.EmbeddingModel = string.IsNullOrWhiteSpace(request.EmbeddingModel) ? null : request.EmbeddingModel.Trim();
        entity.LlmModel = string.IsNullOrWhiteSpace(request.LlmModel) ? null : request.LlmModel.Trim();
        if (request.ApiKey != null)
        {
            // 允许更新/清空 ApiKey：传空字符串即清空
            entity.ApiKey = string.IsNullOrWhiteSpace(request.ApiKey) ? null : request.ApiKey.Trim();
        }

        entity.UpdatedAt = DateTime.Now;
        _unitOfWork.AiServiceConfigs.Update(entity);

        await _unitOfWork.SaveChangesAsync();

        if (request.IsDefault)
        {
            await _unitOfWork.AiServiceConfigs.SetDefaultAsync(entity.Id);
            await _unitOfWork.SaveChangesAsync();
            entity.IsDefault = true;
        }

        return Success(ToDto(entity), "更新成功");
    }

    /// <summary>
    /// 删除AI服务配置
    /// </summary>
    [HttpDelete("{id}")]
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
    /// 设置默认AI服务配置
    /// </summary>
    [HttpPost("{id}/set-default")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> SetDefault(int id)
    {
        var entity = await _unitOfWork.AiServiceConfigs.GetByIdAsync(id);
        if (entity == null)
            return Error(400, "配置不存在");

        await _unitOfWork.AiServiceConfigs.SetDefaultAsync(id);
        await _unitOfWork.SaveChangesAsync();
        return Success("设置默认成功");
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

        if (string.IsNullOrWhiteSpace(entity.Endpoint))
            return Error<AiServiceTestResultDto>(400, "Endpoint不能为空");

        var sw = Stopwatch.StartNew();
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(6);

            using var request = BuildTestRequest(entity);
            using var response = await client.SendAsync(request);

            sw.Stop();
            var ok = ((int)response.StatusCode) is >= 200 and < 500; // 4xx 也证明“能连上”，只是鉴权/路径可能不对

            return Success(new AiServiceTestResultDto
            {
                Success = ok,
                HttpStatusCode = (int)response.StatusCode,
                ElapsedMs = sw.ElapsedMilliseconds,
                Message = ok
                    ? "连接可用（若返回4xx，请检查ApiKey/路径/模型配置）"
                    : $"连接失败: HTTP {(int)response.StatusCode}"
            });
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

    private static AiServiceConfigDto ToDto(AiServiceConfig c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        ServiceType = c.ServiceType,
        Endpoint = c.Endpoint,
        EmbeddingModel = c.EmbeddingModel,
        LlmModel = c.LlmModel,
        IsDefault = c.IsDefault,
        HasApiKey = !string.IsNullOrWhiteSpace(c.ApiKey),
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt
    };

    private static HttpRequestMessage BuildTestRequest(AiServiceConfig cfg)
    {
        var baseUri = new Uri(cfg.Endpoint!.Trim().TrimEnd('/') + "/");
        Uri target;

        switch (cfg.ServiceType)
        {
            case AiServiceType.Ollama:
                target = new Uri(baseUri, "api/tags");
                break;
            case AiServiceType.OpenAI:
            case AiServiceType.CustomOpenAICompatible:
            case AiServiceType.LMStudio:
                target = new Uri(baseUri, "v1/models");
                break;
            case AiServiceType.AzureOpenAI:
                // Azure OpenAI 的标准接口需要 api-version，这里仅做可达性探测
                target = baseUri;
                break;
            default:
                target = baseUri;
                break;
        }

        var req = new HttpRequestMessage(HttpMethod.Get, target);

        if (!string.IsNullOrWhiteSpace(cfg.ApiKey))
        {
            if (cfg.ServiceType == AiServiceType.AzureOpenAI)
            {
                req.Headers.Add("api-key", cfg.ApiKey);
            }
            else
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiKey);
            }
        }

        return req;
    }
}

