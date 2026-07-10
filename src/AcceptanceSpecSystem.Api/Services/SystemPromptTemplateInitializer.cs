using AcceptanceSpecSystem.Core.Matching.Services;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using CorePromptTemplateScene = AcceptanceSpecSystem.Core.Matching.Models.PromptTemplateScene;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 启动期补齐当前保留的系统 Prompt 模板，避免读接口产生写副作用。
/// </summary>
public sealed class SystemPromptTemplateInitializer
{
    private readonly IUnitOfWork _unitOfWork;

    public SystemPromptTemplateInitializer(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        foreach (var definition in PromptTemplateCatalog.GetSystemTemplates())
        {
            await EnsureTemplateAsync(definition, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync();
    }

    private async Task EnsureTemplateAsync(SystemPromptTemplateDefinition definition, CancellationToken cancellationToken)
    {
        var scene = ToDataPromptTemplateScene(definition.Scene);
        var entity = await _unitOfWork.PromptTemplates.GetOrCreateSystemAsync(
            scene,
            definition.Name,
            definition.DisplayName,
            definition.DefaultContent);

        var isLegacy =
            (!string.IsNullOrWhiteSpace(definition.LegacyDefaultContent) &&
             string.Equals(entity.Content.Trim(), definition.LegacyDefaultContent.Trim(), StringComparison.Ordinal)) ||
            (definition.AdditionalLegacyContents != null &&
             definition.AdditionalLegacyContents.Any(legacy =>
                 string.Equals(entity.Content.Trim(), legacy.Trim(), StringComparison.Ordinal)));

        var shouldUpgradeContent =
            string.IsNullOrWhiteSpace(entity.Content) || isLegacy;

        var changed = false;
        if (entity.Scene != scene)
        {
            entity.Scene = scene;
            changed = true;
        }

        if (!entity.IsSystem)
        {
            entity.IsSystem = true;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(entity.DisplayName))
        {
            entity.DisplayName = definition.DisplayName;
            changed = true;
        }

        if (shouldUpgradeContent)
        {
            entity.Content = definition.DefaultContent;
            entity.UpdatedAt = DateTime.UtcNow;
            changed = true;
        }

        if (changed)
        {
            _unitOfWork.PromptTemplates.Update(entity);
        }
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
            CorePromptTemplateScene.SmartConfigColumnSemanticRecall => PromptTemplateScene.SmartConfigColumnSemanticRecall,
            _ => PromptTemplateScene.Unknown
        };
    }
}
