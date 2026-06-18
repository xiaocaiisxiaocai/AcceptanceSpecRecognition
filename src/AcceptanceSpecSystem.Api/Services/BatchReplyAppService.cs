using System.IO.Compression;
using System.Security.Claims;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 批量回复应用服务入口，负责源表上传、目标文件预检、执行写回与下载产物管理。
/// </summary>
public interface IBatchReplyAppService
{
    Task<BatchReplySourceUploadResponse> UploadSourceAsync(
        ClaimsPrincipal user,
        IFormFile file,
        CancellationToken cancellationToken = default);

    Task<List<TableInfoDto>> GetSourceTablesAsync(ClaimsPrincipal user, string sessionId);

    Task<TableDataDto> GetSourceTablePreviewAsync(
        ClaimsPrincipal user,
        string sessionId,
        int tableIndex,
        int previewRows,
        int headerRowIndex,
        int headerRowCount,
        int dataStartRowIndex);

    Task<BatchReplyTargetUploadResponse> UploadTargetsAsync(
        ClaimsPrincipal user,
        string sessionId,
        IReadOnlyCollection<IFormFile> targetFiles,
        CancellationToken cancellationToken = default);

    Task<List<TableInfoDto>> GetTargetTablesAsync(ClaimsPrincipal user, string sessionId, string targetId);

    Task<TableDataDto> GetTargetTablePreviewAsync(
        ClaimsPrincipal user,
        string sessionId,
        string targetId,
        int tableIndex,
        int previewRows,
        int headerRowIndex,
        int headerRowCount,
        int dataStartRowIndex);

    Task<MatchingOperationResult<BatchReplyTablePreviewResponse>> TablePreviewAsync(
        ClaimsPrincipal user,
        BatchReplyTablePreviewRequest request,
        CancellationToken cancellationToken = default);

    Task<MatchingOperationResult<BatchReplyPreviewResponse>> PreviewAsync(
        ClaimsPrincipal user,
        string sessionId,
        IReadOnlyCollection<BatchTableConfig> tableConfigs,
        IReadOnlyCollection<IFormFile> targetFiles,
        CancellationToken cancellationToken = default);

    Task<MatchingOperationResult<BatchReplyExecuteResponse>> ExecuteAsync(
        ClaimsPrincipal user,
        BatchReplyExecuteRequest request,
        CancellationToken cancellationToken = default);

    Task<MatchingDownloadResult> DownloadAsync(
        ClaimsPrincipal user,
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

    private readonly DocumentTableAccessService _documentTableAccessService;
    private readonly MatchingResultWriteBackService _matchingResultWriteBackService;
    private readonly BatchReplySessionService _batchReplySessionService;
    private readonly ExecutionHistoryAppService _executionHistoryAppService;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<BatchReplyAppService> _logger;

    public BatchReplyAppService(
        DocumentTableAccessService documentTableAccessService,
        MatchingResultWriteBackService matchingResultWriteBackService,
        BatchReplySessionService batchReplySessionService,
        ExecutionHistoryAppService executionHistoryAppService,
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
