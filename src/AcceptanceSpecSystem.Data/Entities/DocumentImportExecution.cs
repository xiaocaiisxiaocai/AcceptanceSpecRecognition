namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 文档导入幂等执行快照。请求键只在所属用户/公司内有业务含义，RequestKey 为服务端派生的全局键。
/// </summary>
public sealed class DocumentImportExecution
{
    public int Id { get; set; }
    public string RequestKey { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public int SourceFileId { get; set; }
    public int CreatedByUserId { get; set; }
    public int CompanyId { get; set; }
    public bool CleanupRequested { get; set; }
    public bool CleanupCompleted { get; set; }
    public string ResultJson { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
}
