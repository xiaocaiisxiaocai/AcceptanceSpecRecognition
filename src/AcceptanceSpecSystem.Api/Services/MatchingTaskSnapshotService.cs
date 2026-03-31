using System.Security.Claims;
using System.Text.Json;
using AcceptanceSpecSystem.Api.Authorization;
using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 匹配填充任务快照服务。
/// </summary>
public sealed class MatchingTaskSnapshotService
{
    private static readonly JsonSerializerOptions FillTaskJsonOptions = new(JsonSerializerDefaults.Web);
    private const int FillTaskRetentionHours = 24;
    private const int CurrentFillTaskPayloadVersion = 2;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<MatchingTaskSnapshotService> _logger;

    public MatchingTaskSnapshotService(
        IUnitOfWork unitOfWork,
        IFileStorageService fileStorage,
        ILogger<MatchingTaskSnapshotService> logger)
    {
        _unitOfWork = unitOfWork;
        _fileStorage = fileStorage;
        _logger = logger;
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
        await _unitOfWork.SaveChangesAsync();
    }

    internal async Task SaveAsync(ClaimsPrincipal user, FillTaskResult taskResult)
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
            existed.SourceFileId = taskResult.SourceFileId;
            existed.CreatedByUserId = owner.UserId;
            existed.CompanyId = owner.CompanyId;
            existed.PayloadJson = payload;
            existed.CreatedAt = taskResult.CreatedAt;
            _unitOfWork.MatchingFillTasks.Update(existed);
        }

        var expireTime = DateTime.UtcNow.AddHours(-FillTaskRetentionHours);
        await CleanupExpiredArtifactsAsync(expireTime);
        await _unitOfWork.SaveChangesAsync();
    }

    internal async Task<FillTaskResult?> LoadAsync(ClaimsPrincipal user, string taskId)
    {
        var entity = await _unitOfWork.MatchingFillTasks.GetByTaskIdAsync(taskId);
        if (entity == null || string.IsNullOrWhiteSpace(entity.PayloadJson))
        {
            return null;
        }

        EnsureTaskOwnership(user, entity);

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

    private async Task CleanupExpiredArtifactsAsync(DateTime expireTime)
    {
        var expiredTasks = await _unitOfWork.MatchingFillTasks
            .Query(asNoTracking: false)
            .Where(task => task.CreatedAt < expireTime)
            .ToListAsync();

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
                    await _fileStorage.DeleteIfExistsAsync(snapshot.DownloadArtifactRelativePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理过期填充任务产物失败: {TaskId}", expiredTask.TaskId);
            }
        }

        _unitOfWork.MatchingFillTasks.RemoveRange(expiredTasks);
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

    private static (int UserId, int CompanyId) ResolveTaskOwner(ClaimsPrincipal user)
    {
        var userId = AuthClaimHelper.GetUserId(user);
        var companyId = AuthClaimHelper.GetCompanyId(user);
        if (!userId.HasValue || !companyId.HasValue)
        {
            throw Failure(401, "会话缺少用户上下文");
        }

        return (userId.Value, companyId.Value);
    }

    private static void EnsureTaskOwnership(ClaimsPrincipal user, MatchingFillTask entity)
    {
        if (!entity.CreatedByUserId.HasValue || !entity.CompanyId.HasValue)
        {
            throw NotFoundFailure("任务不存在或已过期");
        }

        var owner = ResolveTaskOwner(user);
        if (entity.CreatedByUserId != owner.UserId || entity.CompanyId != owner.CompanyId)
        {
            throw NotFoundFailure("任务不存在或已过期");
        }
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
