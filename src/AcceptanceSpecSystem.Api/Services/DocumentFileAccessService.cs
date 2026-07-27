using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 文档文件访问协作组件。
/// </summary>
public sealed class DocumentFileAccessService : IDocumentFileAccessService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileStorageService _fileStorage;

    public DocumentFileAccessService(
        IUnitOfWork unitOfWork,
        IFileStorageService fileStorage)
    {
        _unitOfWork = unitOfWork;
        _fileStorage = fileStorage;
    }

    public IQueryable<WordFile> ApplyScopedQuery(
        IQueryable<WordFile> query,
        DataScopeResult scope,
        bool includeScopedSpecs = true)
    {
        var ownershipQuery = WordFileDataScopeHelper.ApplyOwnershipScopeToQuery(query, scope);
        if (!includeScopedSpecs || scope.IsAll)
        {
            return ownershipQuery;
        }

        // 导入/历史文件列表需要包含“文件归属不可见但其中规格可见”的场景，
        // 因此在文件归属范围外额外并入当前数据范围内的规格来源文件。
        var scopedSpecFileIds = SpecDataScopeHelper.ApplyScopeToQuery(
                _unitOfWork.AcceptanceSpecs.Query(),
                scope)
            .Select(spec => spec.WordFileId)
            .Distinct();

        return ownershipQuery.Union(query.Where(file => scopedSpecFileIds.Contains(file.Id)));
    }

    public async Task<Dictionary<int, int>> BuildSpecCountByFileAsync(
        IReadOnlyCollection<int> fileIds,
        DataScopeResult scope,
        CancellationToken cancellationToken = default)
    {
        var normalizedIds = fileIds
            .Where(id => id > 0)
            .Distinct()
            .ToArray();
        if (normalizedIds.Length == 0)
        {
            return [];
        }

        var specsQuery = _unitOfWork.AcceptanceSpecs.Query()
            .Where(spec => normalizedIds.Contains(spec.WordFileId));

        if (!scope.IsAll)
        {
            var scopedOrgUnitIds = scope.OrgUnitIds.Distinct().ToArray();
            if (scope.IncludeSelf && scopedOrgUnitIds.Length > 0)
            {
                specsQuery = specsQuery.Where(spec =>
                    (spec.CreatedByUserId.HasValue && spec.CreatedByUserId.Value == scope.UserId) ||
                    (spec.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(spec.OwnerOrgUnitId.Value)));
            }
            else if (scope.IncludeSelf)
            {
                specsQuery = specsQuery.Where(spec =>
                    spec.CreatedByUserId.HasValue && spec.CreatedByUserId.Value == scope.UserId);
            }
            else if (scopedOrgUnitIds.Length > 0)
            {
                specsQuery = specsQuery.Where(spec =>
                    spec.OwnerOrgUnitId.HasValue && scopedOrgUnitIds.Contains(spec.OwnerOrgUnitId.Value));
            }
            else
            {
                return [];
            }
        }

        return await specsQuery
            .GroupBy(spec => spec.WordFileId)
            .Select(group => new { FileId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.FileId, item => item.Count, cancellationToken);
    }

    public IQueryable<WordFile> ApplyScopedQuery(
        IQueryable<WordFile> query,
        SpecAccessContext scope,
        bool includeScopedSpecs = true)
    {
        var ownershipQuery = scope.ApplyWordFileScopeToQuery(query);
        if (!includeScopedSpecs || scope.IsAll)
            return ownershipQuery;

        var scopedSpecFileIds = scope.ApplySpecScopeToQuery(_unitOfWork.AcceptanceSpecs.Query())
            .Select(spec => spec.WordFileId)
            .Distinct();
        return ownershipQuery.Union(query.Where(file => scopedSpecFileIds.Contains(file.Id)));
    }

    public async Task<Dictionary<int, int>> BuildSpecCountByFileAsync(
        IReadOnlyCollection<int> fileIds,
        SpecAccessContext scope,
        CancellationToken cancellationToken = default)
    {
        var normalizedIds = fileIds.Where(id => id > 0).Distinct().ToArray();
        if (normalizedIds.Length == 0)
            return [];

        return await scope.ApplySpecScopeToQuery(_unitOfWork.AcceptanceSpecs.Query())
            .Where(spec => normalizedIds.Contains(spec.WordFileId))
            .GroupBy(spec => spec.WordFileId)
            .Select(group => new { FileId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.FileId, item => item.Count, cancellationToken);
    }

    public async Task<WordFile?> GetAccessibleWordFileAsync(
        int fileId,
        DataScopeResult scope,
        bool includeScopedSpecs = false,
        CancellationToken cancellationToken = default)
    {
        var wordFile = await _unitOfWork.WordFiles.GetByIdAsync(fileId, cancellationToken);
        if (wordFile == null)
        {
            return null;
        }

        if (WordFileDataScopeHelper.CanAccess(wordFile, scope))
        {
            return wordFile;
        }

        if (!includeScopedSpecs)
        {
            return null;
        }

        var hasScopedSpec = await SpecDataScopeHelper.ApplyScopeToQuery(
                _unitOfWork.AcceptanceSpecs.Query(),
                scope)
            .AnyAsync(spec => spec.WordFileId == fileId, cancellationToken);

        return hasScopedSpec ? wordFile : null;
    }

    public async Task<WordFile?> GetAccessibleWordFileAsync(
        int fileId,
        SpecAccessContext scope,
        bool includeScopedSpecs = false,
        CancellationToken cancellationToken = default)
    {
        var wordFile = await _unitOfWork.WordFiles.GetByIdAsync(fileId, cancellationToken);
        if (wordFile == null)
            return null;
        if (scope.CanAccess(wordFile))
            return wordFile;
        if (!includeScopedSpecs)
            return null;

        var hasScopedSpec = await scope.ApplySpecScopeToQuery(_unitOfWork.AcceptanceSpecs.Query())
            .AnyAsync(spec => spec.WordFileId == fileId, cancellationToken);
        return hasScopedSpec ? wordFile : null;
    }

    public Stream OpenReadStream(WordFile wordFile)
    {
        if (!string.IsNullOrWhiteSpace(wordFile.FilePath))
        {
            var fullPath = _fileStorage.GetAbsolutePath(wordFile.FilePath);
            if (File.Exists(fullPath))
            {
                return File.OpenRead(fullPath);
            }
        }

        if (wordFile.FileContent != null && wordFile.FileContent.Length > 0)
        {
            return new MemoryStream(wordFile.FileContent);
        }

        throw new InvalidOperationException("文件内容不可用（未找到物理文件且数据库内容为空）");
    }

    public Task<string> SaveUploadedFileAsync(
        UploadedFileType fileType,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        return fileType == UploadedFileType.ExcelXlsx
            ? _fileStorage.SaveUploadedExcelAsync(fileName, content, cancellationToken)
            : _fileStorage.SaveUploadedWordAsync(fileName, content, cancellationToken);
    }

    public Task<string> SaveUploadedFileAsync(
        UploadedFileType fileType,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        return fileType == UploadedFileType.ExcelXlsx
            ? _fileStorage.SaveUploadedExcelAsync(fileName, content, cancellationToken)
            : _fileStorage.SaveUploadedWordAsync(fileName, content, cancellationToken);
    }

    public async Task PersistUpdatedFileContentAsync(
        WordFile wordFile,
        byte[] updatedContent,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(wordFile.FilePath))
        {
            var fullPath = _fileStorage.GetAbsolutePath(wordFile.FilePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(tempPath, updatedContent, cancellationToken);

                if (File.Exists(fullPath))
                {
                    File.Move(tempPath, fullPath, overwrite: true);
                }
                else
                {
                    File.Move(tempPath, fullPath);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
        else
        {
            wordFile.FilePath = await SaveUploadedFileAsync(
                wordFile.FileType,
                wordFile.FileName,
                updatedContent,
                cancellationToken);
        }

        wordFile.FileContent = Array.Empty<byte>();
        wordFile.FileHash = FileStorageService.ComputeSha256(updatedContent);
    }

    public Task DeleteIfExistsAsync(string? relativePath, CancellationToken cancellationToken = default)
    {
        return _fileStorage.DeleteIfExistsAsync(relativePath, cancellationToken);
    }
}
