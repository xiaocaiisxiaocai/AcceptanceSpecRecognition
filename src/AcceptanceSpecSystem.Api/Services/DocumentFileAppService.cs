using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Services;

public interface IDocumentFileAppService
{
    Task<PagedData<WordFileDto>> GetFilesAsync(
        DataScopeResult scope,
        int page,
        int pageSize,
        string? keyword,
        CancellationToken cancellationToken = default);

    Task<FileUploadResponse> UploadFileAsync(
        DataScopeResult scope,
        IFormFile file,
        CancellationToken cancellationToken = default);

    Task<WordFile?> FindAccessibleWordFileAsync(DataScopeResult scope, int fileId);

    Task DeleteFileAsync(
        DataScopeResult scope,
        int fileId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 文档资源应用服务。
/// </summary>
public sealed class DocumentFileAppService : IDocumentFileAppService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly DocumentFileAccessService _documentFileAccessService;
    private readonly ILogger<DocumentFileAppService> _logger;

    public DocumentFileAppService(
        IUnitOfWork unitOfWork,
        DocumentFileAccessService documentFileAccessService,
        ILogger<DocumentFileAppService> logger)
    {
        _unitOfWork = unitOfWork;
        _documentFileAccessService = documentFileAccessService;
        _logger = logger;
    }

    public async Task<PagedData<WordFileDto>> GetFilesAsync(
        DataScopeResult scope,
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
        DataScopeResult scope,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        var fileType = UploadFileValidation.ValidateOfficeDocument(file, allowExcel: true, allowWord: true);

        byte[] fileContent;
        using (var memoryStream = new MemoryStream())
        {
            await file.CopyToAsync(memoryStream, cancellationToken);
            fileContent = memoryStream.ToArray();
        }

        var fileHash = FileStorageService.ComputeSha256(fileContent);
        var filePath = await _documentFileAccessService.SaveUploadedFileAsync(
            fileType,
            file.FileName,
            fileContent,
            cancellationToken);

        var wordFile = new WordFile
        {
            CompanyId = scope.CompanyId,
            CreatedByUserId = scope.UserId,
            OwnerOrgUnitId = scope.OrgUnitId,
            FileName = file.FileName,
            FileContent = Array.Empty<byte>(),
            FilePath = filePath,
            FileHash = fileHash,
            UploadedAt = DateTime.UtcNow,
            FileType = fileType
        };

        await _unitOfWork.WordFiles.AddAsync(wordFile);
        await _unitOfWork.SaveChangesAsync();

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

    public Task<WordFile?> FindAccessibleWordFileAsync(DataScopeResult scope, int fileId)
    {
        return _documentFileAccessService.GetAccessibleWordFileAsync(fileId, scope, includeScopedSpecs: true);
    }

    public async Task DeleteFileAsync(
        DataScopeResult scope,
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
