using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Data.Repositories;

public sealed class DocumentImportExecutionRepository : Repository<DocumentImportExecution>, IDocumentImportExecutionRepository
{
    public DocumentImportExecutionRepository(AppDbContext context) : base(context)
    {
    }

    public Task<DocumentImportExecution?> GetByRequestKeyAsync(
        string requestKey,
        CancellationToken cancellationToken = default)
    {
        return _dbSet.FirstOrDefaultAsync(item => item.RequestKey == requestKey, cancellationToken);
    }

    public Task<int> DeleteExpiredAsync(
        DateTime expiresBefore,
        CancellationToken cancellationToken = default)
    {
        return _dbSet
            .Where(item => item.ExpiresAt <= expiresBefore)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
