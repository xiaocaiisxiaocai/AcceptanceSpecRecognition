using System.Data;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AcceptanceSpecSystem.Application.Services;

public sealed class WordFileDeletionLeaseStore
{
    private readonly AppDbContext _db;

    public WordFileDeletionLeaseStore(AppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> RecordFailureAsync(
        int id,
        string leaseToken,
        string category,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        WordFile? file;
        if (_db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true)
        {
            await using var command = _db.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText =
                "SELECT `Id` FROM `WordFiles` " +
                "WHERE `Id` = @id AND `DeletionLeaseToken` = @token FOR UPDATE;";
            var idParameter = command.CreateParameter();
            idParameter.ParameterName = "@id";
            idParameter.Value = id;
            command.Parameters.Add(idParameter);
            var tokenParameter = command.CreateParameter();
            tokenParameter.ParameterName = "@token";
            tokenParameter.Value = leaseToken;
            command.Parameters.Add(tokenParameter);
            var lockedId = await command.ExecuteScalarAsync(cancellationToken);
            file = lockedId == null
                ? null
                : await _db.WordFiles.IgnoreQueryFilters()
                    .SingleAsync(item => item.Id == id, cancellationToken);
        }
        else
        {
            file = await _db.WordFiles
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    item => item.Id == id && item.DeletionLeaseToken == leaseToken,
                    cancellationToken);
        }

        if (file == null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        file.DeletionRetryCount++;
        file.LastDeletionError = category;
        file.NextDeletionAttemptAt = DateTime.UtcNow.Add(
            WordFileDeletionCleanupAppService.CalculateRetryDelay(file.DeletionRetryCount));
        file.DeletionLeaseToken = null;
        file.DeletionLeaseExpiresAt = null;
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
