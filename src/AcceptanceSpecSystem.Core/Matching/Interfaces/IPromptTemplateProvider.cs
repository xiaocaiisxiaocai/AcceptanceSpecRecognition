using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Core.Matching.Interfaces;

public interface IPromptTemplateProvider
{
    Task<PromptTemplateModel> GetOrCreateSystemAsync(
        PromptTemplateScene scene,
        string name,
        string displayName,
        string defaultContent,
        CancellationToken cancellationToken = default);

    Task SaveContentAsync(
        int id,
        string content,
        CancellationToken cancellationToken = default);
}
