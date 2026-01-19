namespace AcceptanceSpecSystem.Core.TextProcessing.Interfaces;

public interface IKeywordService
{
    Task<IReadOnlyList<string>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<bool> IsKeywordAsync(string word, CancellationToken cancellationToken = default);
}

