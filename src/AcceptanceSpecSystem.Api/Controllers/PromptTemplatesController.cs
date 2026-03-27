using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using CorePromptTemplateScene = AcceptanceSpecSystem.Core.Matching.Models.PromptTemplateScene;
using AcceptanceSpecSystem.Core.Matching.Services;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// Prompt模板CRUD API控制器
/// </summary>
[Route("api/prompt-templates")]
[Authorize]
public class PromptTemplatesController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly PromptTemplateValidationService _validationService;
    private readonly ILogger<PromptTemplatesController> _logger;

    private const string DefaultPromptContent =
        "你是验收规格助手。给定项目与规格内容，请生成验收方法与备注。";

    public PromptTemplatesController(
        IUnitOfWork unitOfWork,
        PromptTemplateValidationService validationService,
        ILogger<PromptTemplatesController> logger)
    {
        _unitOfWork = unitOfWork;
        _validationService = validationService;
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
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        await EnsureSystemTemplatesAsync();

        var query = _unitOfWork.PromptTemplates.Query()
            .Where(t => t.IsSystem);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var key = keyword.Trim();
            query = query.Where(t =>
                t.Name.Contains(key) ||
                t.DisplayName.Contains(key) ||
                t.Content.Contains(key));
        }

        var total = await query.CountAsync();
        var rows = await query
            .OrderBy(t => t.Scene)
            .ThenByDescending(t => t.UpdatedAt ?? t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        var items = rows.Select(ToDto).ToList();

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
        await EnsureSystemTemplatesAsync();

        var entity = await _unitOfWork.PromptTemplates.GetByIdAsync(id);
        if (entity == null)
            return NotFoundResult<PromptTemplateDto>("模板不存在");

        return Success(ToDto(entity));
    }

    /// <summary>
    /// 新增Prompt模板
    /// </summary>
    [HttpPost]
    [AuditOperation("create", "prompt-template")]
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
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? name : request.DisplayName.Trim(),
            Content = request.Content,
            Scene = PromptTemplateScene.Unknown,
            IsSystem = false,
            IsDefault = false,
            CreatedAt = DateTime.UtcNow
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
    [AuditOperation("update", "prompt-template")]
    [ProducesResponseType(typeof(ApiResponse<PromptTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PromptTemplateDto>>> Update(int id, [FromBody] UpdatePromptTemplateRequest request)
    {
        var entity = await _unitOfWork.PromptTemplates.GetByIdAsync(id);
        if (entity == null)
            return Error<PromptTemplateDto>(400, "模板不存在");

        if (string.IsNullOrWhiteSpace(request.Content))
            return Error<PromptTemplateDto>(400, "内容不能为空");

        if (entity.IsSystem)
        {
            var validation = _validationService.Validate(ToCorePromptTemplateScene(entity.Scene), request.Content);
            if (!validation.IsValid)
            {
                return Error<PromptTemplateDto>(400, string.Join("；", validation.Errors));
            }
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            entity.DisplayName = request.DisplayName.Trim();
        }

        entity.Content = request.Content;
        entity.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.PromptTemplates.Update(entity);

        await _unitOfWork.SaveChangesAsync();
        return Success(ToDto(entity), "更新成功");
    }

    /// <summary>
    /// 预览Prompt模板
    /// </summary>
    [HttpPost("preview")]
    [ProducesResponseType(typeof(ApiResponse<PreviewPromptTemplateResponse>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<PreviewPromptTemplateResponse>> Preview([FromBody] PreviewPromptTemplateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Scene))
            return Error<PreviewPromptTemplateResponse>(400, "场景不能为空");
        if (string.IsNullOrWhiteSpace(request.Content))
            return Error<PreviewPromptTemplateResponse>(400, "内容不能为空");

        if (!PromptTemplateCatalog.TryGetByName(request.Scene, out var definition))
            return Error<PreviewPromptTemplateResponse>(400, "模板场景不存在");

        var validation = _validationService.Validate(definition.Scene, request.Content);
        return Success(new PreviewPromptTemplateResponse
        {
            IsValid = validation.IsValid,
            Errors = validation.Errors,
            RenderedPrompt = validation.RenderedPrompt,
            ExampleJson = validation.ExampleJson,
            StructuredOutputIsValid = validation.StructuredOutputIsValid,
            StructuredOutputError = validation.StructuredOutputError
        });
    }

    /// <summary>
    /// 删除Prompt模板
    /// </summary>
    [HttpDelete("{id}")]
    [AuditOperation("delete", "prompt-template")]
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
    [AuditOperation("set-default", "prompt-template")]
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

    /// <summary>
    /// 恢复系统模板默认内容
    /// </summary>
    [HttpPost("reset-system/{scene}")]
    [AuditOperation("reset-system", "prompt-template")]
    [ProducesResponseType(typeof(ApiResponse<PromptTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PromptTemplateDto>>> ResetSystem(string scene)
    {
        if (!PromptTemplateCatalog.TryGetByName(scene, out var definition))
            return Error<PromptTemplateDto>(400, "模板场景不存在");

        var entity = await _unitOfWork.PromptTemplates.GetOrCreateSystemAsync(
            ToDataPromptTemplateScene(definition.Scene),
            definition.Name,
            definition.DisplayName,
            definition.DefaultContent);
        entity.Content = definition.DefaultContent;
        entity.DisplayName = definition.DisplayName;
        entity.IsSystem = true;
        entity.Scene = ToDataPromptTemplateScene(definition.Scene);
        entity.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();

        return Success(ToDto(entity), "恢复默认成功");
    }

    private async Task EnsureSystemTemplatesAsync()
    {
        foreach (var definition in PromptTemplateCatalog.GetSystemTemplates())
        {
            await _unitOfWork.PromptTemplates.GetOrCreateSystemAsync(
                ToDataPromptTemplateScene(definition.Scene),
                definition.Name,
                definition.DisplayName,
                definition.DefaultContent);
        }

        await _unitOfWork.SaveChangesAsync();
    }

    private static PromptTemplateDto ToDto(PromptTemplate template)
    {
        PromptTemplateCatalog.TryGetByName(template.Name, out var definition);
        return new PromptTemplateDto
        {
            Id = template.Id,
            Name = template.Name,
            Scene = template.Name,
            DisplayName = string.IsNullOrWhiteSpace(template.DisplayName) ? template.Name : template.DisplayName,
            Content = template.Content,
            IsSystem = template.IsSystem,
            IsDefault = template.IsDefault,
            UsageDescription = definition?.UsageDescription ?? string.Empty,
            AvailableVariables = definition?.AvailableVariables.ToList() ?? [],
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt
        };
    }

    private static CorePromptTemplateScene ToCorePromptTemplateScene(PromptTemplateScene scene)
    {
        return scene switch
        {
            PromptTemplateScene.MatchingReview => CorePromptTemplateScene.MatchingReview,
            PromptTemplateScene.ImportDuplicateReview => CorePromptTemplateScene.ImportDuplicateReview,
            PromptTemplateScene.MatchingGenerate => CorePromptTemplateScene.MatchingGenerate,
            _ => CorePromptTemplateScene.Unknown
        };
    }

    private static PromptTemplateScene ToDataPromptTemplateScene(CorePromptTemplateScene scene)
    {
        return scene switch
        {
            CorePromptTemplateScene.MatchingReview => PromptTemplateScene.MatchingReview,
            CorePromptTemplateScene.ImportDuplicateReview => PromptTemplateScene.ImportDuplicateReview,
            CorePromptTemplateScene.MatchingGenerate => PromptTemplateScene.MatchingGenerate,
            _ => PromptTemplateScene.Unknown
        };
    }
}
