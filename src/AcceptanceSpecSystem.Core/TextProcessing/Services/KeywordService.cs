using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Core.TextProcessing.Services;

public class KeywordService : IKeywordService
{
    private readonly IUnitOfWork _unitOfWork;

    public KeywordService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        // 当前仓库层未提供 cancellationToken，先忽略
        return await _unitOfWork.Keywords.GetAllWordsAsync();
    }

    public async Task<bool> IsKeywordAsync(string word, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;
        return await _unitOfWork.Keywords.IsKeywordAsync(word.Trim());
    }
}

