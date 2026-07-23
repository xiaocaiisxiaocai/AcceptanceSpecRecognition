using AcceptanceSpecSystem.Application.Options;

namespace AcceptanceSpecSystem.Application.Services;

public interface IDatabaseBackupExecutor
{
    Task<DatabaseBackupExecutionResult> BackupAsync(
        DatabaseBackupOptions options,
        CancellationToken cancellationToken);
}

public sealed record DatabaseBackupExecutionResult(string FileName, long FileSizeBytes);
