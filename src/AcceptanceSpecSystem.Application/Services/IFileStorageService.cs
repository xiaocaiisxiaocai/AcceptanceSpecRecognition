namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 文件存储服务（服务器文件系统）
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// 保存上传的Word文件到 uploads/word-files/{yyyy-MM-dd}/{guid}.docx，返回相对路径
    /// </summary>
    Task<string> SaveUploadedWordAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default);

    Task<string> SaveUploadedWordAsync(string originalFileName, Stream content, CancellationToken cancellationToken = default)
        => Task.FromException<string>(new NotSupportedException("当前文件存储实现不支持流式 Word 上传"));

    /// <summary>
    /// 保存上传的 Excel 文件到 uploads/excel-files/{yyyy-MM-dd}/{guid}.xlsx，返回相对路径
    /// </summary>
    Task<string> SaveUploadedExcelAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default);

    Task<string> SaveUploadedExcelAsync(string originalFileName, Stream content, CancellationToken cancellationToken = default)
        => Task.FromException<string>(new NotSupportedException("当前文件存储实现不支持流式 Excel 上传"));

    /// <summary>
    /// 保存填充后的Word文件到 uploads/filled-files/{yyyy-MM-dd}/{guid}.docx，返回相对路径
    /// </summary>
    Task<string> SaveFilledWordAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存智能填充执行历史完整回放归档，返回相对路径
    /// </summary>
    Task<string> SaveSmartFillPlaybackArchiveAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存智能填充最终结果文件的长期存档，返回相对路径。
    /// </summary>
    Task<string> SaveSmartFillResultArchiveAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>
    /// 以只读、异步、顺序读取方式打开存储文件。调用方负责释放返回的流。
    /// </summary>
    Stream OpenReadStream(string relativePath);

    /// <summary>
    /// 写入健康检查临时文件，返回相对路径
    /// </summary>
    Task<string> WriteHealthCheckFileAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 将相对路径转换为绝对路径
    /// </summary>
    string GetAbsolutePath(string relativePath);

    /// <summary>
    /// 删除文件（若存在）
    /// </summary>
    Task DeleteIfExistsAsync(string? relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 仅删除由持久上传流程生成且通过命名空间校验的 WordFile 文件。
    /// </summary>
    Task DeleteUploadedWordFileIfExistsAsync(
        string? relativePath,
        AcceptanceSpecSystem.Data.Entities.UploadedFileType fileType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return Task.CompletedTask;
        if (!WordFileStoragePathPolicy.IsAllowed(relativePath, fileType))
            throw new UnsafeWordFilePathException();
        return Task.FromException(new NotSupportedException("当前文件存储实现未提供持久上传文件安全删除能力"));
    }
}

public static class SmartFillResultArchivePathPolicy
{
    public const string Namespace = "uploads/execution-history/smart-fill-results";

    public static bool IsAllowed(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            return false;

        var normalized = relativePath.Replace('\\', '/');
        if (normalized.Contains("//", StringComparison.Ordinal) ||
            normalized.Split('/').Any(segment => segment is "." or ".."))
            return false;

        var parts = normalized.Split('/');
        if (parts.Length != 5 ||
            parts[0] != "uploads" ||
            parts[1] != "execution-history" ||
            parts[2] != "smart-fill-results" ||
            !DateOnly.TryParseExact(parts[3], "yyyy-MM-dd", out _))
            return false;

        var extension = Path.GetExtension(parts[4]);
        var stem = Path.GetFileNameWithoutExtension(parts[4]);
        return (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) &&
               stem.Length == 32 &&
               stem.All(Uri.IsHexDigit);
    }
}

public sealed class UnsafeWordFilePathException : InvalidOperationException
{
    public UnsafeWordFilePathException() : base("持久文件路径不在允许的上传命名空间内")
    {
    }
}

public static class WordFileStoragePathPolicy
{
    public static bool IsAllowed(
        string? relativePath,
        AcceptanceSpecSystem.Data.Entities.UploadedFileType fileType)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            return false;

        var normalized = relativePath.Replace('\\', '/');
        if (normalized.Contains("//", StringComparison.Ordinal) ||
            normalized.Split('/').Any(segment => segment is "." or ".."))
            return false;

        var parts = normalized.Split('/');
        if (parts.Length != 4 || parts[0] != "uploads")
            return false;

        var expectedNamespace = fileType == AcceptanceSpecSystem.Data.Entities.UploadedFileType.ExcelXlsx
            ? "excel-files"
            : "word-files";
        var expectedExtension = fileType == AcceptanceSpecSystem.Data.Entities.UploadedFileType.ExcelXlsx
            ? ".xlsx"
            : ".docx";
        if (!string.Equals(parts[1], expectedNamespace, StringComparison.Ordinal) ||
            !DateOnly.TryParseExact(parts[2], "yyyy-MM-dd", out _))
            return false;

        var extension = Path.GetExtension(parts[3]);
        var stem = Path.GetFileNameWithoutExtension(parts[3]);
        return string.Equals(extension, expectedExtension, StringComparison.OrdinalIgnoreCase) &&
               stem.Length == 32 &&
               stem.All(Uri.IsHexDigit);
    }
}
