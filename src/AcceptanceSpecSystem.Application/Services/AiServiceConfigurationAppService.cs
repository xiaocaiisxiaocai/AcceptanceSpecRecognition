using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Application.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Application.Services;

public interface IAiServiceConfigurationAppService
{
    Task<PagedResult<AiServiceConfigDto>> GetPagedAsync(int page, int pageSize, string? keyword, AiServiceType? serviceType, CancellationToken cancellationToken = default);
    Task<AiServiceConfigDetailDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<AiServiceProbeConfig?> GetProbeConfigAsync(int id, CancellationToken cancellationToken = default);
    Task<AiServiceConfigDto> CreateAsync(CreateAiServiceRequest request, CancellationToken cancellationToken = default);
    Task<AiServiceConfigDto> UpdateAsync(int id, UpdateAiServiceRequest request, CancellationToken cancellationToken = default);
    Task<AiServiceConfigDto> SetDisabledAsync(int id, bool isDisabled, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
public sealed class AiServiceConfigurationAppService : IAiServiceConfigurationAppService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly AiServiceReadinessRegistry _readinessRegistry;

    public AiServiceConfigurationAppService(
        IUnitOfWork unitOfWork,
        AiServiceReadinessRegistry readinessRegistry)
    {
        _unitOfWork = unitOfWork;
        _readinessRegistry = readinessRegistry;
    }

    public async Task<PagedResult<AiServiceConfigDto>> GetPagedAsync(int page, int pageSize, string? keyword, AiServiceType? serviceType, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
        var query = _unitOfWork.AiServiceConfigs.Query();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var key = keyword.Trim();
            query = query.Where(config => config.Name.Contains(key) || (config.Endpoint != null && config.Endpoint.Contains(key)));
        }
        if (serviceType.HasValue) query = query.Where(config => config.ServiceType == serviceType.Value);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(config => config.Priority)
            .ThenByDescending(config => config.UpdatedAt ?? config.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<AiServiceConfigDto>
        {
            Items = rows.Select(ToDto).ToList(), Total = total, Page = page, PageSize = pageSize
        };
    }

    public async Task<AiServiceConfigDetailDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.AiServiceConfigs.Query().SingleOrDefaultAsync(config => config.Id == id, cancellationToken);
        return entity == null ? null : ToDetailDto(entity);
    }

