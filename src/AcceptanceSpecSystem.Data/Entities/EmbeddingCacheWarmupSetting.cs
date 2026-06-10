namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 向量缓存预热运行期覆盖配置。
/// </summary>
public class EmbeddingCacheWarmupSetting
{
    /// <summary>
    /// 主键。当前只保留一条覆盖配置记录。
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 是否启用自动预热。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 是否在服务启动后立即执行一次预热。
    /// </summary>
    public bool RunOnStartup { get; set; }

    /// <summary>
    /// 每日本地执行时间，格式 HH:mm 或 HH:mm:ss；为空时按间隔执行。
    /// </summary>
    public string? RunAtLocalTime { get; set; }

    /// <summary>
    /// 预热间隔小时数。
    /// </summary>
    public int IntervalHours { get; set; }

    /// <summary>
    /// 单批处理数量。
    /// </summary>
    public int BatchSize { get; set; }

    /// <summary>
    /// 单次预热最多处理数量。
    /// </summary>
    public int MaxItemsPerRun { get; set; }

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 更新时间。
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
