namespace AcceptanceSpecSystem.Api.Options;

/// <summary>
/// 向量缓存预热配置选项
/// </summary>
public class EmbeddingCacheWarmupOptions
{
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "EmbeddingCacheWarmup";

    /// <summary>
    /// 是否启用自动预热
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 是否在服务启动后立即执行一次预热
    /// </summary>
    public bool RunOnStartup { get; set; } = false;

    /// <summary>
    /// 每日本地执行时间，格式 HH:mm 或 HH:mm:ss；为空时按间隔执行
    /// </summary>
    public string? RunAtLocalTime { get; set; }

    /// <summary>
    /// 预热间隔小时数
    /// </summary>
    public int IntervalHours { get; set; } = 24;

    /// <summary>
    /// 单批处理数量
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// 单次预热最多处理数量
    /// </summary>
    public int MaxItemsPerRun { get; set; } = 1000;
}
