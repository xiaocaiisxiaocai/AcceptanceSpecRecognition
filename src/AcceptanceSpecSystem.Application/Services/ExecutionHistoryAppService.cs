using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO.Compression;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;

namespace AcceptanceSpecSystem.Application.Services;

public interface IExecutionHistoryAppService
{
    Task<PagedData<ExecutionHistoryListItemDto>> GetListAsync(
        MatchingUserContext user,
        int page,
        int pageSize,
        string? keyword,
        string? taskType,
        CancellationToken cancellationToken = default);

    Task<ExecutionHistoryDetailDto?> GetDetailAsync(
        MatchingUserContext user,
        int id,
        CancellationToken cancellationToken = default);

    Task<ExecutionHistorySmartFillRowDto?> GetSmartFillRowAsync(
        MatchingUserContext user,
        int id,
        int fileIndex,
        int sheetIndex,
        int rowIndex,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 执行记录应用服务。
/// </summary>
public sealed class ExecutionHistoryAppService : IExecutionHistoryAppService
{
    private const int MaxPersistedDetailBytes = 512 * 1024;
    private const string CompressedSmartFillLegacyMessage = "执行记录过大，已自动压缩，仅保留汇总信息。";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<ExecutionHistoryAppService> _logger;

    public ExecutionHistoryAppService(
        IUnitOfWork unitOfWork,
        IFileStorageService fileStorage,
        ILogger<ExecutionHistoryAppService> logger)
    {
        _unitOfWork = unitOfWork;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task SaveAsync(
        MatchingUserContext user,
        ExecutionHistoryDraft draft,
        CancellationToken cancellationToken = default,
        bool saveImmediately = true)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var owner = ResolveOwner(user);
        var detail = BuildDetailDto(draft);
        var detailJson = JsonSerializer.Serialize(detail, JsonOptions);
        var detailBytes = Encoding.UTF8.GetByteCount(detailJson);
        string? fullArchiveRelativePath = null;
        if (detailBytes > MaxPersistedDetailBytes)
        {
            if (string.Equals(draft.TaskType, ExecutionHistoryTaskTypes.SmartFill, StringComparison.Ordinal))
            {
                fullArchiveRelativePath = await SaveFullSmartFillArchiveAsync(
                    draft.TaskId,
                    detailJson,
                    cancellationToken);
                // 智能填充：优先“精简”而非整段丢弃，剥离重负载但保留逐行分析信号
                // （命中来源/决策/置信度/AI 裁决结论/问题码）；仅当精简后仍超限才降级为汇总归档。
                ExecutionHistorySmartFillSlimmer.SlimInPlace(detail);
                MarkFullArchive(detail, fullArchiveRelativePath);
                var slimmedJson = JsonSerializer.Serialize(detail, JsonOptions);
                var slimmedBytes = Encoding.UTF8.GetByteCount(slimmedJson);

                if (slimmedBytes <= MaxPersistedDetailBytes)
                {
                    detailJson = slimmedJson;
                    _logger.LogInformation(
                        "智能填充执行记录过大，已精简归档（保留逐行分析信号）: taskId={TaskId}, originalBytes={OriginalBytes}, slimmedBytes={SlimmedBytes}",
                        draft.TaskId,
                        detailBytes,
                        slimmedBytes);
                }
                else
                {
                    ExecutionHistorySmartFillSlimmer.SlimToPlaybackOutlineInPlace(detail);
                    MarkFullArchive(detail, fullArchiveRelativePath);
                    var outlineJson = JsonSerializer.Serialize(detail, JsonOptions);
                    var outlineBytes = Encoding.UTF8.GetByteCount(outlineJson);

                    if (outlineBytes <= MaxPersistedDetailBytes)
                    {
                        detailJson = outlineJson;
                        _logger.LogInformation(
                            "智能填充执行记录过大，已二级精简归档（保留逐行回放骨架）: taskId={TaskId}, originalBytes={OriginalBytes}, slimmedBytes={SlimmedBytes}, outlineBytes={OutlineBytes}",
                            draft.TaskId,
                            detailBytes,
                            slimmedBytes,
                            outlineBytes);
                    }
                    else
                    {
                        detail = BuildCompressedSmartFillDetail(detail);
                        MarkFullArchive(detail, fullArchiveRelativePath);
                        detailJson = JsonSerializer.Serialize(detail, JsonOptions);

                        _logger.LogWarning(
                            "智能填充执行记录过大，二级精简后仍超限，已降级为汇总归档: taskId={TaskId}, sourceFileId={SourceFileId}, originalBytes={OriginalBytes}, slimmedBytes={SlimmedBytes}, outlineBytes={OutlineBytes}, compressedBytes={CompressedBytes}",
                            draft.TaskId,
                            draft.SourceFileId,
                            detailBytes,
                            slimmedBytes,
                            outlineBytes,
                            Encoding.UTF8.GetByteCount(detailJson));
                    }
                }
            }
            else
            {
                // 其它任务类型（如批量回复）：精简掉逐行明细，保留文件头与记录级计数，
                // 避免大批量执行记录撑爆持久化（DB packet 上限）与历史列表查询（逐条反序列化整段 DetailJson）。
                CompactGenericDetailInPlace(detail);
                detailJson = JsonSerializer.Serialize(detail, JsonOptions);

                _logger.LogWarning(
                    "{TaskType} 执行记录过大，已精简逐行明细归档: taskId={TaskId}, originalBytes={OriginalBytes}, compactedBytes={CompactedBytes}",
                    draft.TaskType,
                    draft.TaskId,
                    detailBytes,
                    Encoding.UTF8.GetByteCount(detailJson));
            }
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
            await _unitOfWork.ExecutionHistoryRecords.AddAsync(entity, cancellationToken);
        }

        entity.TaskType = draft.TaskType;
        entity.SourceFileId = draft.SourceFileId;
        entity.SourceFileName = draft.SourceFileName;
        entity.SourceFileType = draft.SourceFileType;
        entity.OwnerOrgUnitId = draft.OwnerOrgUnitId ?? entity.OwnerOrgUnitId;
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
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    public Task SaveAsync(
        BatchReplyUserContext user,
        ExecutionHistoryDraft draft,
        CancellationToken cancellationToken = default,
        bool saveImmediately = true) =>
        SaveAsync(
            new MatchingUserContext(user.UserId, user.CompanyId),
            draft,
            cancellationToken,
            saveImmediately);

    public async Task<PagedData<ExecutionHistoryListItemDto>> GetListAsync(
        MatchingUserContext user,
        int page,
        int pageSize,
        string? keyword,
        string? taskType,
        CancellationToken cancellationToken = default)
    {
        var owner = ResolveOwner(user);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, ExecutionHistoryRecordRepository.MaxPageSize);
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
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<ExecutionHistoryDetailDto?> GetDetailAsync(
        MatchingUserContext user,
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
            var detail = NormalizeDetail(entity, TryDeserializeDetail(entity));
            detail = await RestoreArchivedSmartFillPlaybackAsync(entity, detail, cancellationToken);
            HideArchivePath(detail);
            return detail;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "执行记录详情反序列化失败: {Id}", id);
            throw;
        }
    }

    public async Task<ExecutionHistorySmartFillRowDto?> GetSmartFillRowAsync(
        MatchingUserContext user,
        int id,
        int fileIndex,
        int sheetIndex,
        int rowIndex,
        CancellationToken cancellationToken = default)
    {
        var owner = ResolveOwner(user);
        var entity = await _unitOfWork.ExecutionHistoryRecords.GetOwnedByIdAsync(id, owner.CompanyId, owner.UserId);
        if (entity == null ||
            !string.Equals(entity.TaskType, ExecutionHistoryTaskTypes.SmartFill, StringComparison.Ordinal))
        {
            return null;
        }

        var lightDetail = NormalizeDetail(entity, TryDeserializeDetail(entity));
        var archivePath = lightDetail.SmartFillPlayback?.FullArchiveRelativePath;
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            if (lightDetail.SmartFillPlayback is { IsSlimmed: true } or { IsLegacy: true })
            {
                return null;
            }

            return FindSmartFillRow(lightDetail.SmartFillPlayback, fileIndex, sheetIndex, rowIndex);
        }

        var fullDetail = await ReadFullSmartFillArchiveAsync(archivePath, cancellationToken);
        return FindSmartFillRow(fullDetail?.SmartFillPlayback, fileIndex, sheetIndex, rowIndex);
    }

