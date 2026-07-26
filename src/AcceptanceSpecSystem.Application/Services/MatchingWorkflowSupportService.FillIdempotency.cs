using System.Collections.Concurrent;
using static AcceptanceSpecSystem.Application.Services.MatchingResultHelpers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class MatchingWorkflowSupportService
{
    private static readonly ConcurrentDictionary<
        string,
        Lazy<Task<MatchingOperationResult<ExecuteFillResponse>>>> InFlightFillExecutions = new(StringComparer.Ordinal);

    internal async Task<MatchingOperationResult<ExecuteFillResponse>> BatchExecuteFillCoreAsync(
        MatchingUserContext user,
        BatchExecuteFillRequest request,
        CancellationToken cancellationToken = default)
    {
        var executionRequestId = request.ExecutionRequestId?.Trim();
        if (string.IsNullOrEmpty(executionRequestId))
        {
            return await ExecuteFillWithFileLockAsync(user, request, cancellationToken);
        }

        var requestFingerprint = BuildFillExecutionRequestFingerprint(request);
        // 文件与完整请求语义都必须进入进程内合并键。相同 requestId 的不同文件或
        // 不同映射不能共享另一个请求的成功结果，必须各自进入持久化冲突校验。
        var inFlightKey = $"{user.CompanyId}:{user.UserId}:{executionRequestId}:{request.FileId}:{requestFingerprint}";
        var execution = InFlightFillExecutions.GetOrAdd(
            inFlightKey,
            _ => new Lazy<Task<MatchingOperationResult<ExecuteFillResponse>>>(
                () => ExecuteFillWithFileLockAsync(user, request, cancellationToken),
                // 相同幂等请求先合并，再由文件锁与其它逻辑请求串行写回同一工作簿。
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            // 同一进程内完全相同的幂等键共享同一任务，第二个请求不会进入写回、
            // 快照和历史持久化路径，因此也不会暴露数据库唯一键冲突为 500。
            return await execution.Value;
        }
        catch (MatchingApiException originalException) when (originalException.Code >= 500)
        {
            // 多实例部署无法共享进程内任务。若另一实例先赢得 TaskId 唯一键，当前
            // 实例可能在持久化阶段失败；短暂回读既有快照，把唯一冲突收敛为幂等成功。
            for (var attempt = 0; attempt < 3; attempt++)
            {
                FillTaskResult? existingTask;
                try
                {
                    // 失败请求所在 DbContext 可能仍跟踪已经回滚的任务实体；必须使用
                    // 全新作用域读取数据库已提交状态，不能把内存幽灵快照当成幂等成功。
                    using var readScope = _scopeFactory.CreateScope();
                    var snapshotService = readScope.ServiceProvider.GetRequiredService<MatchingTaskSnapshotService>();
                    existingTask = await LoadIdempotentTaskAsync(snapshotService, user, executionRequestId);
                }
                catch (Exception readException)
                {
                    _logger.LogWarning(
                        readException,
                        "填充幂等冲突后回读任务失败: requestId={ExecutionRequestId}",
                        executionRequestId);
                    break;
                }

                if (existingTask != null)
                {
                    EnsureIdempotentFillRequestMatches(existingTask, request.FileId, requestFingerprint);
                    return Result(
                        new ExecuteFillResponse
                        {
                            TaskId = existingTask.TaskId,
                            FilledCount = existingTask.FilledCount,
                            SkippedCount = existingTask.SkippedCount,
                            DownloadUrl = string.Empty
                        },
                        "该填充请求已由其他执行实例完成，已返回原任务结果");
                }

                if (attempt < 2)
                {
                    await Task.Delay(100, CancellationToken.None);
                }
            }

            throw;
        }
        finally
        {
            if (InFlightFillExecutions.TryGetValue(inFlightKey, out var current) &&
                ReferenceEquals(current, execution))
            {
                InFlightFillExecutions.TryRemove(inFlightKey, out _);
            }
        }
    }

    private async Task<MatchingOperationResult<ExecuteFillResponse>> ExecuteFillWithFileLockAsync(
        MatchingUserContext user,
        BatchExecuteFillRequest request,
        CancellationToken cancellationToken)
    {
        var lockKey = $"{user.CompanyId}:{request.FileId}";
        await using var operationLock = await _unitOfWork.AcquireOperationLockAsync(
            $"matching-fill:{lockKey}",
            cancellationToken);
        return await BatchExecuteFillUnlockedAsync(user, request, cancellationToken);
    }

    private static string BuildFillExecutionRequestFingerprint(BatchExecuteFillRequest request)
    {
        // ExecutionRequestId 只标识一次逻辑操作，不能参与请求内容指纹；其余字段均会
        // 影响写回内容、匹配门禁或历史快照，必须纳入稳定序列化结果。
        var payload = JsonSerializer.Serialize(new
        {
            request.FileId,
            request.CustomerId,
            request.ProcessId,
            request.MachineModelId,
            request.PreviewTables,
            request.Tables,
            request.Config
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static string BuildScopedFillTaskId(MatchingUserContext user, string executionRequestId)
    {
        var input = $"{user.CompanyId}:{user.UserId}:{executionRequestId.Trim()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..32].ToLowerInvariant();
    }

    private static async Task<FillTaskResult?> LoadIdempotentTaskAsync(
        MatchingTaskSnapshotService snapshotService,
        MatchingUserContext user,
        string executionRequestId)
    {
        var scopedTaskId = BuildScopedFillTaskId(user, executionRequestId);
        var task = await snapshotService.LoadAsync(user, scopedTaskId);
        if (task != null)
        {
            return task;
        }

        // 兼容升级前直接以 executionRequestId 作为主键保存的任务。LoadAsync 仍会
        // 校验归属，因此不会借兼容路径跨租户读取。
        return await snapshotService.LoadAsync(user, executionRequestId);
    }

    private async Task<FillTaskResult?> LoadTaskFromFreshScopeAsync(
        MatchingUserContext user,
        string taskId)
    {
        using var scope = _scopeFactory.CreateScope();
        var snapshotService = scope.ServiceProvider.GetRequiredService<MatchingTaskSnapshotService>();
        return await snapshotService.LoadAsync(user, taskId);
    }

    private async Task RecoverPendingTaskFromFreshScopeAsync(
        MatchingUserContext user,
        string taskId,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var snapshotService = scope.ServiceProvider.GetRequiredService<MatchingTaskSnapshotService>();
        var task = await snapshotService.LoadAsync(user, taskId);
        if (task?.FileMutationPending == true)
        {
            await snapshotService.RecoverPendingFileMutationAsync(user, task, cancellationToken);
        }
    }

    private static void EnsureIdempotentFillRequestMatches(
        FillTaskResult existingTask,
        int sourceFileId,
        string requestFingerprint)
    {
        if (existingTask.SourceFileId != sourceFileId)
        {
            throw Failure(409, "执行幂等键已被其他文件使用");
        }

        if (string.IsNullOrWhiteSpace(existingTask.RequestFingerprint))
        {
            throw Failure(409, "执行幂等键对应的历史任务缺少请求指纹，请使用新的幂等键重试");
        }

        if (!string.Equals(existingTask.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
        {
            throw Failure(409, "执行幂等键已被不同的填充请求使用");
        }
    }
}
