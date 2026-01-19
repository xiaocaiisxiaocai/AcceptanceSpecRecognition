using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// Prompt模板CRUD API控制器
/// </summary>
[Route("api/prompt-templates")]
public class PromptTemplatesController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PromptTemplatesController> _logger;

    private const string DefaultPromptContent =
        "你是验收规格助手。给定项目与规格内容，请生成验收方法与备注。";

    public PromptTemplatesController(IUnitOfWork unitOfWork, ILogger<PromptTemplatesController> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// 获取Prompt模板列表
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedData<PromptTemplateDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedData<PromptTemplateDto>>>> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null)
    {
        var all = await _unitOfWork.PromptTemplates.GetAllAsync();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            all = all.Where(t =>
                    t.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    t.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var total = all.Count;
        var items = all
            .OrderByDescending(t => t.IsDefault)
            .ThenByDescending(t => t.UpdatedAt ?? t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToDto)
            .ToList();

        return Success(new PagedData<PromptTemplateDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取默认Prompt模板（若不存在则创建）
    /// </summary>
    [HttpGet("default")]
    [ProducesResponseType(typeof(ApiResponse<PromptTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PromptTemplateDto>>> GetDefault()
    {
        var tpl = await _unitOfWork.PromptTemplates.GetOrCreateDefaultAsync(DefaultPromptContent);
        await _unitOfWork.SaveChangesAsync();
        return Success(ToDto(tpl));
    }

    /// <summary>
    /// 获取模板详情
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<PromptTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PromptTemplateDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PromptTemplateDto>>> GetById(int id)
    {
        var entity = await _unitOfWork.PromptTemplates.GetByIdAsync(id);
        if (entity == null)
            return NotFoundResult<PromptTemplateDto>("模板不存在");

        return Success(ToDto(entity));
    }

    /// <summary>
    /// 新增Prompt模板
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<PromptTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PromptTemplateDto>>> Create([FromBody] CreatePromptTemplateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Error<PromptTemplateDto>(400, "名称不能为空");
        if (string.IsNullOrWhiteSpace(request.Content))
            return Error<PromptTemplateDto>(400, "内容不能为空");

        var name = request.Name.Trim();
        var exists = await _unitOfWork.PromptTemplates.GetByNameAsync(name);
        if (exists != null)
            return Error<PromptTemplateDto>(400, "名称已存在");

        var entity = new PromptTemplate
        {
            Name = name,
            Content = request.Content,
            IsDefault = false,
            CreatedAt = DateTime.Now
        };

        await _unitOfWork.PromptTemplates.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        if (request.IsDefault)
        {
            await _unitOfWork.PromptTemplates.SetDefaultAsync(entity.Id);
            await _unitOfWork.SaveChangesAsync();
            entity.IsDefault = true;
        }

        _logger.LogInformation("创建Prompt模板: {Id} {Name}", entity.Id, entity.Name);
        return Success(ToDto(entity), "创建成功");
    }

    /// <summary>
    /// 更新Prompt模板
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ApiResponse<PromptTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PromptTemplateDto>>> Update(int id, [FromBody] UpdatePromptTemplateRequest request)
    {
        var entity = await _unitOfWork.PromptTemplates.GetByIdAsync(id);
        if (entity == null)
            return Error<PromptTemplateDto>(400, "模板不存在");

        if (string.IsNullOrWhiteSpace(request.Name))
            return Error<PromptTemplateDto>(400, "名称不能为空");
        if (string.IsNullOrWhiteSpace(request.Content))
            return Error<PromptTemplateDto>(400, "内容不能为空");

        var newName = request.Name.Trim();
        if (!string.Equals(entity.Name, newName, StringComparison.OrdinalIgnoreCase))
        {
            var exists = await _unitOfWork.PromptTemplates.GetByNameAsync(newName);
            if (exists != null && exists.Id != id)
                return Error<PromptTemplateDto>(400, "名称已存在");
        }

        entity.Name = newName;
        entity.Content = request.Content;
        entity.UpdatedAt = DateTime.Now;
        _unitOfWork.PromptTemplates.Update(entity);

        await _unitOfWork.SaveChangesAsync();

        if (request.IsDefault)
        {
            await _unitOfWork.PromptTemplates.SetDefaultAsync(entity.Id);
            await _unitOfWork.SaveChangesAsync();
            entity.IsDefault = true;
        }

        return Success(ToDto(entity), "更新成功");
    }

    /// <summary>
    /// 删除Prompt模板
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> Delete(int id)
    {
        var entity = await _unitOfWork.PromptTemplates.GetByIdAsync(id);
        if (entity == null)
            return Error(400, "模板不存在");

        _unitOfWork.PromptTemplates.Remove(entity);
        await _unitOfWork.SaveChangesAsync();
        return Success("删除成功");
    }

    /// <summary>
    /// 设置默认Prompt模板
    /// </summary>
    [HttpPost("{id}/set-default")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse>> SetDefault(int id)
    {
        var entity = await _unitOfWork.PromptTemplates.GetByIdAsync(id);
        if (entity == null)
            return Error(400, "模板不存在");

        await _unitOfWork.PromptTemplates.SetDefaultAsync(id);
        await _unitOfWork.SaveChangesAsync();
        return Success("设置默认成功");
    }

    private static PromptTemplateDto ToDto(PromptTemplate t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Content = t.Content,
        IsDefault = t.IsDefault,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt
    };
}

