using System.Text.Json;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using static AcceptanceSpecSystem.Application.Services.MatchingResultHelpers;

namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 匹配填充任务快照服务。
/// </summary>
public sealed class MatchingTaskSnapshotService
{
    private static readonly JsonSerializerOptions FillTaskJsonOptions = new(JsonSerializerDefaults.Web);
    private const int FillTaskRetentionHours = 24;
    private const int CurrentFillTaskPayloadVersion = 4;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileStorageService _fileStorage;
    private readonly IDocumentFileAccessService _documentFileAccessService;
    private readonly ILogger<MatchingTaskSnapshotService> _logger;
    private readonly HashSet<string> _deferredExpiredArtifactPaths = new(StringComparer.OrdinalIgnoreCase);

    public MatchingTaskSnapshotService(
        IUnitOfWork unitOfWork,
        IFileStorageService fileStorage,
        IDocumentFileAccessService documentFileAccessService,
        ILogger<MatchingTaskSnapshotService> logger)
    {
        _unitOfWork = unitOfWork;
        _fileStorage = fileStorage;
        _documentFileAccessService = documentFileAccessService;
        _logger = logger;
    }

    internal async Task PersistSourceRollbackArtifactAsync(
        FillTaskResult taskResult,
        string sourceFileName,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(taskResult.SourceRollbackArtifactRelativePath))
        {
            return;
        }

