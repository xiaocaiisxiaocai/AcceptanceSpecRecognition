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
    private readonly IResourceBudgetGovernor _resourceBudgetGovernor;
    private readonly IFileCompareTemporaryStorage _temporaryStorage;

    public FileCompareAppService(
        IUnitOfWork unitOfWork,
        IFileStorageService fileStorage,
        IDocumentFileAccessService documentFileAccess,
        IDocumentImportTableReader tableReader,
        IFileCompareService compareService,
        IResourceBudgetGovernor resourceBudgetGovernor,
        IFileCompareTemporaryStorage temporaryStorage)
    {
        _unitOfWork = unitOfWork;
        _fileStorage = fileStorage;
        _documentFileAccess = documentFileAccess;
        _tableReader = tableReader;
        _compareService = compareService;
        _resourceBudgetGovernor = resourceBudgetGovernor;
        _temporaryStorage = temporaryStorage;
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
        using var resourceLease = await _resourceBudgetGovernor.AcquireAsync(
            ResourceWorkload.DocumentParsing, cancellationToken);
        var result = await _compareService.CompareAsync(fileA, fileB, cancellationToken);
        return ToPreviewResponse(result, request.IncludeUnchanged, cancellationToken);
    }

    public async Task<FileCompareDownloadResult> DownloadAsync(
        SpecAccessContext scope,
        FileComparePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var (fileA, fileB) = await LoadPairAsync(scope, request, cancellationToken);
        using var resourceLease = await _resourceBudgetGovernor.AcquireAsync(
            ResourceWorkload.DocumentParsing, cancellationToken);
        var result = await _compareService.CompareAsync(fileA, fileB, cancellationToken);
        var response = ToPreviewResponse(result, includeUnchanged: true, cancellationToken);
        var output = await _temporaryStorage.CreateOutputAsync(cancellationToken);
        try
        {
            await using (var raw = output.OpenWrite())
            await using (var bounded = new FileCompareResultWriteStream(raw, _resourceBudgetGovernor))
            {
                await JsonSerializer.SerializeAsync(bounded, response, DownloadJsonOptions, cancellationToken);
                await bounded.FlushAsync(cancellationToken);
            }
            var content = new LeaseOwnedReadStream(output.OpenRead(), output);
            output = null!;
            return new FileCompareDownloadResult(
                content,
                "application/json",
                $"compare_{DateTime.UtcNow:yyyyMMddHHmmss}.json");
        }
        catch
        {
            if (output != null)
                await output.DisposeAsync();
            throw;
        }
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
        var fileHash = upload.Sha256;
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
                using var replacement = upload.Content.OpenRead();
                existingFile.FilePath = await _documentFileAccess.SaveUploadedFileAsync(
                    existingFile.FileType, existingFile.FileName, replacement, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return ToUploadResponse(existingFile, await CountTablesAsync(existingFile, cancellationToken));
        }

        using var content = upload.Content.OpenRead();
        var filePath = await _documentFileAccess.SaveUploadedFileAsync(
            upload.FileType, upload.FileName, content, cancellationToken);
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

    private static FileComparePreviewResponse ToPreviewResponse(
        FileCompareResult result,
        bool includeUnchanged,
        CancellationToken cancellationToken)
    {
        var items = new List<FileCompareDiffItemDto>();
        var added = 0;
        var removed = 0;
        var modified = 0;
        var unchanged = 0;
        foreach (var item in result.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (item.DiffType)
            {
                case FileCompareDiffType.Added: added++; break;
                case FileCompareDiffType.Removed: removed++; break;
                case FileCompareDiffType.Modified: modified++; break;
                default: unchanged++; break;
            }
            if (!includeUnchanged && item.DiffType == FileCompareDiffType.Unchanged)
                continue;
            items.Add(new FileCompareDiffItemDto
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
            });
        }
        var hunks = new List<FileCompareHunkDto>(result.Hunks.Count);
        foreach (var hunk in result.Hunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lines = new List<FileCompareHunkLineDto>(hunk.Lines.Count);
            foreach (var line in hunk.Lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lines.Add(new FileCompareHunkLineDto
                {
                    LineType = line.LineType,
                    ItemIndex = line.ItemIndex,
                    ChangeGroupId = line.ChangeGroupId,
                    DisplayLocation = line.DisplayLocation,
                    OriginalText = line.OriginalText,
                    CurrentText = line.CurrentText
                });
            }
            hunks.Add(new FileCompareHunkDto
            {
                StartItemIndex = hunk.StartItemIndex,
                EndItemIndex = hunk.EndItemIndex,
                RangeText = hunk.RangeText,
                Lines = lines
            });
        }
        return new FileComparePreviewResponse
        {
            FileType = result.FileType,
            Items = items,
            Hunks = hunks,
            AddedCount = added,
            RemovedCount = removed,
            ModifiedCount = modified,
            UnchangedCount = unchanged,
            TotalCount = added + removed + modified + unchanged
        };
    }
}

public sealed record FileCompareUploadDocument(
    string FileName,
    UploadedFileType FileType,
    long Length,
    string Sha256,
    TemporaryFileLease Content);

public sealed record FileCompareDownloadResult(
    Stream Content,
    string ContentType,
    string FileName);

internal sealed class FileCompareResultWriteStream(
    Stream inner,
    IResourceBudgetGovernor resourceBudgetGovernor) : Stream
{
    private long _written;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => _written;
    public override long Position { get => _written; set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override void Write(byte[] buffer, int offset, int count)
    {
        Validate(count);
        inner.Write(buffer, offset, count);
    }
    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        Validate(buffer.Length);
        await inner.WriteAsync(buffer, cancellationToken);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync();
        GC.SuppressFinalize(this);
    }
    private void Validate(int count)
    {
        var next = checked(_written + count);
        resourceBudgetGovernor.ValidateFileCompareResultBytes(next);
        _written = next;
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

internal sealed class LeaseOwnedReadStream(Stream inner, TemporaryFileLease lease) : Stream
{
    private int _disposed;
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        inner.ReadAsync(buffer, cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void Flush() { }
    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            inner.Dispose();
            lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        base.Dispose(disposing);
    }
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await inner.DisposeAsync();
            await lease.DisposeAsync();
        }
        GC.SuppressFinalize(this);
    }
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
