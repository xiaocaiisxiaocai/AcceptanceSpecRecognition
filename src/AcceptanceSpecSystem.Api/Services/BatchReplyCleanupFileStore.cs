using AcceptanceSpecSystem.Application.Services;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// BatchReply 清理用例的本地文件系统适配器。
/// </summary>
public sealed class BatchReplyCleanupFileStore(IFileStorageService fileStorage) : IBatchReplyCleanupStore
{
    public IReadOnlyList<string> EnumerateManifestPaths(string relativeDirectory)
    {
        var absoluteDirectory = fileStorage.GetAbsolutePath(relativeDirectory);
        if (!Directory.Exists(absoluteDirectory))
        {
            return [];
        }

        return Directory.GetFiles(absoluteDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => $"{relativeDirectory}/{Path.GetFileName(path)}")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    public Task<string> ReadTextAsync(string relativePath, CancellationToken cancellationToken)
    {
        return File.ReadAllTextAsync(fileStorage.GetAbsolutePath(relativePath), cancellationToken);
    }

    public Task<bool> DeleteIfExistsAsync(string relativePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var absolutePath = fileStorage.GetAbsolutePath(relativePath);
        if (!File.Exists(absolutePath))
        {
            return Task.FromResult(false);
        }

        File.Delete(absolutePath);
        return Task.FromResult(true);
    }
}
