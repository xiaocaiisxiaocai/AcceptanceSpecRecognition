using System.IO.Compression;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 批量回复应用服务入口，负责源表上传、目标文件预检、执行写回与下载产物管理。
/// </summary>
public interface IBatchReplyAppService
{
    Task<BatchReplySourceUploadResponse> UploadSourceAsync(
        BatchReplyUserContext user,
        BatchReplyUploadDocument file,
        CancellationToken cancellationToken = default);

    Task<List<TableInfoDto>> GetSourceTablesAsync(
        BatchReplyUserContext user,
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<TableDataDto> GetSourceTablePreviewAsync(
        BatchReplyUserContext user,
        string sessionId,
        int tableIndex,
        int previewRows,
        int headerRowIndex,
        int headerRowCount,
        int dataStartRowIndex,
        CancellationToken cancellationToken = default);

    Task<BatchReplyTargetUploadResponse> UploadTargetsAsync(
        BatchReplyUserContext user,
        string sessionId,
        IReadOnlyCollection<BatchReplyUploadDocument> targetFiles,
        CancellationToken cancellationToken = default);

    Task<List<TableInfoDto>> GetTargetTablesAsync(
        BatchReplyUserContext user,
        string sessionId,
        string targetId,
        CancellationToken cancellationToken = default);

    Task<TableDataDto> GetTargetTablePreviewAsync(
        BatchReplyUserContext user,
        string sessionId,
        string targetId,
        int tableIndex,
        int previewRows,
        int headerRowIndex,
        int headerRowCount,
        int dataStartRowIndex,
        CancellationToken cancellationToken = default);

    Task<MatchingOperationResult<BatchReplyTablePreviewResponse>> TablePreviewAsync(
        BatchReplyUserContext user,
        BatchReplyTablePreviewRequest request,
        CancellationToken cancellationToken = default);

    Task<MatchingOperationResult<BatchReplyPreviewResponse>> PreviewAsync(
        BatchReplyUserContext user,
        string sessionId,
        IReadOnlyCollection<BatchTableConfig> tableConfigs,
        IReadOnlyCollection<BatchReplyUploadDocument> targetFiles,
        CancellationToken cancellationToken = default);

    Task<MatchingOperationResult<BatchReplyExecuteResponse>> ExecuteAsync(
        BatchReplyUserContext user,
        BatchReplyExecuteRequest request,
        CancellationToken cancellationToken = default);

    Task<MatchingDownloadResult> DownloadAsync(
        BatchReplyUserContext user,
        string taskId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 批量回复应用服务。
/// </summary>
public sealed partial class BatchReplyAppService : IBatchReplyAppService
{
    private const string DuplicateSourceKindSource = "source";
    private const string DuplicateSourceKindTarget = "target";
    private const string DuplicateStrategyKeepFirst = "keepFirst";
    private const string DuplicateStrategyKeepLast = "keepLast";
    private const string DuplicateStrategySkip = "skip";

    private readonly IBatchReplyDocumentTablePort _documentTableAccessService;
    private readonly IBatchReplyWriteBackPort _matchingResultWriteBackService;
    private readonly BatchReplySessionService _batchReplySessionService;
    private readonly IBatchReplyExecutionHistoryPort _executionHistoryAppService;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<BatchReplyAppService> _logger;

    public BatchReplyAppService(
        IBatchReplyDocumentTablePort documentTableAccessService,
        IBatchReplyWriteBackPort matchingResultWriteBackService,
        BatchReplySessionService batchReplySessionService,
        IBatchReplyExecutionHistoryPort executionHistoryAppService,
        IFileStorageService fileStorage,
        ILogger<BatchReplyAppService> logger)
    {
        _documentTableAccessService = documentTableAccessService;
        _matchingResultWriteBackService = matchingResultWriteBackService;
        _batchReplySessionService = batchReplySessionService;
        _executionHistoryAppService = executionHistoryAppService;
        _fileStorage = fileStorage;
        _logger = logger;
    }








}
