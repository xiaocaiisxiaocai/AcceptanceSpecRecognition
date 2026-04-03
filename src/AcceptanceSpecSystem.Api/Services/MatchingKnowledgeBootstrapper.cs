using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 匹配知识初始化器。
/// </summary>
public sealed class MatchingKnowledgeBootstrapper
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly MatchingKnowledgeOptions _defaultOptions;

    /// <summary>
    /// 初始化匹配知识初始化器。
    /// </summary>
    public MatchingKnowledgeBootstrapper(
        IUnitOfWork unitOfWork,
        IOptions<MatchingKnowledgeOptions> defaultOptions)
    {
        _unitOfWork = unitOfWork;
        _defaultOptions = defaultOptions.Value ?? new MatchingKnowledgeOptions();
    }

    /// <summary>
    /// 确保数据库内存在当前生效的匹配知识配置。
    /// </summary>
    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _unitOfWork.MatchingKnowledgeConfigs.GetConfigAsync();
        if (existing != null)
        {
            return;
        }

        await _unitOfWork.MatchingKnowledgeConfigs.SaveConfigAsync(MatchingKnowledgeComposition.CreateSeedEntity(_defaultOptions));

        await _unitOfWork.SaveChangesAsync();
    }
}
