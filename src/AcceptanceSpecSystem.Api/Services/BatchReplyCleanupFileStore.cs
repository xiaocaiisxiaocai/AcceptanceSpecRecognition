using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Data.Context;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// BatchReply 清理用例的本地文件系统适配器。
/// </summary>
public sealed class BatchReplyCleanupFileStore(
    IFileStorageService fileStorage,
    IServiceScopeFactory scopeFactory,
    IBatchReplyDistributedLockProvider distributedLocks,
    ILogger<BatchReplyCleanupFileStore> logger) : IBatchReplyCleanupStore
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

    public async Task<bool> IsContentPathReferencedAsync(
        string relativePath,
        string excludingManifestPath,
        CancellationToken cancellationToken)
    {
        foreach (var directory in new[]
                 {
                     BatchReplyCleanupAppService.SessionManifestDirectory,
                     BatchReplyCleanupAppService.ArtifactManifestDirectory
                 })
        {
            foreach (var manifestPath in EnumerateManifestPaths(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.Equals(manifestPath, excludingManifestPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    await using var stream = File.OpenRead(fileStorage.GetAbsolutePath(manifestPath));
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                    if (ContainsStringValue(document.RootElement, relativePath))
                    {
                        return true;
                    }
                }
                catch (FileNotFoundException)
                {
                    // 清单可能刚被另一个正常流程原子替换；继续进行数据库引用检查。
                }
                catch (JsonException ex)
                {
                    // 无法证明损坏清单不引用目标文件时采取保守策略，避免误删。
                    logger.LogWarning(ex, "清理引用检查遇到损坏清单，保留候选文件: {ManifestPath}", manifestPath);
                    return true;
                }
            }
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.WordFiles
            .AsNoTracking()
            .AnyAsync(file => file.FilePath == relativePath, cancellationToken);
    }

    public async Task<IBatchReplyCleanupLease?> TryAcquireCleanupLeaseAsync(CancellationToken cancellationToken)
    {
        return await distributedLocks.TryAcquireAsync("cleanup", TimeSpan.Zero, cancellationToken) is { } lease
            ? new CleanupLeaseAdapter(lease)
            : null;
    }

    private static bool ContainsStringValue(JsonElement element, string expected)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => string.Equals(element.GetString(), expected, StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray().Any(child => ContainsStringValue(child, expected)),
            JsonValueKind.Object => element.EnumerateObject().Any(property => ContainsStringValue(property.Value, expected)),
            _ => false
        };
    }

    private sealed class CleanupLeaseAdapter(IAsyncDisposable lease) : IBatchReplyCleanupLease
    {
        public ValueTask DisposeAsync() => lease.DisposeAsync();
    }
}