    public async Task<AiServiceProbeConfig?> GetProbeConfigAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.AiServiceConfigs.Query().SingleOrDefaultAsync(config => config.Id == id, cancellationToken);
        return entity == null ? null : new AiServiceProbeConfig(
            entity.Id, entity.Name, entity.ServiceType, entity.GetEffectivePurpose(), entity.Priority,
            entity.ApiKey, entity.Endpoint, entity.EmbeddingModel, entity.LlmModel,
            entity.DisableThinking, entity.IsDisabled, entity.CreatedAt, entity.UpdatedAt);
    }

    public async Task<AiServiceConfigDto> CreateAsync(CreateAiServiceRequest request, CancellationToken cancellationToken = default)
    {
        var values = Validate(request.Name, request.ServiceType, request.Purpose, request.Endpoint, request.LlmModel, request.EmbeddingModel);
        if (await _unitOfWork.AiServiceConfigs.Query().AnyAsync(config => config.Name == values.Name, cancellationToken))
            throw Error("名称已存在");
        var entity = new AiServiceConfig
        {
            Name = values.Name, ServiceType = request.ServiceType, Purpose = request.Purpose,
            Priority = request.Priority, ApiKey = Optional(request.ApiKey), Endpoint = values.Endpoint,
            EmbeddingModel = request.Purpose == AiServicePurpose.Llm ? null : Optional(request.EmbeddingModel),
            LlmModel = request.Purpose == AiServicePurpose.Embedding ? null : Optional(request.LlmModel),
            DisableThinking = request.DisableThinking,
            DefaultRecallTopK = Math.Clamp(request.DefaultRecallTopK, 1, MatchingThresholds.MaxRecallTopK),
            CreatedAt = DateTime.UtcNow
        };
        await _unitOfWork.AiServiceConfigs.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _readinessRegistry.Invalidate(entity.Id);
        return ToDto(entity);
    }

    public async Task<AiServiceConfigDto> UpdateAsync(int id, UpdateAiServiceRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.AiServiceConfigs.GetByIdAsync(id, cancellationToken) ?? throw Error("配置不存在");
        if (entity.IsLegacyDualPurposeConfiguration()) throw Error(LegacyMessage);
        var values = Validate(request.Name, request.ServiceType, request.Purpose, request.Endpoint, request.LlmModel, request.EmbeddingModel);
        if (!string.Equals(entity.Name, values.Name, StringComparison.OrdinalIgnoreCase) &&
            await _unitOfWork.AiServiceConfigs.Query().AnyAsync(config => config.Name == values.Name && config.Id != id, cancellationToken))
            throw Error("名称已存在");
        entity.Name = values.Name; entity.ServiceType = request.ServiceType; entity.Purpose = request.Purpose;
        entity.Priority = request.Priority; entity.Endpoint = values.Endpoint; entity.DisableThinking = request.DisableThinking;
        entity.DefaultRecallTopK = Math.Clamp(request.DefaultRecallTopK, 1, MatchingThresholds.MaxRecallTopK);
        entity.EmbeddingModel = request.Purpose == AiServicePurpose.Llm ? null : Optional(request.EmbeddingModel);
        entity.LlmModel = request.Purpose == AiServicePurpose.Embedding ? null : Optional(request.LlmModel);
        if (request.ApiKey != null) entity.ApiKey = Optional(request.ApiKey);
        entity.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.AiServiceConfigs.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _readinessRegistry.Invalidate(entity.Id);
        return ToDto(entity);
    }

    public async Task<AiServiceConfigDto> SetDisabledAsync(int id, bool isDisabled, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.AiServiceConfigs.GetByIdAsync(id, cancellationToken) ?? throw Error("配置不存在");
        entity.IsDisabled = isDisabled; entity.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.AiServiceConfigs.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _readinessRegistry.Invalidate(entity.Id);
        return ToDto(entity);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.AiServiceConfigs.GetByIdAsync(id, cancellationToken) ?? throw Error("配置不存在");
        _unitOfWork.AiServiceConfigs.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _readinessRegistry.Invalidate(entity.Id);
    }

    private static (string Name, string? Endpoint) Validate(string? name, AiServiceType type, AiServicePurpose purpose, string? endpoint, string? llm, string? embedding)
    {
        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName)) throw Error("名称不能为空");
        if (purpose == AiServicePurpose.None) throw Error("用途不能为空");
        if (purpose != AiServicePurpose.Llm && purpose != AiServicePurpose.Embedding)
            throw Error("LLM 与 Embedding 需要分开配置，请选择单一用途");
        if (purpose == AiServicePurpose.Llm && string.IsNullOrWhiteSpace(llm)) throw Error("LLM 模型不能为空");
        if (purpose == AiServicePurpose.Embedding && string.IsNullOrWhiteSpace(embedding)) throw Error("Embedding 模型不能为空");
        try
        {
            var normalizedEndpoint = AiEndpointNormalizer.NormalizeOptionalEndpoint(
                endpoint, allowPrivateNetwork: type is AiServiceType.Ollama or AiServiceType.LMStudio);
            return (normalizedName, normalizedEndpoint);
        }
        catch (InvalidOperationException ex) { throw Error(ex.Message); }
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static ApplicationServiceException Error(string message) => new(400, message);
    public const string LegacyMessage = "检测到历史双用途 AI 服务配置，当前记录同时保留了 LLM 和 Embedding 模型。请先完成迁移拆分后再编辑或测试，避免静默丢失另一侧模型。";

    private static AiServiceConfigDto ToDto(AiServiceConfig entity)
    {
        var purpose = entity.GetEffectivePurpose();
        return new AiServiceConfigDto
        {
            Id = entity.Id, Name = entity.Name, ServiceType = entity.ServiceType, Purpose = purpose,
            Priority = entity.Priority, Endpoint = entity.Endpoint,
            EmbeddingModel = purpose.HasFlag(AiServicePurpose.Embedding) ? entity.EmbeddingModel : null,
            LlmModel = purpose.HasFlag(AiServicePurpose.Llm) ? entity.LlmModel : null,
            DisableThinking = entity.DisableThinking, IsDisabled = entity.IsDisabled,
            DefaultRecallTopK = entity.DefaultRecallTopK, HasApiKey = !string.IsNullOrWhiteSpace(entity.ApiKey),
            CreatedAt = entity.CreatedAt, UpdatedAt = entity.UpdatedAt
        };
    }

    private static AiServiceConfigDetailDto ToDetailDto(AiServiceConfig entity)
    {
        var summary = ToDto(entity);
        return new AiServiceConfigDetailDto
        {
            Id = summary.Id, Name = summary.Name, ServiceType = summary.ServiceType, Purpose = summary.Purpose,
            Priority = summary.Priority, Endpoint = summary.Endpoint, EmbeddingModel = summary.EmbeddingModel,
            LlmModel = summary.LlmModel, DisableThinking = summary.DisableThinking, IsDisabled = summary.IsDisabled,
            DefaultRecallTopK = summary.DefaultRecallTopK, HasApiKey = summary.HasApiKey,
            CreatedAt = summary.CreatedAt, UpdatedAt = summary.UpdatedAt, ApiKey = Mask(entity.ApiKey)
        };
    }

    private static string? Mask(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return key;
        var value = key.Trim();
        if (value.Length <= 4) return new string('*', value.Length);
        return value.Length <= 8 ? $"{value[..2]}***{value[^2..]}" : $"{value[..4]}***{value[^4..]}";
    }
}
