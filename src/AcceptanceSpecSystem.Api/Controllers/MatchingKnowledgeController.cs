using System.Text.Json;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 匹配知识配置管理接口。
/// </summary>
[Route("api/matching-knowledge")]
[Authorize]
public class MatchingKnowledgeController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly MatchingKnowledgeBootstrapper _bootstrapper;
    private readonly MatchingKnowledgeOptions _defaultOptions;

    public MatchingKnowledgeController(
        IUnitOfWork unitOfWork,
        MatchingKnowledgeBootstrapper bootstrapper,
        IOptions<MatchingKnowledgeOptions> defaultOptions)
    {
        _unitOfWork = unitOfWork;
        _bootstrapper = bootstrapper;
        _defaultOptions = defaultOptions.Value ?? new MatchingKnowledgeOptions();
    }

    /// <summary>
    /// 获取当前生效的匹配知识配置。
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<MatchingKnowledgeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<MatchingKnowledgeDto>>> Get()
    {
        await _bootstrapper.EnsureInitializedAsync();
        var entity = await _unitOfWork.MatchingKnowledgeConfigs.GetConfigAsync();
        if (entity == null)
        {
            return Error<MatchingKnowledgeDto>(500, "匹配知识初始化失败");
        }

        return Success(ToDto(entity));
    }

    /// <summary>
    /// 保存当前匹配知识配置。
    /// </summary>
    [HttpPut]
    [AuditOperation("update", "matching-knowledge")]
    [ProducesResponseType(typeof(ApiResponse<MatchingKnowledgeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<MatchingKnowledgeDto>>> Put([FromBody] UpdateMatchingKnowledgeRequest request)
    {
        var normalized = NormalizeRequest(request);
        var entity = new MatchingKnowledgeConfig
        {
            EntityAliasesJson = JsonSerializer.Serialize(normalized.EntityAliases),
            UnitAliasesJson = JsonSerializer.Serialize(normalized.UnitAliases),
            UnitFactorsJson = JsonSerializer.Serialize(normalized.UnitFactors),
            FieldAliasesJson = JsonSerializer.Serialize(normalized.FieldAliases),
            ConflictPairsJson = JsonSerializer.Serialize(normalized.ConflictPairs.Select(item => item.ToOption()).ToList()),
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.MatchingKnowledgeConfigs.SaveConfigAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        var saved = await _unitOfWork.MatchingKnowledgeConfigs.GetConfigAsync();
        return Success(ToDto(saved!), "保存成功");
    }

    /// <summary>
    /// 重置为系统默认匹配知识配置。
    /// </summary>
    [HttpPost("reset")]
    [AuditOperation("reset", "matching-knowledge")]
    [ProducesResponseType(typeof(ApiResponse<MatchingKnowledgeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<MatchingKnowledgeDto>>> Reset()
    {
        var entity = BuildDefaultEntity(_defaultOptions);
        await _unitOfWork.MatchingKnowledgeConfigs.SaveConfigAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        var saved = await _unitOfWork.MatchingKnowledgeConfigs.GetConfigAsync();
        return Success(ToDto(saved!), "已重置为系统默认配置");
    }

    private static UpdateMatchingKnowledgeRequest NormalizeRequest(UpdateMatchingKnowledgeRequest request)
    {
        return new UpdateMatchingKnowledgeRequest
        {
            EntityAliases = NormalizeStringDictionary(request.EntityAliases),
            UnitAliases = NormalizeStringDictionary(request.UnitAliases),
            UnitFactors = NormalizeDecimalDictionary(request.UnitFactors),
            FieldAliases = NormalizeStringDictionary(request.FieldAliases),
            ConflictPairs = request.ConflictPairs
                .Where(item => !string.IsNullOrWhiteSpace(item.Left) && !string.IsNullOrWhiteSpace(item.Right))
                .Select(item => new ConflictPairDto
                {
                    Left = item.Left.Trim(),
                    Right = item.Right.Trim()
                })
                .ToList()
        };
    }

    private static MatchingKnowledgeConfig BuildDefaultEntity(MatchingKnowledgeOptions options)
    {
        var normalized = NormalizeRequest(new UpdateMatchingKnowledgeRequest
        {
            EntityAliases = new Dictionary<string, string>(options.EntityAliases, StringComparer.OrdinalIgnoreCase),
            UnitAliases = new Dictionary<string, string>(options.UnitAliases, StringComparer.OrdinalIgnoreCase),
            UnitFactors = new Dictionary<string, decimal>(options.UnitFactors, StringComparer.OrdinalIgnoreCase),
            FieldAliases = new Dictionary<string, string>(options.FieldAliases, StringComparer.OrdinalIgnoreCase),
            ConflictPairs = options.ConflictPairs.Select(item => new ConflictPairDto
            {
                Left = item.Left,
                Right = item.Right
            }).ToList()
        });

        return new MatchingKnowledgeConfig
        {
            EntityAliasesJson = JsonSerializer.Serialize(normalized.EntityAliases),
            UnitAliasesJson = JsonSerializer.Serialize(normalized.UnitAliases),
            UnitFactorsJson = JsonSerializer.Serialize(normalized.UnitFactors),
            FieldAliasesJson = JsonSerializer.Serialize(normalized.FieldAliases),
            ConflictPairsJson = JsonSerializer.Serialize(normalized.ConflictPairs.Select(item => item.ToOption()).ToList()),
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static MatchingKnowledgeDto ToDto(MatchingKnowledgeConfig entity)
    {
        return new MatchingKnowledgeDto
        {
            EntityAliases = DeserializeStringDictionary(entity.EntityAliasesJson),
            UnitAliases = DeserializeStringDictionary(entity.UnitAliasesJson),
            UnitFactors = DeserializeDecimalDictionary(entity.UnitFactorsJson),
            FieldAliases = DeserializeStringDictionary(entity.FieldAliasesJson),
            ConflictPairs = DeserializeConflictPairs(entity.ConflictPairsJson)
        };
    }

    private static Dictionary<string, string> NormalizeStringDictionary(Dictionary<string, string> source)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            var key = pair.Key?.Trim();
            var value = pair.Value?.Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            result[key] = value;
        }

        return result;
    }

    private static Dictionary<string, decimal> NormalizeDecimalDictionary(Dictionary<string, decimal> source)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            var key = pair.Key?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            result[key] = pair.Value;
        }

        return result;
    }

    private static Dictionary<string, string> DeserializeStringDictionary(string json)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        return new Dictionary<string, string>(raw, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, decimal> DeserializeDecimalDictionary(string json)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, decimal>>(json) ?? [];
        return new Dictionary<string, decimal>(raw, StringComparer.OrdinalIgnoreCase);
    }

    private static List<ConflictPairDto> DeserializeConflictPairs(string json)
    {
        return JsonSerializer.Deserialize<List<ConflictPairDto>>(json) ?? [];
    }
}
