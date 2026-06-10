namespace AcceptanceSpecSystem.Api.Options;

/// <summary>
/// 向量缓存清理配置选项
/// </summary>
public class EmbeddingCacheCleanupOptions
{
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "EmbeddingCacheCleanup";

    /// <summary>
    /// 是否启用自动清理
    /// </summary>
    public bool EnableAutoCleanup { get; set; } = true;

    /// <summary>
    /// 清理间隔（小时），默认 24 小时
    /// </summary>
    public int CleanupIntervalHours { get; set; } = 24;

    /// <summary>
    /// 缓存保留天数（早于此天数的缓存将被删除），默认 30 天
    /// </summary>
    public int RetentionDays { get; set; } = 30;
}
