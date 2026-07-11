namespace AcceptanceSpecSystem.Api.Options;

/// <summary>
/// BatchReply 临时会话与下载产物的后台清理配置。
/// </summary>
public sealed class BatchReplyCleanupOptions
{
    public const string SectionName = "BatchReplyCleanup";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 观察模式只扫描和记录，不删除文件。首次部署默认开启。
    /// </summary>
    public bool ObservationMode { get; set; } = true;

    public int InitialDelaySeconds { get; set; } = 30;

    public int CleanupIntervalMinutes { get; set; } = 15;

    public int SessionRetentionMinutes { get; set; } = 240;

    public int ArtifactRetentionMinutes { get; set; } = 1440;
}
