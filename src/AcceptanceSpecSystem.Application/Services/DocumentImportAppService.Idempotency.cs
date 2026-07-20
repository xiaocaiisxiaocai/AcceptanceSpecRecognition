using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Application.Services;

public sealed partial class DocumentImportAppService
{
    private const int ImportExecutionRetentionHours = 24;
    private const int MaxPersistedImportErrors = 100;
    private const int MaxPersistedSkippedRows = 100;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ImportExecutionLocks = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions ImportResultJsonOptions = new(JsonSerializerDefaults.Web);

    private async Task<DocumentImportAppResult> ExecuteIdempotentImportAsync<TRequest>(
        SpecAccessContext scope,
        string? clientRequestId,
        TRequest request,
        Func<Task<WordFile>> authorizeReplay,
        Func<ImportIdempotencyContext?, Task<DocumentImportAppResult>> execute,
        CancellationToken cancellationToken)
    {
        var normalizedId = clientRequestId?.Trim();
        if (string.IsNullOrEmpty(normalizedId))
        {
            return await execute(null);
        }

        if (normalizedId.Length > 80 || normalizedId.Any(character =>
                !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ApplicationServiceException(400, "导入幂等键格式不合法");
        }

        var requestKey = BuildImportRequestKey(scope, normalizedId);
        var fingerprint = ComputeSha256Hex(JsonSerializer.Serialize(request, ImportResultJsonOptions));
        var gate = ImportExecutionLocks.GetOrAdd(requestKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // 快照只承担短期网络重试幂等，不永久保存导入明细。清理由请求路径顺手完成，
            // 即使后台清理任务未运行，过期键也不会继续占用唯一索引。
            await _importExecutions.DeleteExpiredAsync(DateTime.UtcNow, cancellationToken);
            var existing = await _importExecutions.GetByRequestKeyAsync(requestKey, cancellationToken);
            if (existing != null)
            {
                var restored = RestoreImportExecution(existing, fingerprint);
                // 幂等快照只能避免重复写入，不能替代当前时刻的访问控制。文件归属或
                // 目标范围发生变化后，旧请求也必须重新通过同一套授权校验。
                var sourceFile = await authorizeReplay();
                if (existing.CleanupRequested && !existing.CleanupCompleted)
                {
                    await TryCompleteSourceCleanupAsync(existing, sourceFile, "幂等回放", cancellationToken);
                }
                return restored;
            }

            try
            {
                return await execute(new ImportIdempotencyContext(requestKey, fingerprint));
            }
            catch (DbUpdateException ex) when (IsImportExecutionRequestKeyConflict(ex))
            {
                // 数据库唯一键是跨实例最终防线。并发赢家提交后，调用方使用同一键重试
                // 即会命中已持久化结果；当前请求明确返回冲突，绝不再次写入。
                throw new ApplicationServiceException(409, "相同导入请求正在或已经由其他实例处理，请使用同一幂等键重试");
            }
        }
        finally
        {
            gate.Release();
            if (gate.CurrentCount == 1)
            {
                ImportExecutionLocks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(requestKey, gate));
            }
        }
    }

    private DocumentImportAppResult RestoreImportExecution(
        DocumentImportExecution existing,
        string fingerprint)
    {
        if (!string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new ApplicationServiceException(409, "导入幂等键已被不同的请求内容使用");
        }

        var result = JsonSerializer.Deserialize<ImportResult>(existing.ResultJson, ImportResultJsonOptions);
        if (result == null)
        {
            throw new ApplicationServiceException(409, "导入幂等快照已损坏，请更换幂等键后重试");
        }

        return new DocumentImportAppResult(result, existing.Message);
    }

    private async Task AddImportExecutionSnapshotAsync(
        ImportIdempotencyContext? idempotency,
        SpecAccessContext scope,
        int sourceFileId,
        ImportResult result,
        string message,
        bool cleanupRequested)
    {
        if (idempotency == null)
        {
            return;
        }

        var createdAt = DateTime.UtcNow;
        await _importExecutions.AddAsync(new DocumentImportExecution
        {
            RequestKey = idempotency.RequestKey,
            RequestFingerprint = idempotency.RequestFingerprint,
            SourceFileId = sourceFileId,
            CreatedByUserId = scope.UserId,
            CompanyId = scope.CompanyId,
            CleanupRequested = cleanupRequested,
            CleanupCompleted = !cleanupRequested,
            ResultJson = JsonSerializer.Serialize(CreatePersistedImportResult(result), ImportResultJsonOptions),
            Message = message,
            CreatedAt = createdAt,
            ExpiresAt = createdAt.AddHours(ImportExecutionRetentionHours)
        });
    }

    private static ImportResult CreatePersistedImportResult(ImportResult result)
    {
        // RowValues 可能包含整张客户表的原始文本。幂等回放只需要计数、错误摘要和
        // 待确认决定，不保存完整跳过行，避免 ResultJson 随大文件无限膨胀。
        return new ImportResult
        {
            SuccessCount = result.SuccessCount,
            FailedCount = result.FailedCount,
            SkippedCount = result.SkippedCount,
            TotalCount = result.TotalCount,
            Errors = result.Errors
                .Take(MaxPersistedImportErrors)
                .Select(error => new ImportError
                {
                    RowIndex = error.RowIndex,
                    Message = error.Message
                })
                .ToList(),
            SkippedRows = result.SkippedRows
                .Take(MaxPersistedSkippedRows)
                .Select(row => new ImportSkippedRow
                {
                    RowIndex = row.RowIndex,
                    Message = row.Message,
                    RowValues = []
                })
                .ToList(),
            RequiresConfirmation = result.RequiresConfirmation,
            PendingCount = result.PendingCount,
            PendingDifferences = result.PendingDifferences,
            ProjectBackfilledFromSpecification = result.ProjectBackfilledFromSpecification
        };
    }

    private static bool IsImportExecutionRequestKeyConflict(DbUpdateException exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("IX_DocumentImportExecutions_RequestKey", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("DocumentImportExecutions.RequestKey", StringComparison.OrdinalIgnoreCase))
            {
                return message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                       message.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
                       message.Contains("constraint", StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private static string BuildImportRequestKey(SpecAccessContext scope, string clientRequestId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{scope.CompanyId}:{scope.UserId}:{clientRequestId}"));
        return "import_" + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string ComputeSha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record ImportIdempotencyContext(string RequestKey, string RequestFingerprint);
}
