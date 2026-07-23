using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Application.Models;
using AcceptanceSpecSystem.Core.Matching.Services;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using CorePromptTemplateScene = AcceptanceSpecSystem.Core.Matching.Models.PromptTemplateScene;

namespace AcceptanceSpecSystem.Application.Services;

public interface IPromptTemplateAppService
{
    Task<PagedResult<PromptTemplateDto>> GetPagedAsync(int page, int pageSize, string? keyword, CancellationToken cancellationToken = default);
    Task<PromptTemplateDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<PromptTemplateDto> UpdateAsync(int id, UpdatePromptTemplateRequest request, CancellationToken cancellationToken = default);
    PreviewPromptTemplateResponse Preview(PreviewPromptTemplateRequest request);
    Task<PromptTemplateDto> ResetSystemAsync(string scene, CancellationToken cancellationToken = default);
}

public sealed class PromptTemplateAppService : IPromptTemplateAppService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly PromptTemplateValidationService _validationService;

    public PromptTemplateAppService(IUnitOfWork unitOfWork, PromptTemplateValidationService validationService)
    {
        _unitOfWork = unitOfWork;
        _validationService = validationService;
    }

    public async Task<PagedResult<PromptTemplateDto>> GetPagedAsync(int page, int pageSize, string? keyword, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var names = PromptTemplateCatalog.GetSystemTemplates().Select(item => item.Name).ToArray();
        var query = _unitOfWork.PromptTemplates.Query().Where(template => template.IsSystem && names.Contains(template.Name));
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var key = keyword.Trim();
            query = query.Where(template => template.Name.Contains(key) || template.DisplayName.Contains(key) || template.Content.Contains(key));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(template => template.Scene)
            .ThenByDescending(template => template.UpdatedAt ?? template.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<PromptTemplateDto>
        {
            Items = items.Select(ToDto).ToList(), Total = total, Page = page, PageSize = pageSize
        };
    }

    public async Task<PromptTemplateDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.PromptTemplates.Query(asNoTracking: false)
            .SingleOrDefaultAsync(template => template.Id == id, cancellationToken);
        return entity == null || !TryGetDefinition(entity, out _) ? null : ToDto(entity);
    }

    public async Task<PromptTemplateDto> UpdateAsync(int id, UpdatePromptTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.PromptTemplates.Query(asNoTracking: false)
            .SingleOrDefaultAsync(template => template.Id == id, cancellationToken);
        if (entity == null)
            throw new ApplicationServiceException(404, "模板不存在");
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ApplicationServiceException(400, "内容不能为空");
        if (!TryGetDefinition(entity, out var definition))
            throw new ApplicationServiceException(404, "模板不存在");
        var validation = _validationService.Validate(definition, request.Content);
        if (!validation.IsValid)
            throw new ApplicationServiceException(400, string.Join("；", validation.Errors));
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
            entity.DisplayName = request.DisplayName.Trim();
        entity.Content = request.Content;
        entity.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.PromptTemplates.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public PreviewPromptTemplateResponse Preview(PreviewPromptTemplateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Scene))
            throw new ApplicationServiceException(400, "场景不能为空");
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ApplicationServiceException(400, "内容不能为空");
        if (!PromptTemplateCatalog.TryGetByName(request.Scene, out var definition))
            throw new ApplicationServiceException(400, "模板场景不存在");
        var result = _validationService.Validate(definition.Scene, request.Content);
        return new PreviewPromptTemplateResponse
        {
            IsValid = result.IsValid, Errors = result.Errors, RenderedPrompt = result.RenderedPrompt,
            ExampleJson = result.ExampleJson, StructuredOutputIsValid = result.StructuredOutputIsValid,
            StructuredOutputError = result.StructuredOutputError
        };
    }

    public async Task<PromptTemplateDto> ResetSystemAsync(string scene, CancellationToken cancellationToken = default)
    {
        if (!PromptTemplateCatalog.TryGetByName(scene, out var definition))
            throw new ApplicationServiceException(400, "模板场景不存在");
        var entity = await _unitOfWork.PromptTemplates.GetOrCreateSystemAsync(
            ToDataScene(definition.Scene), definition.Name, definition.DisplayName, definition.DefaultContent);
        entity.Content = definition.DefaultContent;
        entity.DisplayName = definition.DisplayName;
        entity.IsSystem = true;
        entity.Scene = ToDataScene(definition.Scene);
        entity.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    private static bool TryGetDefinition(PromptTemplate template, out SystemPromptTemplateDefinition definition)
    {
        definition = null!;
        return template.IsSystem && PromptTemplateCatalog.TryGetByName(template.Name, out definition);
    }

    private static PromptTemplateDto ToDto(PromptTemplate template)
    {
        PromptTemplateCatalog.TryGetByName(template.Name, out var definition);
        return new PromptTemplateDto
        {
            Id = template.Id, Name = template.Name, Scene = template.Name,
            DisplayName = string.IsNullOrWhiteSpace(template.DisplayName) ? template.Name : template.DisplayName,
            Content = template.Content, IsSystem = template.IsSystem,
            UsageDescription = definition?.UsageDescription ?? string.Empty,
            AvailableVariables = definition?.AvailableVariables.ToList() ?? [],
            CreatedAt = template.CreatedAt, UpdatedAt = template.UpdatedAt
        };
    }

    internal static PromptTemplateScene ToDataScene(CorePromptTemplateScene scene) => scene switch
    {
        CorePromptTemplateScene.MatchingReview => PromptTemplateScene.MatchingReview,
        CorePromptTemplateScene.ImportDuplicateReview => PromptTemplateScene.ImportDuplicateReview,
        CorePromptTemplateScene.MatchingEquivalenceAdjudication => PromptTemplateScene.MatchingEquivalenceAdjudication,
        CorePromptTemplateScene.MatchingCandidateRerank => PromptTemplateScene.MatchingCandidateRerank,
        CorePromptTemplateScene.SmartConfigStructureRecognition => PromptTemplateScene.SmartConfigStructureRecognition,
        CorePromptTemplateScene.SmartConfigColumnSemanticRecall => PromptTemplateScene.SmartConfigColumnSemanticRecall,
        _ => PromptTemplateScene.Unknown
    };
}

