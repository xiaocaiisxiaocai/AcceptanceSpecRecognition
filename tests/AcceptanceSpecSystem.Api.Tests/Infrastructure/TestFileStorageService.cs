using AcceptanceSpecSystem.Api.Services;

namespace AcceptanceSpecSystem.Api.Tests.Infrastructure;

public sealed class TestFileStorageService : IFileStorageService
{
    private readonly string _root;

    public TestFileStorageService(string root)
    {
        _root = root;
    }

    public Task<string> SaveUploadedWordAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default)
        => SaveAsync("uploads/word-files", originalFileName, content, cancellationToken);

    public Task<string> SaveUploadedExcelAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default)
        => SaveAsync("uploads/excel-files", originalFileName, content, cancellationToken);

    public Task<string> SaveFilledWordAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default)
        => SaveAsync("uploads/filled-files", originalFileName, content, cancellationToken);

    public Task<string> SaveSmartFillPlaybackArchiveAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default)
        => SaveAsync("uploads/execution-history/smart-fill", originalFileName, content, cancellationToken);

    public Task<byte[]> ReadSmartFillPlaybackArchiveAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (!normalized.StartsWith("uploads/execution-history/smart-fill/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("智能填充回放归档路径非法");

        return File.ReadAllBytesAsync(GetAbsolutePath(relativePath), cancellationToken);
    }

    public Task<string> WriteHealthCheckFileAsync(CancellationToken cancellationToken = default)
        => SaveAsync("health", "health.txt", Array.Empty<byte>(), cancellationToken);

    public string GetAbsolutePath(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(_root, normalized));
    }

    public Task DeleteIfExistsAsync(string? relativePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return Task.CompletedTask;

        var fullPath = GetAbsolutePath(relativePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.CompletedTask;
    }

    private async Task<string> SaveAsync(string baseRelativeDir, string originalFileName, byte[] content, CancellationToken cancellationToken)
    {
        var ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".docx";

        var dateDir = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var fileName = $"{Guid.NewGuid():N}{ext}";

        var relativePath = $"{baseRelativeDir}/{dateDir}/{fileName}";
        var fullPath = GetAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await File.WriteAllBytesAsync(fullPath, content, cancellationToken);
        return relativePath;
    }
}

