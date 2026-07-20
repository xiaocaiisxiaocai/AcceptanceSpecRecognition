using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Data.Repositories;

public interface IDocumentImportExecutionRepository : IRepository<DocumentImportExecution>
{
    Task<DocumentImportExecution?> GetByRequestKeyAsync(
        string requestKey,
        CancellationToken cancellationToken = default);

    Task<int> DeleteExpiredAsync(DateTime expiresBefore, CancellationToken cancellationToken = default);
}