    private async Task<string> SaveFullSmartFillArchiveAsync(
        string taskId,
        string detailJson,
        CancellationToken cancellationToken)
    {
        var archiveBytes = CompressUtf8(detailJson);
        var safeTaskId = string.Concat(taskId.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-'));
        return await _fileStorage.SaveSmartFillPlaybackArchiveAsync(
            $"{safeTaskId}-smart-fill-playback.json.gz",
            archiveBytes,
            cancellationToken);
    }

    private async Task<ExecutionHistoryDetailDto?> ReadFullSmartFillArchiveAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var normalizedPath = relativePath.Replace('\\', '/');
            if (!normalizedPath.StartsWith("uploads/execution-history/smart-fill/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("智能填充回放归档路径非法");
            }

            await using var archiveStream = _fileStorage.OpenReadStream(relativePath);
            await using var gzip = new GZipStream(archiveStream, CompressionMode.Decompress);
            return await JsonSerializer.DeserializeAsync<ExecutionHistoryDetailDto>(
                gzip,
                JsonOptions,
                cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException or InvalidDataException)
        {
            _logger.LogWarning(ex, "智能填充完整回放归档读取失败: {RelativePath}", relativePath);
            return null;
        }
    }

    private async Task<ExecutionHistoryDetailDto> RestoreArchivedSmartFillPlaybackAsync(
        ExecutionHistoryRecord entity,
        ExecutionHistoryDetailDto detail,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(entity.TaskType, ExecutionHistoryTaskTypes.SmartFill, StringComparison.Ordinal))
        {
            return detail;
        }

        var playback = detail.SmartFillPlayback;
        if (playback == null || string.IsNullOrWhiteSpace(playback.FullArchiveRelativePath))
        {
            return detail;
        }

        var hasRows = playback.Files.Any(file =>
            file.Sheets.Any(sheet => sheet.Rows.Count > 0));
        if (!playback.IsLegacy && hasRows)
        {
            return detail;
        }

        var archivedDetail = await ReadFullSmartFillArchiveAsync(playback.FullArchiveRelativePath, cancellationToken);
        if (archivedDetail?.SmartFillPlayback?.Files.Count > 0 != true)
        {
            return detail;
        }

        ExecutionHistorySmartFillSlimmer.SlimToPlaybackOutlineInPlace(archivedDetail);
        archivedDetail.SmartFillPlayback.IsLegacy = false;
        MarkFullArchive(archivedDetail, playback.FullArchiveRelativePath);
        return NormalizeDetail(entity, archivedDetail);
    }

