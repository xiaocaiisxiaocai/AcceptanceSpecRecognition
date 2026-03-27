using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;

namespace AcceptanceSpecSystem.Core.TextProcessing.Services;

public class KeywordService : IKeywordService
{
    private readonly IKeywordDataProvider _keywordDataProvider;

    public KeywordService(IKeywordDataProvider keywordDataProvider)
    {
        _keywordDataProvider = keywordDataProvider;
    }

    public async Task<IReadOnlyList<string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _keywordDataProvider.GetAllAsync(cancellationToken);
    }

    public async Task<bool> IsKeywordAsync(string word, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;
        return await _keywordDataProvider.IsKeywordAsync(word.Trim(), cancellationToken);
    }
}

