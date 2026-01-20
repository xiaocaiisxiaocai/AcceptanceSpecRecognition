namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 文件存储服务（服务器文件系统）
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// 保存上传的Word文件到 uploads/word-files/{yyyy-MM-dd}/{guid}.docx，返回相对路径
    /// </summary>
    Task<string> SaveUploadedWordAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存上传的 Excel 文件到 uploads/excel-files/{yyyy-MM-dd}/{guid}.xlsx，返回相对路径
    /// </summary>
    Task<string> SaveUploadedExcelAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存填充后的Word文件到 uploads/filled-files/{yyyy-MM-dd}/{guid}.docx，返回相对路径
    /// </summary>
    Task<string> SaveFilledWordAsync(string originalFileName, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将相对路径转换为绝对路径
    /// </summary>
    string GetAbsolutePath(string relativePath);

    /// <summary>
    /// 删除文件（若存在）
    /// </summary>
    Task DeleteIfExistsAsync(string? relativePath, CancellationToken cancellationToken = default);
}

