namespace AcceptanceSpecSystem.Application.Options;

/// <summary>
/// 已上传文档解析快照缓存配置。
/// </summary>
public sealed class UploadedDocumentSnapshotOptions
{
    public const string SectionName = "UploadedDocumentSnapshot";

    /// <summary>
    /// 是否启用跨请求快照复用。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 滑动过期秒数。
    /// </summary>
    public int SlidingExpirationSeconds { get; set; } = 120;

    /// <summary>
    /// 缓存总字节预算。
    /// </summary>
    public long TotalBudgetBytes { get; set; } = 128L * 1024 * 1024;

    /// <summary>
    /// 单条目最大字节预算。
    /// </summary>
    public long MaxEntryBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>
    /// 单条目最小计费单位。
    /// </summary>
    public long MinEntryChargeBytes { get; set; } = 1024 * 1024;
}
