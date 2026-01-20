using System.Security.Cryptography;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 服务器文件系统存储实现
/// </summary>
public class FileStorageService : IFileStorageService
{
    private readonly string _contentRootPath;

    public FileStorageService(IWebHostEnvironment env)
    {
        _contentRootPath = env.ContentRootPath;
    }

    public async Task<string> SaveUploadedWordAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default)
    {
        return await SaveAsync("uploads/word-files", originalFileName, content, cancellationToken);
    }

    public async Task<string> SaveUploadedExcelAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default)
    {
        return await SaveAsync("uploads/excel-files", originalFileName, content, cancellationToken);
    }

    public async Task<string> SaveFilledWordAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default)
    {
        return await SaveAsync("uploads/filled-files", originalFileName, content, cancellationToken);
    }

    public string GetAbsolutePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("relativePath不能为空", nameof(relativePath));

        // 统一用 OS 分隔符
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(_contentRootPath, normalized));
    }

    public Task DeleteIfExistsAsync(string? relativePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return Task.CompletedTask;

        var fullPath = GetAbsolutePath(relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private async Task<string> SaveAsync(string baseRelativeDir, string originalFileName, byte[] content, CancellationToken cancellationToken)
    {
        if (content == null || content.Length == 0)
            throw new ArgumentException("content不能为空", nameof(content));

        var ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".docx";

        var dateDir = DateTime.Now.ToString("yyyy-MM-dd");
        var fileName = $"{Guid.NewGuid():N}{ext}";

        var relativePath = $"{baseRelativeDir}/{dateDir}/{fileName}";
        var fullPath = GetAbsolutePath(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // 写文件（原子性：先写到临时文件再替换）
        var tempPath = fullPath + ".tmp";
        await File.WriteAllBytesAsync(tempPath, content, cancellationToken);

        if (File.Exists(fullPath))
            File.Delete(fullPath);

        File.Move(tempPath, fullPath);

        return relativePath;
    }

    /// <summary>
    /// 计算文件哈希（可用于诊断/扩展）
    /// </summary>
    public static string ComputeSha256(byte[] content)
    {
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(content)).ToLowerInvariant();
    }
}

