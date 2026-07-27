namespace AcceptanceSpecSystem.Application.Services;

/// <summary>
/// 四类批量删除共用的输入边界。
/// </summary>
public static class BatchDeleteInputNormalizer
{
    public const int MaxBatchDeleteItems = 500;

    public static IReadOnlyList<int> Normalize(
        IEnumerable<int> ids,
        string emptyMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        cancellationToken.ThrowIfCancellationRequested();

        var normalized = new List<int>(MaxBatchDeleteItems);
        var seen = new HashSet<int>();
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (id <= 0 || !seen.Add(id))
                continue;

            if (normalized.Count == MaxBatchDeleteItems)
            {
                throw new ApplicationServiceException(
                    422,
                    "单次最多删除500项，请缩小范围后重试");
            }

            normalized.Add(id);
        }

        if (normalized.Count == 0)
            throw new ApplicationServiceException(400, emptyMessage);

        cancellationToken.ThrowIfCancellationRequested();
        return normalized;
    }
}