    private static byte[] CompressUtf8(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    private static void MarkFullArchive(ExecutionHistoryDetailDto detail, string? archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || detail.SmartFillPlayback == null)
        {
            return;
        }

        detail.SmartFillPlayback.HasFullArchive = true;
        detail.SmartFillPlayback.FullArchiveRelativePath = archivePath;
        if (detail.SmartFillSummary != null)
        {
            detail.SmartFillSummary.HasPlaybackArchive = true;
        }
    }

    private static void HideArchivePath(ExecutionHistoryDetailDto detail)
    {
        if (detail.SmartFillPlayback != null)
        {
            detail.SmartFillPlayback.FullArchiveRelativePath = null;
        }
    }

    private static ExecutionHistorySmartFillRowDto? FindSmartFillRow(
        ExecutionHistorySmartFillPlaybackDto? playback,
        int fileIndex,
        int sheetIndex,
        int rowIndex)
    {
        var file = playback?.Files.ElementAtOrDefault(fileIndex);
        var sheet = file?.Sheets.ElementAtOrDefault(sheetIndex);
        return sheet?.Rows.FirstOrDefault(row => row.RowIndex == rowIndex);
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

    /// <summary>
    /// 通用精简：清空 Files / BatchReplyDetail 的逐行明细（保留文件头与表头），
    /// 用于智能填充以外的任务类型（如批量回复）超限时压缩；记录级计数另存于实体列，不受影响。
    /// </summary>
    private static void CompactGenericDetailInPlace(ExecutionHistoryDetailDto detail)
    {
        EmptyRows(detail.Files);
        if (detail.BatchReplyDetail != null)
        {
            EmptyRows(detail.BatchReplyDetail.Files);
        }
    }

    private static void EmptyRows(List<ExecutionHistoryFileDto>? files)
    {
        foreach (var file in files ?? [])
        {
            foreach (var sheet in file.Sheets ?? [])
            {
                sheet.Rows = [];
            }
        }
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
                detail.SmartFillPlayback is { IsLegacy: false } or { HasFullArchive: true };
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

    private static (int UserId, int CompanyId) ResolveOwner(MatchingUserContext user)
    {
        return (user.UserId, user.CompanyId);
    }
}
