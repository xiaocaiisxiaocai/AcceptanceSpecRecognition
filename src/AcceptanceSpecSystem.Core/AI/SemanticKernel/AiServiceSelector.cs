using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

/// <summary>
/// AI 服务选择器（按用途 + 离线优先 + 优先级排序）
/// </summary>
public class AiServiceSelector
{
    private readonly IUnitOfWork _unitOfWork;

    public AiServiceSelector(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<AiServiceConfig>> GetCandidatesAsync(
        AiServicePurpose purpose,
        int? preferredId = null)
    {
        var all = await _unitOfWork.AiServiceConfigs.GetByPurposeAsync(purpose);
        var list = all
            .Where(c => IsConfigUsable(c, purpose))
            .OrderBy(c => IsLocal(c.ServiceType) ? 0 : 1)
            .ThenBy(c => c.Priority)
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

    private static bool IsLocal(AiServiceType type)
    {
        return type is AiServiceType.Ollama or AiServiceType.LMStudio;
    }

    private static bool IsConfigUsable(AiServiceConfig config, AiServicePurpose purpose)
    {
        if (purpose.HasFlag(AiServicePurpose.Llm) && string.IsNullOrWhiteSpace(config.LlmModel))
            return false;

        if (purpose.HasFlag(AiServicePurpose.Embedding) && string.IsNullOrWhiteSpace(config.EmbeddingModel))
            return false;

        return true;
    }
}
