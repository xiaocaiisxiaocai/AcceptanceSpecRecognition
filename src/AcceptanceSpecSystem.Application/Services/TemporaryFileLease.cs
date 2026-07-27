namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 受控临时文件句柄。调用方只能打开流，不能取得或伪造底层路径。
/// </summary>
public abstract class TemporaryFileLease : IAsyncDisposable
{
    public abstract long Length { get; }
    public abstract string Sha256 { get; }
    public abstract Stream OpenRead();
    public abstract Stream OpenWrite();
    public abstract ValueTask DisposeAsync();
}
public interface IFileCompareTemporaryStorage
{
    Task<TemporaryFileLease> StageUploadAsync(
        Stream content,
        long maxBytes,
        CancellationToken cancellationToken = default);

    Task<TemporaryFileLease> CreateOutputAsync(CancellationToken cancellationToken = default);

    Task CleanupExpiredAsync(CancellationToken cancellationToken = default);
}
