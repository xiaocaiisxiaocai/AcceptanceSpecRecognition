using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Models;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Api.Services;

public interface IExecutionHistoryAppService
{
    Task<PagedData<ExecutionHistoryListItemDto>> GetListAsync(
        ClaimsPrincipal user,
        int page,
        int pageSize,
        string? keyword,
        string? taskType,
        CancellationToken cancellationToken = default);

    Task<ExecutionHistoryDetailDto?> GetDetailAsync(
        ClaimsPrincipal user,
        int id,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 执行记录应用服务。
/// </summary>
public sealed class ExecutionHistoryAppService : IExecutionHistoryAppService
{
    private const int MaxPersistedDetailBytes = 512 * 1024;
    private const string CompressedSmartFillLegacyMessage = "执行记录过大，已自动压缩，仅保留汇总信息。";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ExecutionHistoryAppService> _logger;

    public ExecutionHistoryAppService(IUnitOfWork unitOfWork, ILogger<ExecutionHistoryAppService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    internal async Task SaveAsync(
        ClaimsPrincipal user,
        ExecutionHistoryDraft draft,
        CancellationToken cancellationToken = default,
        bool saveImmediately = true)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var owner = ResolveOwner(user);
        var detail = BuildDetailDto(draft);
        var detailJson = JsonSerializer.Serialize(detail, JsonOptions);
        var detailBytes = Encoding.UTF8.GetByteCount(detailJson);
        if (ShouldCompressSmartFillDetail(draft, detailBytes))
        {
            detail = BuildCompressedSmartFillDetail(detail);
            detailJson = JsonSerializer.Serialize(detail, JsonOptions);

            _logger.LogWarning(
                "智能填充执行记录过大，已自动压缩归档: taskId={TaskId}, sourceFileId={SourceFileId}, originalBytes={OriginalBytes}, compressedBytes={CompressedBytes}",
                draft.TaskId,
                draft.SourceFileId,
                detailBytes,
                Encoding.UTF8.GetByteCount(detailJson));
        }

        var entity = await _unitOfWork.ExecutionHistoryRecords.GetOwnedByTaskIdAsync(
            draft.TaskId,
            owner.CompanyId,
            owner.UserId);

        if (entity == null)
        {
            entity = new ExecutionHistoryRecord
            {
                TaskId = draft.TaskId,
                CreatedByUserId = owner.UserId,
                CompanyId = owner.CompanyId
            };
            await _unitOfWork.ExecutionHistoryRecords.AddAsync(entity);
        }

        entity.TaskType = draft.TaskType;
        entity.SourceFileId = draft.SourceFileId;
        entity.SourceFileName = draft.SourceFileName;
        entity.SourceFileType = draft.SourceFileType;
        entity.FileCount = detail.FileCount;
        entity.TotalRowCount = detail.TotalRowCount;
        entity.MatchedRowCount = detail.MatchedRowCount;
        entity.AdoptedRowCount = detail.AdoptedRowCount;
        entity.UnmatchedRowCount = detail.UnmatchedRowCount;
        entity.SkippedRowCount = detail.SkippedRowCount;
        entity.NotAdoptedRowCount = detail.NotAdoptedRowCount;
        entity.ManualSelectedRowCount = detail.ManualSelectedRowCount;
        entity.DetailJson = detailJson;
        entity.CreatedAt = draft.CreatedAt;

        if (saveImmediately)
        {
            await _unitOfWork.SaveChangesAsync();
        }
    }

    public async Task<PagedData<ExecutionHistoryListItemDto>> GetListAsync(
        ClaimsPrincipal user,
        int page,
        int pageSize,
        string? keyword,
        string? taskType,
        CancellationToken cancellationToken = default)
    {
        var owner = ResolveOwner(user);
        var (items, total) = await _unitOfWork.ExecutionHistoryRecords.GetPagedOwnedAsync(
            owner.CompanyId,
            owner.UserId,
            page,
            pageSize,
            keyword,
            taskType);

        return new PagedData<ExecutionHistoryListItemDto>
        {
            Items = items.Select(entity => ToListDto(entity, TryDeserializeDetail(entity))).ToList(),
            Total = total,
            Page = page <= 0 ? 1 : page,
            PageSize = pageSize <= 0 ? 20 : pageSize
        };
    }

    public async Task<ExecutionHistoryDetailDto?> GetDetailAsync(
        ClaimsPrincipal user,
        int id,
        CancellationToken cancellationToken = default)
    {
        var owner = ResolveOwner(user);
        var entity = await _unitOfWork.ExecutionHistoryRecords.GetOwnedByIdAsync(id, owner.CompanyId, owner.UserId);
        if (entity == null)
        {
            return null;
        }

        try
        {
            return NormalizeDetail(entity, TryDeserializeDetail(entity));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "执行记录详情反序列化失败: {Id}", id);
            throw;
        }
    }

    private static ExecutionHistoryDetailDto BuildDetailDto(ExecutionHistoryDraft draft)
    {
        var rows = draft.Files.SelectMany(file => file.Sheets).SelectMany(sheet => sheet.Rows).ToList();

        return new ExecutionHistoryDetailDto
        {
            TaskId = draft.TaskId,
            TaskType = draft.TaskType,
            SourceFileId = draft.SourceFileId,
            SourceFileName = draft.SourceFileName,
            SourceFileType = draft.SourceFileType,
            FileCount = draft.Files.Count,
            TotalRowCount = rows.Count,
            MatchedRowCount = rows.Count(row => row.Status != ExecutionHistoryStatuses.Unmatched),
            AdoptedRowCount = rows.Count(row => row.Status == ExecutionHistoryStatuses.Adopted),
            UnmatchedRowCount = rows.Count(row => row.Status == ExecutionHistoryStatuses.Unmatched),
            SkippedRowCount = rows.Count(row => row.Status == ExecutionHistoryStatuses.Skipped),
            NotAdoptedRowCount = rows.Count(row => row.Status == ExecutionHistoryStatuses.NotAdopted),
            ManualSelectedRowCount = rows.Count(row => row.IsManualSelected),
            SmartFillSummary = draft.SmartFillSummary,
            CreatedAt = draft.CreatedAt,
            Files = draft.Files,
            SmartFillPlayback = draft.SmartFillPlayback,
            BatchReplyDetail = draft.BatchReplyDetail
        };
    }

    private static bool ShouldCompressSmartFillDetail(ExecutionHistoryDraft draft, int detailBytes)
    {
        return string.Equals(draft.TaskType, ExecutionHistoryTaskTypes.SmartFill, StringComparison.Ordinal) &&
               detailBytes > MaxPersistedDetailBytes;
    }

    private static ExecutionHistoryDetailDto BuildCompressedSmartFillDetail(ExecutionHistoryDetailDto detail)
    {
        return new ExecutionHistoryDetailDto
        {
            TaskId = detail.TaskId,
            TaskType = detail.TaskType,
            SourceFileId = detail.SourceFileId,
            SourceFileName = detail.SourceFileName,
            SourceFileType = detail.SourceFileType,
            FileCount = detail.FileCount,
            TotalRowCount = detail.TotalRowCount,
            MatchedRowCount = detail.MatchedRowCount,
            AdoptedRowCount = detail.AdoptedRowCount,
            UnmatchedRowCount = detail.UnmatchedRowCount,
            SkippedRowCount = detail.SkippedRowCount,
            NotAdoptedRowCount = detail.NotAdoptedRowCount,
            ManualSelectedRowCount = detail.ManualSelectedRowCount,
            SmartFillSummary = CloneSmartFillSummary(detail.SmartFillSummary, hasPlaybackArchive: false),
            CreatedAt = detail.CreatedAt,
            Files = [],
            SmartFillPlayback = new ExecutionHistorySmartFillPlaybackDto
            {
                PayloadVersion = detail.SmartFillPlayback?.PayloadVersion ?? ExecutionHistoryDraft.CurrentSmartFillPlaybackVersion,
                IsLegacy = true,
                LegacyMessage = CompressedSmartFillLegacyMessage,
                Files = []
            }
        };
    }

    private static ExecutionHistorySmartFillSummaryDto? CloneSmartFillSummary(
        ExecutionHistorySmartFillSummaryDto? summary,
        bool hasPlaybackArchive)
    {
        if (summary == null)
        {
            return null;
        }

        return new ExecutionHistorySmartFillSummaryDto
        {
            ExactMatchedRowCount = summary.ExactMatchedRowCount,
            AiMatchedRowCount = summary.AiMatchedRowCount,
            ManualConfirmedRowCount = summary.ManualConfirmedRowCount,
            ManualEditedRowCount = summary.ManualEditedRowCount,
            NotUsedRowCount = summary.NotUsedRowCount,
            HasPlaybackArchive = hasPlaybackArchive
        };
    }

    private ExecutionHistoryDetailDto? TryDeserializeDetail(ExecutionHistoryRecord entity)
    {
        if (string.IsNullOrWhiteSpace(entity.DetailJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ExecutionHistoryDetailDto>(entity.DetailJson, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "执行记录摘要反序列化失败: {Id}", entity.Id);
            return null;
        }
    }

    private static ExecutionHistoryDetailDto NormalizeDetail(
        ExecutionHistoryRecord entity,
        ExecutionHistoryDetailDto? detail)
    {
        detail ??= new ExecutionHistoryDetailDto();
        detail.Id = entity.Id;
        detail.TaskId = entity.TaskId;
        detail.TaskType = entity.TaskType;
        detail.SourceFileId = entity.SourceFileId;
        detail.SourceFileName = entity.SourceFileName;
        detail.SourceFileType = entity.SourceFileType;
        detail.FileCount = entity.FileCount;
        detail.TotalRowCount = entity.TotalRowCount;
        detail.MatchedRowCount = entity.MatchedRowCount;
        detail.AdoptedRowCount = entity.AdoptedRowCount;
        detail.UnmatchedRowCount = entity.UnmatchedRowCount;
        detail.SkippedRowCount = entity.SkippedRowCount;
        detail.NotAdoptedRowCount = entity.NotAdoptedRowCount;
        detail.ManualSelectedRowCount = entity.ManualSelectedRowCount;
        detail.CreatedAt = entity.CreatedAt;
        detail.Files ??= [];
        detail.SmartFillSummary = NormalizeSmartFillSummary(entity, detail);
        detail.SmartFillPlayback = NormalizeSmartFillPlayback(entity, detail);
        detail.BatchReplyDetail = NormalizeBatchReplyDetail(entity, detail);
        return detail;
    }

    private static ExecutionHistoryListItemDto ToListDto(
        ExecutionHistoryRecord entity,
        ExecutionHistoryDetailDto? detail)
    {
        var normalized = NormalizeDetail(entity, detail);

        return new ExecutionHistoryListItemDto
        {
            Id = entity.Id,
            TaskId = entity.TaskId,
            TaskType = entity.TaskType,
            SourceFileId = entity.SourceFileId,
            SourceFileName = entity.SourceFileName,
            SourceFileType = entity.SourceFileType,
            FileCount = entity.FileCount,
            TotalRowCount = entity.TotalRowCount,
            MatchedRowCount = entity.MatchedRowCount,
            AdoptedRowCount = entity.AdoptedRowCount,
            UnmatchedRowCount = entity.UnmatchedRowCount,
            SkippedRowCount = entity.SkippedRowCount,
            NotAdoptedRowCount = entity.NotAdoptedRowCount,
            ManualSelectedRowCount = entity.ManualSelectedRowCount,
            SmartFillSummary = normalized.SmartFillSummary,
            CreatedAt = entity.CreatedAt
        };
    }

    private static ExecutionHistorySmartFillSummaryDto? NormalizeSmartFillSummary(
        ExecutionHistoryRecord entity,
        ExecutionHistoryDetailDto detail)
    {
        if (!string.Equals(entity.TaskType, ExecutionHistoryTaskTypes.SmartFill, StringComparison.Ordinal))
        {
            return detail.SmartFillSummary;
        }

        if (detail.SmartFillSummary != null)
        {
            detail.SmartFillSummary.HasPlaybackArchive =
                detail.SmartFillPlayback is { IsLegacy: false };
            return detail.SmartFillSummary;
        }

        return new ExecutionHistorySmartFillSummaryDto
        {
            ExactMatchedRowCount = null,
            AiMatchedRowCount = null,
            ManualConfirmedRowCount = null,
            ManualEditedRowCount = null,
            NotUsedRowCount = entity.NotAdoptedRowCount + entity.UnmatchedRowCount,
            HasPlaybackArchive = false
        };
    }

    private static ExecutionHistorySmartFillPlaybackDto? NormalizeSmartFillPlayback(
        ExecutionHistoryRecord entity,
        ExecutionHistoryDetailDto detail)
    {
        if (!string.Equals(entity.TaskType, ExecutionHistoryTaskTypes.SmartFill, StringComparison.Ordinal))
        {
            return null;
        }

        if (detail.SmartFillPlayback != null)
        {
            detail.SmartFillPlayback.Files ??= [];
            return detail.SmartFillPlayback;
        }

        return new ExecutionHistorySmartFillPlaybackDto
        {
            PayloadVersion = 0,
            IsLegacy = true,
            LegacyMessage = "历史记录，缺少预览归档，当前仅能展示简化结果。"
        };
    }

    private static ExecutionHistoryBatchReplyDetailDto? NormalizeBatchReplyDetail(
        ExecutionHistoryRecord entity,
        ExecutionHistoryDetailDto detail)
    {
        if (!string.Equals(entity.TaskType, ExecutionHistoryTaskTypes.BatchReply, StringComparison.Ordinal))
        {
            return detail.BatchReplyDetail;
        }

        if (detail.BatchReplyDetail != null)
        {
            detail.BatchReplyDetail.Files ??= detail.Files;
            return detail.BatchReplyDetail;
        }

        return new ExecutionHistoryBatchReplyDetailDto
        {
            Files = detail.Files
        };
    }

    private static (int UserId, int CompanyId) ResolveOwner(ClaimsPrincipal user)
    {
        var userId = AuthClaimHelper.GetUserId(user);
        var companyId = AuthClaimHelper.GetCompanyId(user);
        if (!userId.HasValue || !companyId.HasValue)
        {
            throw new InvalidOperationException("会话缺少用户上下文");
        }

        return (userId.Value, companyId.Value);
    }
}
