namespace AcceptanceSpecSystem.Core.TextProcessing.Interfaces;

public interface IKeywordDataProvider
{
    Task<IReadOnlyList<string>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<bool> IsKeywordAsync(string word, CancellationToken cancellationToken = default);
}
