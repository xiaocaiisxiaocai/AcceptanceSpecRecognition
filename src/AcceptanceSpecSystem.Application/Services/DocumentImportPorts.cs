using AcceptanceSpecSystem.Core.Documents.Models;
using AcceptanceSpecSystem.Data.Entities;

namespace AcceptanceSpecSystem.Application.Services;

public interface IDocumentImportTableReader
{
    Task<IReadOnlyList<TableInfo>> GetTablesAsync(
        WordFile wordFile,
        CancellationToken cancellationToken = default);

    Task<TableData> ExtractTableDataAsync(
        WordFile wordFile,
        int tableIndex,
        ColumnMapping mapping,
        int? maxDataRowCount = null,
        CancellationToken cancellationToken = default);
}

public interface IImportEmbeddingCache
{
    Task<IReadOnlyDictionary<int, float[]>> GetImportDuplicateEmbeddingsAsync(
        IReadOnlyCollection<AcceptanceSpec> specs,
        int? embeddingServiceId,
        CancellationToken cancellationToken = default);

    Task RemoveSpecCachesAsync(int specId);
}

public interface IImportWarmupTrigger
{
    bool Request();
}
