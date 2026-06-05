using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using AcceptanceSpecSystem.Core.TextProcessing.Interfaces;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 智能匹配共享协作组件。
/// </summary>
public sealed partial class MatchingWorkflowSupportService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMatchingService _matchingService;
    private readonly DocumentFileAccessService _documentFileAccessService;
    private readonly DocumentTableAccessService _documentTableAccessService;
    private readonly MatchingResultWriteBackService _matchingResultWriteBackService;
    private readonly ITextPreprocessingPipeline _textPipeline;
    private readonly IAuthDataScopeService _authDataScopeService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MatchingTaskSnapshotService _matchingTaskSnapshotService;
    private readonly ExecutionHistoryAppService _executionHistoryAppService;
    private readonly MatchingApprovalTokenService _approvalTokenService;
    private readonly MatchingConfigResolver _matchingConfigResolver;
    private readonly MatchingCandidateProvider _matchingCandidateProvider;
    private readonly ILogger<MatchingWorkflowSupportService> _logger;

    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class ExecutionMatchSnapshot
    {
        public Dictionary<int, MatchResult> MatchLookup { get; init; } = [];

        public Dictionary<int, MatchSourceItem> SourceRowLookup { get; init; } = [];
    }

    private sealed class LlmStreamItemContext
    {
        public required MatchLlmStreamItem Item { get; init; }

        public MatchResultDto? AuthoritativeBestMatch { get; init; }
    }

    private readonly record struct LlmStreamItemKey(int? TableIndex, int RowIndex);

    private sealed class LlmStepFailureException : Exception
    {
        public LlmStepFailureException(string eventMessage, string? decision = null, Exception? innerException = null)
            : base(eventMessage, innerException)
        {
            EventMessage = eventMessage;
            Decision = decision;
        }

        public string EventMessage { get; }

        public string? Decision { get; }
    }

    /// <summary>
    /// 创建匹配工作流协作组件实例。
    /// </summary>
    public MatchingWorkflowSupportService(
        IUnitOfWork unitOfWork,
        IMatchingService matchingService,
        DocumentFileAccessService documentFileAccessService,
        DocumentTableAccessService documentTableAccessService,
        MatchingResultWriteBackService matchingResultWriteBackService,
        ITextPreprocessingPipeline textPipeline,
        IAuthDataScopeService authDataScopeService,
        IServiceScopeFactory scopeFactory,
        MatchingTaskSnapshotService matchingTaskSnapshotService,
        ExecutionHistoryAppService executionHistoryAppService,
        MatchingApprovalTokenService approvalTokenService,
        MatchingConfigResolver matchingConfigResolver,
        MatchingCandidateProvider matchingCandidateProvider,
        ILogger<MatchingWorkflowSupportService> logger)
    {
        _unitOfWork = unitOfWork;
        _matchingService = matchingService;
        _documentFileAccessService = documentFileAccessService;
        _documentTableAccessService = documentTableAccessService;
        _matchingResultWriteBackService = matchingResultWriteBackService;
        _textPipeline = textPipeline;
        _authDataScopeService = authDataScopeService;
        _scopeFactory = scopeFactory;
        _matchingTaskSnapshotService = matchingTaskSnapshotService;
        _executionHistoryAppService = executionHistoryAppService;
        _approvalTokenService = approvalTokenService;
        _matchingConfigResolver = matchingConfigResolver;
        _matchingCandidateProvider = matchingCandidateProvider;
        _logger = logger;
    }

    private static MatchingOperationResult<T> Result<T>(T data, string message = "操作成功")
    {
        return new MatchingOperationResult<T>(data, message);
    }

    private static MatchingApiException Failure(int code, string message)
    {
        return new MatchingApiException(code, message);
    }

    private static MatchingApiException NotFoundFailure(string message)
    {
        return new MatchingApiException(404, message, isNotFound: true);
    }




}

public readonly record struct MatchingOperationResult<T>(T Data, string Message);

public readonly record struct MatchingDownloadResult(byte[] Content, string ContentType, string FileName);

internal sealed class MatchingApiException : Exception
{
    public MatchingApiException(int code, string message, bool isNotFound = false)
        : base(message)
    {
        Code = code;
        IsNotFound = isNotFound;
    }

    public int Code { get; }

    public bool IsNotFound { get; }
}

/// <summary>
/// 填充任务结果
/// </summary>
internal class FillTaskResult
{
    public int PayloadVersion { get; set; } = 2;
    public string TaskId { get; set; } = string.Empty;
    public int SourceFileId { get; set; }
    public int SourceTableIndex { get; set; }
    public int? AcceptanceColumnIndex { get; set; }
    public int? RemarkColumnIndex { get; set; }
    public List<FillResult> FillResults { get; set; } = [];
    public string? FilledFilePath { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 是否为批量模式（多表格一次性填充）
    /// </summary>
    public bool IsBatchMode { get; set; }

    /// <summary>
    /// 批量模式下各表格的填充条目
    /// </summary>
    public List<TableFillEntry> TableEntries { get; set; } = [];

    /// <summary>
    /// 下载产物相对路径
    /// </summary>
    public string? DownloadArtifactRelativePath { get; set; }

    /// <summary>
    /// 下载产物文件名
    /// </summary>
    public string? DownloadArtifactFileName { get; set; }

    /// <summary>
    /// 下载产物内容类型
    /// </summary>
    public string? DownloadArtifactContentType { get; set; }
}

/// <summary>
/// 单个表格的填充条目（批量模式）
/// </summary>
internal class TableFillEntry
{
    public int TableIndex { get; set; }
    public int AcceptanceColumnIndex { get; set; }
    public int? RemarkColumnIndex { get; set; }
    public List<FillResult> FillResults { get; set; } = [];
}

/// <summary>
/// 单行填充结果
/// </summary>
internal class FillResult
{
    public int RowIndex { get; set; }
    public int SpecId { get; set; }
    public string Acceptance { get; set; } = string.Empty;
    public string? Remark { get; set; }
}

internal class GeneratedArtifactFile
{
    public int FileId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = [];
}

internal readonly record struct WriteBackSummary(int RequestedCells, int WrittenCells);

internal enum LlmStepOutcome
{
    Success = 0,
    Failed = 1,
    Timeout = 2
}

internal readonly record struct LlmStepExecutionResult(LlmStepOutcome Outcome, int RetriesUsed);
