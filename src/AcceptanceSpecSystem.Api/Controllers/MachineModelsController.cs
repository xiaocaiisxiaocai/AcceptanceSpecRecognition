using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 机型管理API控制器
/// </summary>
[Route("api/machine-models")]
[Authorize]
public class MachineModelsController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthDataScopeService _authDataScopeService;
    private readonly ILogger<MachineModelsController> _logger;

    /// <summary>
    /// 创建机型控制器实例
    /// </summary>
    public MachineModelsController(
        IUnitOfWork unitOfWork,
        IAuthDataScopeService authDataScopeService,
        ILogger<MachineModelsController> logger)
    {
        _unitOfWork = unitOfWork;
        _authDataScopeService = authDataScopeService;
        _logger = logger;
    }

    /// <summary>
    /// 获取机型列表
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedData<MachineModelDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedData<MachineModelDto>>>> GetMachineModels(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
        {
            return Error<PagedData<MachineModelDto>>(401, "会话缺少用户上下文");
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _unitOfWork.MachineModels.Query();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var key = keyword.Trim();
            query = query.Where(m => m.Name.Contains(key));
        }

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new { m.Id, m.Name, m.CreatedAt })
            .ToListAsync();

        var modelIds = rows.Select(m => m.Id).ToList();
        var specCountByModel = await BuildModelSpecCountAsync(modelIds, scope);

        var items = rows.Select(m => new MachineModelDto
        {
            Id = m.Id,
            Name = m.Name,
            CreatedAt = m.CreatedAt,
            SpecCount = specCountByModel.TryGetValue(m.Id, out var count) ? count : 0
        }).ToList();

        var pagedData = new PagedData<MachineModelDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };

        return Success(pagedData);
    }

    /// <summary>
    /// 获取机型详情
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<MachineModelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MachineModelDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MachineModelDto>>> GetMachineModel(int id)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
        {
            return Error<MachineModelDto>(401, "会话缺少用户上下文");
        }

        var model = await _unitOfWork.MachineModels.GetByIdAsync(id);

        if (model == null)
        {
            return NotFoundResult<MachineModelDto>("机型不存在");
        }

        var specCount = await GetModelSpecCountAsync(id, scope);

        var dto = new MachineModelDto
        {
            Id = model.Id,
            Name = model.Name,
            CreatedAt = model.CreatedAt,
            SpecCount = specCount
        };

        return Success(dto);
    }

    /// <summary>
    /// 创建机型
    /// </summary>
    [HttpPost]
    [AuditOperation("create", "machine-model")]
    [ProducesResponseType(typeof(ApiResponse<MachineModelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MachineModelDto>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<MachineModelDto>>> CreateMachineModel([FromBody] CreateMachineModelRequest request)
    {
        var model = new MachineModel
        {
            Name = request.Name,
            CreatedAt = DateTime.Now
        };

        await _unitOfWork.MachineModels.AddAsync(model);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("创建机型成功: {MachineModelId} - {MachineModelName}", model.Id, model.Name);

        var dto = new MachineModelDto
        {
            Id = model.Id,
            Name = model.Name,
            CreatedAt = model.CreatedAt,
            SpecCount = 0
        };

        return Success(dto, "创建机型成功");
    }

    /// <summary>
    /// 更新机型
    /// </summary>
    [HttpPut("{id}")]
    [AuditOperation("update", "machine-model")]
    [ProducesResponseType(typeof(ApiResponse<MachineModelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MachineModelDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MachineModelDto>>> UpdateMachineModel(int id, [FromBody] UpdateMachineModelRequest request)
    {
        var scope = await ResolveSpecScopeAsync();
        if (scope == null)
        {
            return Error<MachineModelDto>(401, "会话缺少用户上下文");
        }

        var model = await _unitOfWork.MachineModels.GetByIdAsync(id);
        if (model == null)
        {
            return NotFoundResult<MachineModelDto>("机型不存在");
        }

        model.Name = request.Name;

        _unitOfWork.MachineModels.Update(model);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("更新机型成功: {MachineModelId} - {MachineModelName}", model.Id, model.Name);

        var dto = new MachineModelDto
        {
            Id = model.Id,
            Name = model.Name,
            CreatedAt = model.CreatedAt,
            SpecCount = await GetModelSpecCountAsync(id, scope)
        };

        return Success(dto, "更新机型成功");
    }

    /// <summary>
    /// 删除机型
    /// </summary>
    [HttpDelete("{id}")]
    [AuditOperation("delete", "machine-model")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteMachineModel(int id)
    {
        var model = await _unitOfWork.MachineModels.GetByIdAsync(id);
        if (model == null)
        {
            return NotFound(ApiResponse.Error(404, "机型不存在"));
        }

        _unitOfWork.MachineModels.Remove(model);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("删除机型成功: {MachineModelId} - {MachineModelName}", model.Id, model.Name);

        return Success("删除机型成功");
    }

    private async Task<DataScopeResult?> ResolveSpecScopeAsync()
    {
        return await SpecDataScopeHelper.ResolveScopeAsync(User, _authDataScopeService);
    }

    private async Task<Dictionary<int, int>> BuildModelSpecCountAsync(
        IReadOnlyCollection<int> modelIds,
        DataScopeResult scope)
    {
        if (modelIds.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        var specs = await _unitOfWork.AcceptanceSpecs.FindAsync(
            s => s.MachineModelId.HasValue && modelIds.Contains(s.MachineModelId.Value));

        return SpecDataScopeHelper.ApplyScope(specs, scope)
            .Where(s => s.MachineModelId.HasValue)
            .GroupBy(s => s.MachineModelId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private async Task<int> GetModelSpecCountAsync(int modelId, DataScopeResult scope)
    {
        var counts = await BuildModelSpecCountAsync([modelId], scope);
        return counts.TryGetValue(modelId, out var count) ? count : 0;
    }
}
