using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Services;

public sealed record BatchReplyUserContext(int UserId, int CompanyId);

public sealed record BatchReplyUploadDocument(
    string FileName,
    UploadedFileType FileType,
    byte[] Content);

public interface IBatchReplyDocumentTablePort
{
    Task<int> CountTablesAsync(
        UploadedFileType fileType,
        byte[] fileContent,
        CancellationToken cancellationToken = default);
    Task<List<TableInfoDto>> GetTableInfoDtosAsync(
        WordFile wordFile,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AcceptanceSpecSystem.Core.Documents.Models.TableInfo>> GetTablesAsync(
        WordFile wordFile,
        CancellationToken cancellationToken = default);
    Task<TableDataDto> GetTablePreviewAsync(
        WordFile wordFile,
        int tableIndex,
        int previewRows,
        int headerRowIndex,
        int headerRowCount,
        int dataStartRowIndex,
        int? dataEndRowIndex = null,
        CancellationToken cancellationToken = default);
    Task<List<MatchSourceItem>> ExtractMatchSourceItemsAsync(
        WordFile wordFile,
        int tableIndex,
        int projectColumnIndex,
        int specificationColumnIndex,
        int? headerRowStart = null,
        int? headerRowCount = null,
        int? dataStartRow = null,
        bool filterEmptySourceRows = true,
        CancellationToken cancellationToken = default);
    Task<List<ReplySourceItem>> ExtractReplySourceItemsAsync(
        WordFile wordFile,
        BatchTableConfig config,
        CancellationToken cancellationToken = default);
}

public sealed class ReplySourceItem
{
    public int RowIndex { get; set; }
    public string Project { get; set; } = string.Empty;
    public string Specification { get; set; } = string.Empty;
    public string Acceptance { get; set; } = string.Empty;
    public string? Remark { get; set; }
}

public interface IBatchReplyWriteBackPort
{
    Task<GeneratedArtifactFile> GenerateTargetFileAsync(
        WordFile targetFile,
        IReadOnlyCollection<BatchReplyWriteTable> writeTables,
        CancellationToken cancellationToken = default);
}

public interface IBatchReplyExecutionHistoryPort
{
    Task SaveAsync(
        BatchReplyUserContext user,
        string taskId,
        BatchReplySourceSession session,
        IReadOnlyCollection<BatchReplyTargetFile> targetFiles,
        IReadOnlyCollection<BatchReplyExecuteFileResult> executeResults,
        IReadOnlyDictionary<string, IReadOnlyCollection<BatchReplyWriteTable>> executionHistoryRows,
        CancellationToken cancellationToken = default);
}
