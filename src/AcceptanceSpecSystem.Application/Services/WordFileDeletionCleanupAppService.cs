using System.Data;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AcceptanceSpecSystem.Application.Services;

public interface IWordFileDeletionCleanupAppService
{
    Task<int> RunBatchAsync(int batchSize, CancellationToken cancellationToken);
}

/// <summary>
/// 持久文件删除编排器。候选领取和每条处理均使用独立作用域，防止失败状态污染后续记录。
/// </summary>
public sealed class WordFileDeletionCleanupAppService : IWordFileDeletionCleanupAppService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WordFileDeletionCleanupAppService> _logger;

    public WordFileDeletionCleanupAppService(
        IServiceScopeFactory scopeFactory,
        ILogger<WordFileDeletionCleanupAppService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<int> RunBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        batchSize = Math.Clamp(batchSize, 1, 1000);
        var now = DateTime.UtcNow;
        int[] candidateIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            candidateIds = await db.WordFiles
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(file =>
                    file.DeletionStatus == WordFileDeletionStatus.PendingDeletion &&
                    (file.NextDeletionAttemptAt == null || file.NextDeletionAttemptAt <= now) &&
                    (file.DeletionLeaseToken == null ||
                     file.DeletionLeaseExpiresAt == null ||
                     file.DeletionLeaseExpiresAt <= now))
                .OrderBy(file => file.Id)
                .Select(file => file.Id)
                .Take(batchSize)
                .ToArrayAsync(cancellationToken);
        }

        var completed = 0;
        foreach (var id in candidateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = Guid.NewGuid().ToString("N");
            var claimNow = DateTime.UtcNow;
            using var claimScope = _scopeFactory.CreateScope();
            var db = claimScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var affected = await db.WordFiles
                .IgnoreQueryFilters()
                .Where(file =>
                    file.Id == id &&
                    file.DeletionStatus == WordFileDeletionStatus.PendingDeletion &&
                    (file.NextDeletionAttemptAt == null || file.NextDeletionAttemptAt <= now) &&
                    (file.DeletionLeaseToken == null ||
                     file.DeletionLeaseExpiresAt == null ||
                     file.DeletionLeaseExpiresAt <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(file => file.DeletionLeaseToken, token)
                    .SetProperty(file => file.DeletionLeaseExpiresAt, claimNow.Add(LeaseDuration)),
                    cancellationToken);
            if (affected != 1)
                continue;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await ProcessClaimedItemAsync(id, token, cancellationToken))
                    completed++;
            }
            catch (OperationCanceledException)
            {
                await ReleaseLeaseAsync(id, token);
                throw;
            }
            catch (Exception exception)
            {
                await RecordFailureAsync(id, token, ClassifyFailure(exception));
                _logger.LogWarning(exception, "待删除文件清理失败，已安排重试: {FileId} {Category}", id, ClassifyFailure(exception));
            }
        }

        return completed;
    }

    public static TimeSpan CalculateRetryDelay(int retryCount)
    {
        if (retryCount <= 1)
            return TimeSpan.FromMinutes(1);
        var exponent = Math.Min(retryCount - 1, 11);
        return TimeSpan.FromMinutes(Math.Min(Math.Pow(2, exponent), 24 * 60));
    }

    private async Task<bool> ProcessClaimedItemAsync(int id, string token, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var file = await GetLockedFileAsync(db, id, cancellationToken);
        if (file == null ||
            file.DeletionStatus != WordFileDeletionStatus.PendingDeletion ||
            !string.Equals(file.DeletionLeaseToken, token, StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        if (await HasReferencesAsync(db, id, cancellationToken))
            throw new WordFileReferencedException();

        await storage.DeleteUploadedWordFileIfExistsAsync(file.FilePath, file.FileType, cancellationToken);
        db.WordFiles.Remove(file);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task<WordFile?> GetLockedFileAsync(
        AppDbContext db,
        int id,
        CancellationToken cancellationToken)
    {
        if (db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true)
        {
            var transaction = db.Database.CurrentTransaction
                ?? throw new InvalidOperationException("待删除文件行锁必须在数据库事务内获取");
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "SELECT `Id` FROM `WordFiles` WHERE `Id` = @id FOR UPDATE;";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@id";
            parameter.Value = id;
            command.Parameters.Add(parameter);
            var lockedId = await command.ExecuteScalarAsync(cancellationToken);
            if (lockedId == null)
                return null;
            return await db.WordFiles
                .IgnoreQueryFilters()
                .SingleAsync(file => file.Id == id, cancellationToken);
        }

        return await db.WordFiles
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(file => file.Id == id, cancellationToken);
    }

    internal static async Task<bool> HasReferencesAsync(
        AppDbContext db,
        int id,
        CancellationToken cancellationToken)
    {
        return await db.AcceptanceSpecs.AnyAsync(item => item.WordFileId == id, cancellationToken) ||
               await db.MatchingFillTasks.AnyAsync(item => item.SourceFileId == id, cancellationToken) ||
               await db.DocumentImportExecutions.AnyAsync(item => item.SourceFileId == id, cancellationToken);
    }

    private async Task RecordFailureAsync(int id, string token, string category)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<WordFileDeletionLeaseStore>();
        await store.RecordFailureAsync(id, token, category, CancellationToken.None);
    }

    private async Task ReleaseLeaseAsync(int id, string token)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.WordFiles
            .IgnoreQueryFilters()
            .Where(item => item.Id == id && item.DeletionLeaseToken == token)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.DeletionLeaseToken, (string?)null)
                .SetProperty(item => item.DeletionLeaseExpiresAt, (DateTime?)null),
                CancellationToken.None);
    }

    public static string ClassifyFailure(Exception exception) => exception switch
    {
        WordFileReferencedException => "Referenced",
        UnsafeWordFilePathException => "UnsafePath",
        UnauthorizedAccessException => "AccessDenied",
        IOException => "IoError",
        _ => "Unexpected"
    };

    private sealed class WordFileReferencedException : InvalidOperationException;
}