        var extension = Path.GetExtension(sourceFileName);
        var fileName = $"rollback-{taskResult.TaskId}{extension}";
        taskResult.SourceRollbackArtifactRelativePath = await _fileStorage.SaveFilledWordAsync(
            fileName,
            content,
            cancellationToken);
    }

    internal async Task DeleteSourceRollbackArtifactAsync(
        FillTaskResult taskResult,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(taskResult.SourceRollbackArtifactRelativePath))
        {
            try
            {
                await _fileStorage.DeleteIfExistsAsync(
                    taskResult.SourceRollbackArtifactRelativePath,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "清理 Excel 回滚产物失败，后续保留策略将重试: {ArtifactPath}",
                    taskResult.SourceRollbackArtifactRelativePath);
            }
        }
    }

    internal async Task<bool> IsCompletedFileMutationAsync(
        MatchingUserContext user,
        string taskId,
        string requestFingerprint)
    {
        var task = await LoadAsync(user, taskId);
        return task != null &&
               !task.FileMutationPending &&
               string.Equals(task.RequestFingerprint, requestFingerprint, StringComparison.Ordinal);
    }

    internal async Task RecoverPendingFileMutationAsync(
        MatchingUserContext user,
        FillTaskResult taskResult,
        CancellationToken cancellationToken = default)
    {
        if (!taskResult.FileMutationPending)
        {
            return;
        }

        var entity = await _unitOfWork.MatchingFillTasks.GetByTaskIdAsync(taskResult.TaskId);
        if (entity == null)
        {
            return;
        }
        await EnsureTaskOwnershipAsync(user, entity);

        if (string.IsNullOrWhiteSpace(taskResult.SourceRollbackArtifactRelativePath))
        {
            throw new InvalidOperationException($"待恢复任务缺少回滚产物: {taskResult.TaskId}");
        }

        byte[] originalContent;
        await using (var rollbackStream = _fileStorage.OpenReadStream(taskResult.SourceRollbackArtifactRelativePath))
        using (var rollbackContent = new MemoryStream())
        {
            await rollbackStream.CopyToAsync(rollbackContent, cancellationToken);
            originalContent = rollbackContent.ToArray();
        }

        var wordFile = await _unitOfWork.WordFiles.GetByIdAsync(taskResult.SourceFileId);
        if (wordFile != null)
        {
            if (!string.IsNullOrWhiteSpace(taskResult.SourceOriginalFilePath))
            {
                wordFile.FilePath = taskResult.SourceOriginalFilePath;
                await _documentFileAccessService.PersistUpdatedFileContentAsync(
                    wordFile,
                    originalContent,
                    cancellationToken);
            }
            else
            {
                wordFile.FilePath = null;
                wordFile.FileContent = originalContent;
                wordFile.FileHash = taskResult.SourceOriginalFileHash ?? wordFile.FileHash;
            }
            _unitOfWork.WordFiles.Update(wordFile);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        _unitOfWork.MatchingFillTasks.Remove(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await DeleteSourceRollbackArtifactAsync(taskResult, cancellationToken);
    }

    public async Task RecoverAllPendingFileMutationsAsync(CancellationToken cancellationToken = default)
    {
        var candidates = await _unitOfWork.MatchingFillTasks
            .Query()
            .Where(task => task.PayloadJson.Contains("\"fileMutationPending\":true"))
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var taskResult = DeserializeFillTaskResult(candidate.PayloadJson);
                if (taskResult?.FileMutationPending != true ||
                    !candidate.CreatedByUserId.HasValue ||
                    !candidate.CompanyId.HasValue)
                {
                    continue;
                }

                await RecoverPendingFileMutationAsync(
                    new MatchingUserContext(candidate.CreatedByUserId.Value, candidate.CompanyId.Value),
                    taskResult,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "恢复未完成的 Excel 文件写回失败: {TaskId}", candidate.TaskId);
            }
        }
    }

    internal async Task PersistDownloadArtifactAsync(
        string taskId,
        FillTaskResult taskResult,
        string downloadFileName,
        string contentType,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.MatchingFillTasks.GetByTaskIdAsync(taskId);
        if (entity == null)
        {
            throw new InvalidOperationException($"未找到匹配任务快照: {taskId}");
        }

        if (!string.IsNullOrWhiteSpace(taskResult.DownloadArtifactRelativePath))
        {
            var existingArtifactPath = _fileStorage.GetAbsolutePath(taskResult.DownloadArtifactRelativePath);
            if (File.Exists(existingArtifactPath))
            {
                return;
            }
        }

        var relativePath = await _fileStorage.SaveFilledWordAsync(downloadFileName, content, cancellationToken);
        taskResult.DownloadArtifactRelativePath = relativePath;
        taskResult.DownloadArtifactFileName = downloadFileName;
        taskResult.DownloadArtifactContentType = contentType;

        entity.PayloadJson = JsonSerializer.Serialize(taskResult, FillTaskJsonOptions);
        _unitOfWork.MatchingFillTasks.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    internal async Task SaveAsync(
        MatchingUserContext user,
        FillTaskResult taskResult,
        bool saveImmediately = true,
        CancellationToken cancellationToken = default)
    {
        var owner = ResolveTaskOwner(user);
        taskResult.PayloadVersion = CurrentFillTaskPayloadVersion;
        var payload = JsonSerializer.Serialize(taskResult, FillTaskJsonOptions);
        var existed = await _unitOfWork.MatchingFillTasks.GetByTaskIdAsync(taskResult.TaskId);
        if (existed == null)
        {
            await _unitOfWork.MatchingFillTasks.AddAsync(new MatchingFillTask
            {
                TaskId = taskResult.TaskId,
                SourceFileId = taskResult.SourceFileId,
                CreatedByUserId = owner.UserId,
                CompanyId = owner.CompanyId,
                PayloadJson = payload,
                CreatedAt = taskResult.CreatedAt
            });
        }
        else
        {
            var existingTask = DeserializeFillTaskResult(existed.PayloadJson);
            if (existingTask == null ||
                existed.CreatedByUserId != owner.UserId ||
                existed.CompanyId != owner.CompanyId ||
                existingTask.SourceFileId != taskResult.SourceFileId ||
                !string.Equals(
                    existingTask.RequestFingerprint,
                    taskResult.RequestFingerprint,
                    StringComparison.Ordinal))
            {
                // TaskId 是数据库唯一键。并发请求若在入口检查后才看到其它请求已经
                // 落库，不能把赢家的快照改写成当前文件/映射，交由上层回读并返回 409。
                throw new InvalidOperationException("匹配任务幂等键已被不同请求占用");
            }

            existed.SourceFileId = taskResult.SourceFileId;
            existed.CreatedByUserId = owner.UserId;
            existed.CompanyId = owner.CompanyId;
            existed.PayloadJson = payload;
            existed.CreatedAt = taskResult.CreatedAt;
            _unitOfWork.MatchingFillTasks.Update(existed);
        }

        var expireTime = DateTime.UtcNow.AddHours(-FillTaskRetentionHours);
        await CleanupExpiredArtifactsAsync(expireTime, cancellationToken);
        if (saveImmediately)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await CompleteDeferredExpiredArtifactCleanupAsync(cancellationToken);
        }
    }

    internal async Task<FillTaskResult?> LoadAsync(
        MatchingUserContext user,
        string taskId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _unitOfWork.MatchingFillTasks.GetByTaskIdAsync(taskId);
        if (entity == null || string.IsNullOrWhiteSpace(entity.PayloadJson))
        {
            return null;
        }

        await EnsureTaskOwnershipAsync(user, entity, cancellationToken);

        try
        {
            return DeserializeFillTaskResult(entity.PayloadJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "任务快照反序列化失败: {TaskId}", taskId);
            return null;
        }
    }

    private async Task CleanupExpiredArtifactsAsync(
        DateTime expireTime,
        CancellationToken cancellationToken)
    {
        var expiredTasks = await _unitOfWork.MatchingFillTasks
            .Query(asNoTracking: false)
            .Where(task => task.CreatedAt < expireTime)
            .ToListAsync(cancellationToken);

        if (expiredTasks.Count == 0)
        {
            return;
        }

        foreach (var expiredTask in expiredTasks)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(expiredTask.PayloadJson))
                {
                    continue;
                }

                var snapshot = DeserializeFillTaskResult(expiredTask.PayloadJson);
                if (!string.IsNullOrWhiteSpace(snapshot?.DownloadArtifactRelativePath))
                {
                    _deferredExpiredArtifactPaths.Add(snapshot.DownloadArtifactRelativePath);
                }
                if (!string.IsNullOrWhiteSpace(snapshot?.SourceRollbackArtifactRelativePath))
                {
                    _deferredExpiredArtifactPaths.Add(snapshot.SourceRollbackArtifactRelativePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理过期填充任务产物失败: {TaskId}", expiredTask.TaskId);
            }
        }

        _unitOfWork.MatchingFillTasks.RemoveRange(expiredTasks);
    }

    internal async Task CompleteDeferredExpiredArtifactCleanupAsync(
        CancellationToken cancellationToken = default)
    {
        if (_deferredExpiredArtifactPaths.Count == 0)
        {
            return;
        }

        var paths = _deferredExpiredArtifactPaths.ToArray();
        _deferredExpiredArtifactPaths.Clear();
        foreach (var path in paths)
        {
            try
            {
                await _fileStorage.DeleteIfExistsAsync(path, cancellationToken);
            }
            catch (Exception ex)
            {
                // 数据库已提交删除；物理清理失败时保留为孤儿文件，后续孤儿扫描可重试。
                _logger.LogWarning(ex, "清理已过期填充产物失败: {ArtifactPath}", path);
            }
        }
    }

    internal void DiscardDeferredExpiredArtifactCleanup()
    {
        _deferredExpiredArtifactPaths.Clear();
    }

    private static FillTaskResult? DeserializeFillTaskResult(string payload)
    {
        var result = JsonSerializer.Deserialize<FillTaskResult>(payload, FillTaskJsonOptions);
        if (result == null)
        {
            return null;
        }

        if (result.PayloadVersion <= 0)
        {
            result.PayloadVersion = 1;
        }

        return result;
    }

    private static (int UserId, int CompanyId) ResolveTaskOwner(MatchingUserContext user)
    {
        return (user.UserId, user.CompanyId);
    }

    private async Task EnsureTaskOwnershipAsync(
        MatchingUserContext user,
        MatchingFillTask entity,
        CancellationToken cancellationToken = default)
    {
        if (!entity.CreatedByUserId.HasValue || !entity.CompanyId.HasValue)
        {
            await TryRecoverLegacyTaskOwnershipAsync(entity, cancellationToken);
        }

        if (!entity.CreatedByUserId.HasValue || !entity.CompanyId.HasValue)
        {
            throw Failure(400, "历史任务缺少归属信息，请重新执行填充后再下载");
        }

        var owner = ResolveTaskOwner(user);
        if (entity.CreatedByUserId != owner.UserId || entity.CompanyId != owner.CompanyId)
        {
            throw NotFoundFailure("任务不存在或已过期");
        }
    }

    private async Task TryRecoverLegacyTaskOwnershipAsync(
        MatchingFillTask entity,
        CancellationToken cancellationToken)
    {
        if (entity.SourceFileId <= 0)
        {
            return;
        }

        var sourceFile = await _unitOfWork.WordFiles.GetByIdAsync(entity.SourceFileId);
        if (sourceFile?.CreatedByUserId == null || sourceFile.CompanyId == null)
        {
            return;
        }

        entity.CreatedByUserId = sourceFile.CreatedByUserId;
        entity.CompanyId = sourceFile.CompanyId;
        _unitOfWork.MatchingFillTasks.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
