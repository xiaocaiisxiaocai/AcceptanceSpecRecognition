using AcceptanceSpecSystem.Core.TextProcessing.Models;

namespace AcceptanceSpecSystem.Core.TextProcessing.Interfaces;

public interface ISynonymDataProvider
{
    Task<IReadOnlyList<SynonymGroupModel>> GetAllGroupsAsync(CancellationToken cancellationToken = default);
}
