using System.Text.Json;
using AcceptanceSpecSystem.Application.Services;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 只枚举显式受管内容目录；manifest、临时文件与重解析点不进入候选集。
/// </summary>
public sealed class OrphanFileStore(IFileStorageService fileStorage) : IOrphanFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<OrphanFileSnapshot> EnumerateManagedFiles()
    {
        var files = new List<OrphanFileSnapshot>();
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        };

        foreach (var managedNamespace in OrphanFilePathRules.ManagedNamespaces)
        {
            var root = fileStorage.GetAbsolutePath(managedNamespace);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var absolutePath in Directory.EnumerateFiles(root, "*", enumerationOptions))
            {
                var relativeSuffix = Path.GetRelativePath(root, absolutePath).Replace('\\', '/');
                var relativePath = $"{managedNamespace}/{relativeSuffix}";
                if (!OrphanFilePathRules.IsManagedContentPath(relativePath))
                {
                    continue;
                }

                var info = new FileInfo(absolutePath);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                files.Add(new OrphanFileSnapshot(
                    OrphanFilePathRules.Normalize(relativePath),
                    info.LastWriteTimeUtc,
                    info.Length));
            }
        }

        return files;
    }

    public async Task<OrphanReferenceSnapshot> ReadManifestReferencesAsync(
        CancellationToken cancellationToken)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var incompleteNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failures = 0;

        failures += await ReadSessionManifestsAsync(references, incompleteNamespaces, cancellationToken);
        failures += await ReadArtifactManifestsAsync(references, incompleteNamespaces, cancellationToken);

        return new OrphanReferenceSnapshot(references, incompleteNamespaces, failures);
    }

    public async Task<OrphanReferenceProbe> ProbeManifestReferenceAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await ReadManifestReferencesAsync(cancellationToken);
            var normalized = OrphanFilePathRules.Normalize(relativePath);
            return new OrphanReferenceProbe(
                snapshot.ReferencedPaths.Contains(normalized),
                snapshot.IsCompleteFor(normalized),
                snapshot.FailureCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new OrphanReferenceProbe(false, false, 1);
        }
    }

    public Task<bool> DeleteIfUnchangedAsync(
        OrphanFileSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OrphanFilePathRules.IsManagedContentPath(snapshot.RelativePath))
        {
            return Task.FromResult(false);
        }

        var absolutePath = fileStorage.GetAbsolutePath(snapshot.RelativePath);
        var managedNamespace = OrphanFilePathRules.GetManagedNamespace(snapshot.RelativePath);
        if (managedNamespace == null ||
            ContainsReparsePoint(absolutePath, fileStorage.GetAbsolutePath(managedNamespace)))
        {
            return Task.FromResult(false);
        }

        var info = new FileInfo(absolutePath);
        if (!info.Exists ||
            (info.Attributes & FileAttributes.ReparsePoint) != 0 ||
            info.Length != snapshot.Length ||
            info.LastWriteTimeUtc != snapshot.LastWriteTimeUtc.UtcDateTime)
        {
            return Task.FromResult(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(absolutePath);
        return Task.FromResult(true);
    }

    private static bool ContainsReparsePoint(string absolutePath, string managedRoot)
    {
        var root = Path.GetFullPath(managedRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = new FileInfo(absolutePath).Directory;
        while (current != null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            var currentPath = current.FullName
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(currentPath, root, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!currentPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            current = current.Parent;
        }

        return true;
    }

    private async Task<int> ReadSessionManifestsAsync(
        HashSet<string> references,
        HashSet<string> incompleteNamespaces,
        CancellationToken cancellationToken)
    {
        var directory = fileStorage.GetAbsolutePath(BatchReplyCleanupAppService.SessionManifestDirectory);
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var failures = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var payload = await File.ReadAllTextAsync(path, cancellationToken);
                var session = JsonSerializer.Deserialize<BatchReplySourceSession>(payload, JsonOptions);
                var manifestId = Path.GetFileNameWithoutExtension(path);
                if (session == null ||
                    !string.Equals(session.SessionId, manifestId, StringComparison.Ordinal) ||
                    !TryAddReference(references, session.SourceFileRelativePath))
                {
                    throw new InvalidDataException("批量回复会话 manifest 无效");
                }

                foreach (var target in session.TargetFiles.Where(item => !string.IsNullOrWhiteSpace(item.RelativePath)))
                {
                    if (!TryAddReference(references, target.RelativePath!))
                    {
                        throw new InvalidDataException("批量回复会话 manifest 包含非法文件路径");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                failures++;
                incompleteNamespaces.Add(OrphanFilePathRules.WordFilesNamespace);
                incompleteNamespaces.Add(OrphanFilePathRules.ExcelFilesNamespace);
            }
        }

        return failures;
    }

    private async Task<int> ReadArtifactManifestsAsync(
        HashSet<string> references,
        HashSet<string> incompleteNamespaces,
        CancellationToken cancellationToken)
    {
        var directory = fileStorage.GetAbsolutePath(BatchReplyCleanupAppService.ArtifactManifestDirectory);
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var failures = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var payload = await File.ReadAllTextAsync(path, cancellationToken);
                var artifact = JsonSerializer.Deserialize<BatchReplyDownloadArtifact>(payload, JsonOptions);
                var manifestId = Path.GetFileNameWithoutExtension(path);
                if (artifact == null ||
                    !string.Equals(artifact.TaskId, manifestId, StringComparison.Ordinal) ||
                    OrphanFilePathRules.GetManagedNamespace(artifact.RelativePath) != OrphanFilePathRules.FilledFilesNamespace ||
                    !TryAddReference(references, artifact.RelativePath))
                {
                    throw new InvalidDataException("批量回复产物 manifest 无效");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                failures++;
                incompleteNamespaces.Add(OrphanFilePathRules.FilledFilesNamespace);
            }
        }

        return failures;
    }

    private static bool TryAddReference(HashSet<string> references, string path)
    {
        if (!OrphanFilePathRules.IsManagedContentPath(path))
        {
            return false;
        }

        references.Add(OrphanFilePathRules.Normalize(path));
        return true;
    }
}
