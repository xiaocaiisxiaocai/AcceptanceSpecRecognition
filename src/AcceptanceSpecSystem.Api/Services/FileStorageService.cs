using System.Security.Cryptography;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 服务器文件系统存储实现
/// </summary>
public class FileStorageService : IFileStorageService
{
    private readonly string _basePath;
    private readonly SafeUploadedFileDeleter _safeUploadedFileDeleter;

    public FileStorageService(IWebHostEnvironment env, IConfiguration configuration)
    {
        var configuredBasePath = configuration["FileStorage:BasePath"]?.Trim();
        if (string.IsNullOrWhiteSpace(configuredBasePath))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = Path.GetTempPath();
            }

            _basePath = Path.Combine(localAppData, "AcceptanceSpecSystem", "files");
        }
        else
        {
            _basePath = Path.IsPathRooted(configuredBasePath)
                ? Path.GetFullPath(configuredBasePath)
                : Path.GetFullPath(Path.Combine(env.ContentRootPath, configuredBasePath));
        }

        Directory.CreateDirectory(_basePath);
        _safeUploadedFileDeleter = new SafeUploadedFileDeleter(_basePath);
    }

    public async Task<string> SaveUploadedWordAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default)
    {
        return await SaveAsync("uploads/word-files", originalFileName, content, cancellationToken);
    }

    public async Task<string> SaveUploadedWordAsync(string originalFileName, Stream content, CancellationToken cancellationToken = default)
    {
        return await SaveAsync("uploads/word-files", originalFileName, content, cancellationToken);
    }

    public async Task<string> SaveUploadedExcelAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default)
    {
        return await SaveAsync("uploads/excel-files", originalFileName, content, cancellationToken);
    }

    public async Task<string> SaveUploadedExcelAsync(string originalFileName, Stream content, CancellationToken cancellationToken = default)
    {
        return await SaveAsync("uploads/excel-files", originalFileName, content, cancellationToken);
    }

    public async Task<string> SaveFilledWordAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default)
    {
        return await SaveAsync("uploads/filled-files", originalFileName, content, cancellationToken);
    }

    public async Task<string> SaveSmartFillPlaybackArchiveAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default)
    {
        return await SaveAsync("uploads/execution-history/smart-fill", originalFileName, content, cancellationToken);
    }

    public async Task<string> SaveSmartFillResultArchiveAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default)
    {
        return await SaveAsync(SmartFillResultArchivePathPolicy.Namespace, originalFileName, content, cancellationToken);
    }

    public Stream OpenReadStream(string relativePath)
    {
        var fullPath = GetAbsolutePath(relativePath);
        return new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public async Task<string> WriteHealthCheckFileAsync(CancellationToken cancellationToken = default)
    {
        return await SaveAsync("health", "health.txt", Array.Empty<byte>(), cancellationToken, allowEmptyContent: true);
    }

    public string GetAbsolutePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("relativePath不能为空", nameof(relativePath));

        // 统一用 OS 分隔符
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var baseFullPath = Path.GetFullPath(_basePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(baseFullPath, normalized));
        var baseWithSeparator = baseFullPath + Path.DirectorySeparatorChar;

        // 防止通过相对路径跳出存储根目录
        if (!fullPath.StartsWith(baseWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullPath, baseFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("relativePath 非法，超出存储根目录");
        }

        return fullPath;
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

    public Task DeleteUploadedWordFileIfExistsAsync(
        string? relativePath,
        AcceptanceSpecSystem.Data.Entities.UploadedFileType fileType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(relativePath))
            return Task.CompletedTask;
        if (!WordFileStoragePathPolicy.IsAllowed(relativePath, fileType))
            throw new UnsafeWordFilePathException();

        _safeUploadedFileDeleter.DeleteIfExists(relativePath);
        return Task.CompletedTask;
    }

    private async Task<string> SaveAsync(
        string baseRelativeDir,
        string originalFileName,
        byte[] content,
        CancellationToken cancellationToken,
        bool allowEmptyContent = false)
    {
        if (content == null || (!allowEmptyContent && content.Length == 0))
            throw new ArgumentException("content不能为空", nameof(content));

        var ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".docx";

        var dateDir = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var fileName = $"{Guid.NewGuid():N}{ext}";

        var relativePath = $"{baseRelativeDir}/{dateDir}/{fileName}";
        var fullPath = GetAbsolutePath(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // 写文件（原子性：先写到临时文件再替换）
        var tempPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await WriteAllBytesAsync(tempPath, content, cancellationToken);

            if (FileExists(fullPath))
            {
                MoveFile(tempPath, fullPath, overwrite: true);
            }
            else
            {
                MoveFile(tempPath, fullPath, overwrite: false);
            }
        }
        catch
        {
            try
            {
                if (FileExists(tempPath))
                {
                    DeleteFile(tempPath);
                }
            }
            catch
            {
                // 临时文件清理失败不能遮蔽原始写入/移动异常。
            }

            throw;
        }

        return relativePath;
    }

    private async Task<string> SaveAsync(
        string baseRelativeDir,
        string originalFileName,
        Stream content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead)
            throw new ArgumentException("content必须可读", nameof(content));

        var ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".docx";

        var dateDir = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var relativePath = $"{baseRelativeDir}/{dateDir}/{fileName}";
        var fullPath = GetAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var tempPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var output = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await content.CopyToAsync(output, cancellationToken);
            }

            MoveFile(tempPath, fullPath, overwrite: false);
        }
        catch
        {
            try
            {
                if (FileExists(tempPath))
                    DeleteFile(tempPath);
            }
            catch
            {
            }

            throw;
        }

        return relativePath;
    }

    protected virtual Task WriteAllBytesAsync(
        string path,
        byte[] content,
        CancellationToken cancellationToken)
    {
        return File.WriteAllBytesAsync(path, content, cancellationToken);
    }

    protected virtual bool FileExists(string path)
    {
        return File.Exists(path);
    }

    protected virtual void MoveFile(string sourcePath, string destinationPath, bool overwrite)
    {
        File.Move(sourcePath, destinationPath, overwrite);
    }

    protected virtual void DeleteFile(string path)
    {
        File.Delete(path);
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
