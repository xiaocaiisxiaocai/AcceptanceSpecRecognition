using AcceptanceSpecSystem.Core.AI.Models;

namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

/// <summary>
/// AI 服务选择器（按用途 + 优先级排序）
/// </summary>
public class AiServiceSelector : IAiServiceSelector
{
    private readonly IAiServiceConfigProvider _configProvider;

    public AiServiceSelector(IAiServiceConfigProvider configProvider)
    {
        _configProvider = configProvider;
    }

    public async Task<IReadOnlyList<AiServiceConfigModel>> GetCandidatesAsync(
        AiServicePurpose purpose,
        int? preferredId = null,
        CancellationToken cancellationToken = default)
    {
        var all = await _configProvider.GetByPurposeAsync(purpose, cancellationToken);
        var list = all
            .Where(c => IsConfigUsable(c, purpose))
            .OrderBy(c => c.Priority)
            .ThenByDescending(c => c.UpdatedAt ?? c.CreatedAt)
            .ToList();

        if (preferredId.HasValue)
        {
            var preferred = list.FirstOrDefault(c => c.Id == preferredId.Value);
            if (preferred != null)
            {
                list.Remove(preferred);
                list.Insert(0, preferred);
            }
        }

        return list;
    }

    private static bool IsConfigUsable(AiServiceConfigModel config, AiServicePurpose purpose)
    {
        if (purpose.HasFlag(AiServicePurpose.Llm) && string.IsNullOrWhiteSpace(config.LlmModel))
            return false;

        if (purpose.HasFlag(AiServicePurpose.Embedding) && string.IsNullOrWhiteSpace(config.EmbeddingModel))
            return false;

        return true;
    }
}
