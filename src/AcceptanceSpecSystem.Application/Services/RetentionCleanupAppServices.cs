using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Application.Services;

public interface IAuditLogRetentionAppService
{
    Task<int> DeleteBeforeAsync(DateTime beforeTime, CancellationToken cancellationToken = default);
}

public sealed class AuditLogRetentionAppService : IAuditLogRetentionAppService
{
    private readonly IUnitOfWork _unitOfWork;

    public AuditLogRetentionAppService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task<int> DeleteBeforeAsync(DateTime beforeTime, CancellationToken cancellationToken = default) =>
        _unitOfWork.AuditLogs.DeleteBeforeAsync(beforeTime, cancellationToken);
}

public interface IEmbeddingCacheRetentionAppService
{
    Task<int> DeleteExpiredAsync(DateTime beforeTime, CancellationToken cancellationToken = default);
}

public sealed class EmbeddingCacheRetentionAppService : IEmbeddingCacheRetentionAppService
{
    private readonly IUnitOfWork _unitOfWork;

    public EmbeddingCacheRetentionAppService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task<int> DeleteExpiredAsync(DateTime beforeTime, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _unitOfWork.EmbeddingCaches.DeleteExpiredAsync(beforeTime);
    }
}
