using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Services;

public interface IDocumentFileAccessService
{
    IQueryable<WordFile> ApplyScopedQuery(
        IQueryable<WordFile> query,
        SpecAccessContext scope,
        bool includeScopedSpecs = true);

    Task<Dictionary<int, int>> BuildSpecCountByFileAsync(
        IReadOnlyCollection<int> fileIds,
        SpecAccessContext scope,
        CancellationToken cancellationToken = default);

    Task<WordFile?> GetAccessibleWordFileAsync(
        int fileId,
        SpecAccessContext scope,
        bool includeScopedSpecs = false,
        CancellationToken cancellationToken = default);

    Task<WordFile?> GetAccessibleWordFileAsync(
        int fileId,
        DataScopeResult scope,
        bool includeScopedSpecs = false,
        CancellationToken cancellationToken = default);

    Stream OpenReadStream(WordFile wordFile);

    Task<string> SaveUploadedFileAsync(
        UploadedFileType fileType,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default);

    Task<string> SaveUploadedFileAsync(
        UploadedFileType fileType,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default) =>
        Task.FromException<string>(new NotSupportedException("当前文档文件访问实现不支持流式上传"));

    Task PersistUpdatedFileContentAsync(
        WordFile wordFile,
        byte[] updatedContent,
        CancellationToken cancellationToken = default);

    Task DeleteIfExistsAsync(string? relativePath, CancellationToken cancellationToken = default);
}
