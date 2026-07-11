using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Application.Services;

public interface IFileCompareAppService
{
    Task<FileCompareUploadResponse> UploadAsync(
        SpecAccessContext scope,
        FileCompareUploadDocument fileA,
        FileCompareUploadDocument fileB,
        CancellationToken cancellationToken = default);

    Task<FileComparePreviewResponse> PreviewAsync(
        SpecAccessContext scope,
        FileComparePreviewRequest request,
        CancellationToken cancellationToken = default);

    Task<FileCompareDownloadResult> DownloadAsync(
        SpecAccessContext scope,
        FileComparePreviewRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class FileCompareAppService : IFileCompareAppService
{
    private static readonly JsonSerializerOptions DownloadJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileStorageService _fileStorage;
    private readonly IDocumentFileAccessService _documentFileAccess;
    private readonly IDocumentImportTableReader _tableReader;
    private readonly IFileCompareService _compareService;

    public FileCompareAppService(
        IUnitOfWork unitOfWork,
        IFileStorageService fileStorage,
        IDocumentFileAccessService documentFileAccess,
        IDocumentImportTableReader tableReader,
        IFileCompareService compareService)
    {
        _unitOfWork = unitOfWork;
        _fileStorage = fileStorage;
        _documentFileAccess = documentFileAccess;
        _tableReader = tableReader;
        _compareService = compareService;
    }

    public async Task<FileCompareUploadResponse> UploadAsync(
        SpecAccessContext scope,
        FileCompareUploadDocument fileA,
        FileCompareUploadDocument fileB,
        CancellationToken cancellationToken = default)
    {
        if (fileA.FileType != fileB.FileType)
            throw new ApplicationServiceException(400, "仅支持同类型文件对比");

        return new FileCompareUploadResponse
        {
            FileA = await SaveUploadedFileAsync(scope, fileA, cancellationToken),
            FileB = await SaveUploadedFileAsync(scope, fileB, cancellationToken)
        };
    }

    public async Task<FileComparePreviewResponse> PreviewAsync(
        SpecAccessContext scope,
        FileComparePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var (fileA, fileB) = await LoadPairAsync(scope, request, cancellationToken);
        var result = await _compareService.CompareAsync(fileA, fileB, cancellationToken);
        return ToPreviewResponse(result);
    }

    public async Task<FileCompareDownloadResult> DownloadAsync(
        SpecAccessContext scope,
        FileComparePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await PreviewAsync(scope, request, cancellationToken);
        var content = new MemoryStream();
        try
        {
            await JsonSerializer.SerializeAsync(content, response, DownloadJsonOptions, cancellationToken);
            content.Position = 0;
        }
        catch
        {
            await content.DisposeAsync();
            throw;
        }

        return new FileCompareDownloadResult(
            content,
            "application/json",
            $"compare_{DateTime.UtcNow:yyyyMMddHHmmss}.json");
    }

    private async Task<(WordFile FileA, WordFile FileB)> LoadPairAsync(
        SpecAccessContext scope,
        FileComparePreviewRequest request,
        CancellationToken cancellationToken)
    {
        if (request.FileIdA <= 0 || request.FileIdB <= 0)
            throw new ApplicationServiceException(400, "文件ID不能为空");

        var fileA = await _documentFileAccess.GetAccessibleWordFileAsync(
            request.FileIdA, scope, cancellationToken: cancellationToken);
        var fileB = await _documentFileAccess.GetAccessibleWordFileAsync(
            request.FileIdB, scope, cancellationToken: cancellationToken);
        if (fileA == null || fileB == null)
            throw new ApplicationServiceException(400, "文件不存在");
        if (fileA.FileType != fileB.FileType)
            throw new ApplicationServiceException(400, "仅支持同类型文件对比");
        return (fileA, fileB);
    }

    private async Task<FileUploadResponse> SaveUploadedFileAsync(
        SpecAccessContext scope,
        FileCompareUploadDocument upload,
        CancellationToken cancellationToken)
    {
        var fileHash = Convert.ToHexString(SHA256.HashData(upload.Content)).ToLowerInvariant();
        var existingFile = await _unitOfWork.WordFiles.FirstOrDefaultAsync(file =>
            file.FileHash == fileHash &&
            file.CompanyId == scope.CompanyId &&
            file.CreatedByUserId == scope.UserId &&
            file.OwnerOrgUnitId == scope.OrgUnitId);
        if (existingFile != null)
        {
            if (string.IsNullOrWhiteSpace(existingFile.FilePath) ||
                !File.Exists(_fileStorage.GetAbsolutePath(existingFile.FilePath)))
            {
                existingFile.FilePath = await _documentFileAccess.SaveUploadedFileAsync(
                    existingFile.FileType,
                    existingFile.FileName,
                    upload.Content,
                    cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return ToUploadResponse(existingFile, await CountTablesAsync(existingFile, cancellationToken));
        }

        var filePath = await _documentFileAccess.SaveUploadedFileAsync(
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
        await _unitOfWork.WordFiles.AddAsync(wordFile, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return ToUploadResponse(
            wordFile,
            await CountTablesAsync(wordFile, cancellationToken),
            isDuplicate: false);
    }

    private async Task<int> CountTablesAsync(
        WordFile file,
        CancellationToken cancellationToken)
    {
        var tables = await _tableReader.GetTablesAsync(file, cancellationToken);
        return tables.Count;
    }

    private static FileUploadResponse ToUploadResponse(WordFile file, int tableCount, bool isDuplicate = true) => new()
    {
        FileId = file.Id,
        FileName = file.FileName,
        FileHash = file.FileHash,
        IsDuplicate = isDuplicate,
        TableCount = tableCount,
        TableCountReady = true,
        FileType = file.FileType
    };

    private static FileComparePreviewResponse ToPreviewResponse(FileCompareResult result)
    {
        var items = result.Items.Select(item => new FileCompareDiffItemDto
        {
            DiffType = item.DiffType.ToString(),
            OriginalText = item.OriginalText,
            CurrentText = item.CurrentText,
            DisplayLocation = item.DisplayLocation,
            Location = new FileCompareLocationDto
            {
                DocumentType = item.Location.DocumentType,
                TableIndex = item.Location.TableIndex,
                SheetName = item.Location.SheetName,
                RowIndex = item.Location.RowIndex,
                ColumnIndex = item.Location.ColumnIndex,
                Address = item.Location.Address
            }
        }).ToList();
        return new FileComparePreviewResponse
        {
            FileType = result.FileType,
            Items = items,
            Hunks = result.Hunks.Select(hunk => new FileCompareHunkDto
            {
                StartItemIndex = hunk.StartItemIndex,
                EndItemIndex = hunk.EndItemIndex,
                RangeText = hunk.RangeText,
                Lines = hunk.Lines.Select(line => new FileCompareHunkLineDto
                {
                    LineType = line.LineType,
                    ItemIndex = line.ItemIndex,
                    ChangeGroupId = line.ChangeGroupId,
                    DisplayLocation = line.DisplayLocation,
                    OriginalText = line.OriginalText,
                    CurrentText = line.CurrentText
                }).ToList()
            }).ToList(),
            AddedCount = items.Count(item => item.DiffType == FileCompareDiffType.Added.ToString()),
            RemovedCount = items.Count(item => item.DiffType == FileCompareDiffType.Removed.ToString()),
            ModifiedCount = items.Count(item => item.DiffType == FileCompareDiffType.Modified.ToString()),
            UnchangedCount = items.Count(item => item.DiffType == FileCompareDiffType.Unchanged.ToString()),
            TotalCount = items.Count
        };
    }
}

public sealed record FileCompareUploadDocument(
    string FileName,
    UploadedFileType FileType,
    byte[] Content);

public sealed record FileCompareDownloadResult(
    Stream Content,
    string ContentType,
    string FileName);
