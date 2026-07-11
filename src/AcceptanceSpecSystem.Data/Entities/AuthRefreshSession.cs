namespace AcceptanceSpecSystem.Data.Entities;

/// <summary>
/// 浏览器 RefreshToken 的服务端会话记录。数据库只保存令牌摘要，不保存原始凭据。
/// </summary>
public class AuthRefreshSession
{
    public long Id { get; set; }

    public string FamilyId { get; set; } = string.Empty;

    public int UserId { get; set; }

    public int PermissionVersion { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public AuthRefreshSessionStatus Status { get; set; } = AuthRefreshSessionStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public DateTime? RotatedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public string? RevocationReason { get; set; }

    public SystemUser User { get; set; } = null!;
}

public enum AuthRefreshSessionStatus
{
    Active = 1,
    Rotated = 2,
    Revoked = 3
}
