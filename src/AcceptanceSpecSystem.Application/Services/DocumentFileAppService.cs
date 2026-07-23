using System.Security.Cryptography;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Application.Services;

public interface IDocumentFileAppService
{
    Task<PagedData<WordFileDto>> GetFilesAsync(
        SpecAccessContext scope,
        int page,
        int pageSize,
        string? keyword,
        CancellationToken cancellationToken = default);

    Task<FileUploadResponse> UploadFileAsync(
        SpecAccessContext scope,
        DocumentUploadCommand upload,
        CancellationToken cancellationToken = default);

    Task DeleteFileAsync(
        SpecAccessContext scope,
        int fileId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 文档资源应用服务。
/// </summary>
public sealed class DocumentFileAppService : IDocumentFileAppService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDocumentFileAccessService _documentFileAccessService;
    private readonly ILogger<DocumentFileAppService> _logger;

    public DocumentFileAppService(
        IUnitOfWork unitOfWork,
        IDocumentFileAccessService documentFileAccessService,
        ILogger<DocumentFileAppService> logger)
    {
        _unitOfWork = unitOfWork;
        _documentFileAccessService = documentFileAccessService;
        _logger = logger;
    }

    public async Task<PagedData<WordFileDto>> GetFilesAsync(
        SpecAccessContext scope,
        int page,
        int pageSize,
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _documentFileAccessService.ApplyScopedQuery(
                _unitOfWork.WordFiles.Query().Where(file => file.FileName != "__MANUAL_ENTRY__"),
                scope,
                includeScopedSpecs: true);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var key = keyword.Trim();
            query = query.Where(file => file.FileName.Contains(key));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(file => file.UploadedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(file => new
            {
                file.Id,
                file.FileName,
                file.FileType,
                file.FileHash,
                file.UploadedAt
            })
            .ToListAsync(cancellationToken);

        var fileIds = rows.Select(file => file.Id).ToArray();
        var specCountByFile = await _documentFileAccessService.BuildSpecCountByFileAsync(
            fileIds,
            scope,
            cancellationToken);

        return new PagedData<WordFileDto>
        {
            Items = rows.Select(file => new WordFileDto
            {
                Id = file.Id,
                FileName = file.FileName,
                FileType = file.FileType,
                FileHash = file.FileHash,
                UploadedAt = file.UploadedAt,
                SpecCount = specCountByFile.TryGetValue(file.Id, out var count) ? count : 0
            }).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<FileUploadResponse> UploadFileAsync(
        SpecAccessContext scope,
        DocumentUploadCommand upload,
        CancellationToken cancellationToken = default)
    {
        var fileHash = Convert.ToHexString(SHA256.HashData(upload.Content)).ToLowerInvariant();
        var filePath = await _documentFileAccessService.SaveUploadedFileAsync(
            upload.FileType,
            upload.FileName,
            upload.Content,
            cancellationToken);

        var wordFile = new WordFile
        {
            CompanyId = scope.CompanyId,
            CreatedByUserId = scope.UserId,
            OwnerOrgUnitId = scope.OrgUnitId,
            FileName = upload.FileName,
            FileContent = Array.Empty<byte>(),
            FilePath = filePath,
            FileHash = fileHash,
            UploadedAt = DateTime.UtcNow,
            FileType = upload.FileType
        };

        try
        {
            await _unitOfWork.WordFiles.AddAsync(wordFile, cancellationToken);
        }
        catch (Exception addException)
        {
            await TryCleanupFailedUploadAsync(filePath, addException);
            throw;
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception saveException)
        {
            // COMMIT 期间连接中断时数据库结果可能不确定。此时保留文件比误删已提交行所引用的文件更安全，
            // 后续可通过文件/元数据巡检回收确认无引用的孤儿文件。
            _logger.LogError(
                saveException,
                "文件元数据保存结果不确定，保留已落盘文件等待巡检: {FilePath}",
                filePath);
            throw;
        }

        _logger.LogInformation("文件临时上传成功: {FileId} - {FileName}", wordFile.Id, wordFile.FileName);

        return new FileUploadResponse
        {
            FileId = wordFile.Id,
            FileName = wordFile.FileName,
            FileHash = wordFile.FileHash,
            IsDuplicate = false,
            TableCount = 0,
            TableCountReady = false,
            FileType = wordFile.FileType
        };
    }

    private async Task TryCleanupFailedUploadAsync(string filePath, Exception persistenceException)
    {
        try
        {
            await _documentFileAccessService.DeleteIfExistsAsync(filePath, CancellationToken.None);
        }
        catch (Exception cleanupException)
        {
            _logger.LogError(
                cleanupException,
                "文件元数据加入跟踪失败后清理已落盘文件失败: {FilePath}; 原始错误: {PersistenceError}",
                filePath,
                persistenceException.Message);
        }
    }

    public async Task DeleteFileAsync(
        SpecAccessContext scope,
        int fileId,
        CancellationToken cancellationToken = default)
    {
        var wordFile = await _documentFileAccessService.GetAccessibleWordFileAsync(
            fileId,
            scope,
            includeScopedSpecs: true);
        if (wordFile == null)
        {
            throw new ApplicationServiceException(404, "文件不存在");
        }

        var hasSpecs = await _unitOfWork.AcceptanceSpecs
            .Query()
            .AnyAsync(spec => spec.WordFileId == fileId, cancellationToken);
        if (hasSpecs)
        {
            throw new ApplicationServiceException(400, "该文件已有关联的验收规格，无法删除");
        }

        _unitOfWork.WordFiles.Remove(wordFile);
        await _unitOfWork.SaveChangesAsync();
        await _documentFileAccessService.DeleteIfExistsAsync(wordFile.FilePath, cancellationToken);

        _logger.LogInformation("删除文件成功: {FileId} - {FileName}", wordFile.Id, wordFile.FileName);
    }
}

public sealed record DocumentUploadCommand(
    string FileName,
    UploadedFileType FileType,
    byte[] Content);