public sealed class SystemPromptTemplateInitializer
{
    private readonly IUnitOfWork _unitOfWork;
    public SystemPromptTemplateInitializer(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        foreach (var definition in PromptTemplateCatalog.GetSystemTemplates())
        {
            var scene = PromptTemplateAppService.ToDataScene(definition.Scene);
            var entity = await _unitOfWork.PromptTemplates.GetOrCreateSystemAsync(
                scene, definition.Name, definition.DisplayName, definition.DefaultContent);
            var isLegacy =
                (!string.IsNullOrWhiteSpace(definition.LegacyDefaultContent) &&
                 string.Equals(entity.Content.Trim(), definition.LegacyDefaultContent.Trim(), StringComparison.Ordinal)) ||
                (definition.AdditionalLegacyContents?.Any(legacy =>
                    string.Equals(entity.Content.Trim(), legacy.Trim(), StringComparison.Ordinal)) ?? false);
            var changed = false;
            if (entity.Scene != scene) { entity.Scene = scene; changed = true; }
            if (!entity.IsSystem) { entity.IsSystem = true; changed = true; }
            if (string.IsNullOrWhiteSpace(entity.DisplayName)) { entity.DisplayName = definition.DisplayName; changed = true; }
            if (string.IsNullOrWhiteSpace(entity.Content) || isLegacy)
            {
                entity.Content = definition.DefaultContent; entity.UpdatedAt = DateTime.UtcNow; changed = true;
            }
            if (changed) _unitOfWork.PromptTemplates.Update(entity);
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ColumnMappingRuleInitializer
{
    private readonly IColumnMappingRuleAppService _appService;
    public ColumnMappingRuleInitializer(IColumnMappingRuleAppService appService) => _appService = appService;
    public Task<int> EnsureAsync(ColumnMappingTargetField? targetField = null, CancellationToken cancellationToken = default) =>
        _appService.RestoreDefaultsAsync(targetField, cancellationToken);
    public Task<int> RestoreMissingAsync(ColumnMappingTargetField? targetField = null, CancellationToken cancellationToken = default) =>
        _appService.RestoreDefaultsAsync(targetField, cancellationToken);
}
