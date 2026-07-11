namespace AcceptanceSpecSystem.Application.Services;

public interface IEmbeddingCacheWarmupExecutor
{
    Task WarmupAsync(int batchSize, int maxItemsPerRun, CancellationToken cancellationToken);
}
