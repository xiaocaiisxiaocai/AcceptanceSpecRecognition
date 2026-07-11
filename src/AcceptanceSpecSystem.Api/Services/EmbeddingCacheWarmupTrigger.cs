namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 向后台预热服务提交合并信号；最多保留一个待处理触发，避免导入突发产生无界任务。
/// </summary>
public interface IEmbeddingCacheWarmupTrigger
{
    bool Request();

    ValueTask WaitAsync(CancellationToken cancellationToken);
}

public sealed class EmbeddingCacheWarmupTrigger : IEmbeddingCacheWarmupTrigger, IImportWarmupTrigger, IDisposable
{
    private readonly SemaphoreSlim _signal = new(initialCount: 0, maxCount: 1);

    public bool Request()
    {
        try
        {
            _signal.Release();
            return true;
        }
        catch (SemaphoreFullException)
        {
            return false;
        }
    }

    public ValueTask WaitAsync(CancellationToken cancellationToken) =>
        new(_signal.WaitAsync(cancellationToken));

    public void Dispose() => _signal.Dispose();
}
