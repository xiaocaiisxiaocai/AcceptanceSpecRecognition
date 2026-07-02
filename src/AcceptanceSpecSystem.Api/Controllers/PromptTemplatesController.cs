using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Core.Matching.Services;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CorePromptTemplateScene = AcceptanceSpecSystem.Core.Matching.Models.PromptTemplateScene;

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

    public PromptTemplatesController(
        IUnitOfWork unitOfWork,
        PromptTemplateValidationService validationService)
    {
        _unitOfWork = unitOfWork;
        _validationService = validationService;
    }

    /// <summary>
    /// 获取Prompt模板列表
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedData<PromptTemplateDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedData<PromptTemplateDto>>>> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var currentSystemTemplateNames = GetCurrentSystemTemplateNames();
        var query = _unitOfWork.PromptTemplates.Query()
            .Where(t => t.IsSystem && currentSystemTemplateNames.Contains(t.Name));
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var key = keyword.Trim();
            query = query.Where(t =>
                t.Name.Contains(key) ||
                t.DisplayName.Contains(key) ||
                t.Content.Contains(key));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(t => t.Scene)
            .ThenByDescending(t => t.UpdatedAt ?? t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
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
    /// 获取模板详情
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<PromptTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PromptTemplateDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PromptTemplateDto>>> GetById(
        int id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.PromptTemplates
            .Query(asNoTracking: false)
            .SingleOrDefaultAsync(template => template.Id == id, cancellationToken);
        if (entity == null)
            return NotFoundResult<PromptTemplateDto>("模板不存在");
        if (!TryGetCurrentSystemDefinition(entity, out _))
            return NotFoundResult<PromptTemplateDto>("模板不存在");

        return Success(ToDto(entity));
    }

    /// <summary>
    /// 更新Prompt模板
    /// </summary>
    [HttpPut("{id:int}")]
    [AuditOperation("update", "prompt-template")]
    [ProducesResponseType(typeof(ApiResponse<PromptTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PromptTemplateDto>>> Update(
        int id,
        [FromBody] UpdatePromptTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.PromptTemplates
            .Query(asNoTracking: false)
            .SingleOrDefaultAsync(template => template.Id == id, cancellationToken);
        if (entity == null)
            return NotFoundResult<PromptTemplateDto>("模板不存在");

        if (string.IsNullOrWhiteSpace(request.Content))
            return Error<PromptTemplateDto>(400, "内容不能为空");

        if (!TryGetCurrentSystemDefinition(entity, out var definition))
        {
            return NotFoundResult<PromptTemplateDto>("模板不存在");
        }

        var validation = _validationService.Validate(definition, request.Content);
        if (!validation.IsValid)
        {
            return Error<PromptTemplateDto>(400, string.Join("；", validation.Errors));
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            entity.DisplayName = request.DisplayName.Trim();
        }

        entity.Content = request.Content;
        entity.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.PromptTemplates.Update(entity);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
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
    /// 恢复系统模板默认内容
    /// </summary>
    [HttpPost("reset-system/{scene}")]
    [AuditOperation("reset-system", "prompt-template")]
    [ProducesResponseType(typeof(ApiResponse<PromptTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PromptTemplateDto>>> ResetSystem(
        string scene,
        CancellationToken cancellationToken = default)
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
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Success(ToDto(entity), "恢复默认成功");
    }

    private static string[] GetCurrentSystemTemplateNames()
    {
        return PromptTemplateCatalog.GetSystemTemplates()
            .Select(item => item.Name)
            .ToArray();
    }

    private static bool TryGetCurrentSystemDefinition(PromptTemplate template, out SystemPromptTemplateDefinition definition)
    {
        definition = null!;
        return template.IsSystem && PromptTemplateCatalog.TryGetByName(template.Name, out definition);
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
            UsageDescription = definition?.UsageDescription ?? string.Empty,
            AvailableVariables = definition?.AvailableVariables.ToList() ?? [],
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt
        };
    }

    private static PromptTemplateScene ToDataPromptTemplateScene(CorePromptTemplateScene scene)
    {
        return scene switch
        {
            CorePromptTemplateScene.MatchingReview => PromptTemplateScene.MatchingReview,
            CorePromptTemplateScene.ImportDuplicateReview => PromptTemplateScene.ImportDuplicateReview,
            CorePromptTemplateScene.MatchingEquivalenceAdjudication => PromptTemplateScene.MatchingEquivalenceAdjudication,
            CorePromptTemplateScene.MatchingCandidateRerank => PromptTemplateScene.MatchingCandidateRerank,
            CorePromptTemplateScene.SmartConfigStructureRecognition => PromptTemplateScene.SmartConfigStructureRecognition,
            _ => PromptTemplateScene.Unknown
        };
    }
}
