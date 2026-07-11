using System.Security.Cryptography;
using System.Text;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.EntityFrameworkCore;
using AcceptanceSpecSystem.Application.Options;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Application.Services;

public interface IAuthRefreshSessionService
{
    Task CreateAsync(int userId, int permissionVersion, string refreshToken, DateTime expiresAt, CancellationToken cancellationToken);
    Task<RefreshSessionRotationResult> RotateAsync(string refreshToken, CancellationToken cancellationToken);
    Task RevokeByTokenAsync(string refreshToken, string reason, CancellationToken cancellationToken);
    Task RevokeUserSessionsAsync(int userId, string reason, CancellationToken cancellationToken);
}

public sealed record RefreshSessionRotationResult(
    RefreshSessionRotationStatus Status,
    int? UserId = null,
    string? FamilyId = null,
    string? ReplacementToken = null,
    DateTime? ReplacementExpiresAt = null);

public enum RefreshSessionRotationStatus
{
    Success,
    Invalid,
    Expired,
    UserInvalid,
    PermissionVersionChanged,
    ReplayDetected
}

/// <summary>
/// 一次性 RefreshToken 会话族。原始令牌只存在于请求/响应 Cookie，数据库仅保存 SHA-256 摘要。
/// </summary>
public sealed class AuthRefreshSessionService : IAuthRefreshSessionService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AuthRefreshSessionService> _logger;
    private readonly JwtAuthOptions _jwtOptions;
    private readonly ReferenceCountedKeyedLock<string> _rotationLocks;

    public AuthRefreshSessionService(
        AppDbContext db,
        ILogger<AuthRefreshSessionService> logger,
        IOptions<JwtAuthOptions> jwtOptions,
        ReferenceCountedKeyedLock<string> rotationLocks)
    {
        _db = db;
        _logger = logger;
        _jwtOptions = jwtOptions.Value;
        _rotationLocks = rotationLocks;
    }

    public async Task CreateAsync(int userId, int permissionVersion, string refreshToken, DateTime expiresAt, CancellationToken cancellationToken)
    {
        _db.AuthRefreshSessions.Add(new AuthRefreshSession
        {
            FamilyId = Guid.NewGuid().ToString("N"),
            UserId = userId,
            PermissionVersion = permissionVersion,
            TokenHash = HashToken(refreshToken),
            ExpiresAt = expiresAt
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<RefreshSessionRotationResult> RotateAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return new(RefreshSessionRotationStatus.Invalid);

        var tokenHash = HashToken(refreshToken);
        using var rotationLease = await _rotationLocks.AcquireAsync(tokenHash, cancellationToken);
        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var now = DateTime.UtcNow;
        var claimed = await _db.AuthRefreshSessions
            .Where(item => item.TokenHash == tokenHash && item.Status == AuthRefreshSessionStatus.Active)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, AuthRefreshSessionStatus.Rotated)
                .SetProperty(item => item.RotatedAt, now), cancellationToken);

        var session = await _db.AuthRefreshSessions.AsNoTracking()
            .Include(item => item.User)
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);

        if (session == null)
            return new(RefreshSessionRotationStatus.Invalid);

        if (claimed == 0)
        {
            if (session.Status == AuthRefreshSessionStatus.Rotated)
            {
                await RevokeFamilyCoreAsync(session.FamilyId, "refresh-token-replay", cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                _logger.LogWarning("检测到 RefreshToken 重放，已撤销会话族。UserId={UserId}, FamilyId={FamilyId}", session.UserId, session.FamilyId);
                return new(RefreshSessionRotationStatus.ReplayDetected, session.UserId, session.FamilyId);
            }

            return new(RefreshSessionRotationStatus.Invalid, session.UserId, session.FamilyId);
        }

        if (session.ExpiresAt <= now)
        {
            await RevokeFamilyCoreAsync(session.FamilyId, "expired", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(RefreshSessionRotationStatus.Expired, session.UserId, session.FamilyId);
        }

        if (!session.User.IsActive)
        {
            await RevokeFamilyCoreAsync(session.FamilyId, "user-disabled", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(RefreshSessionRotationStatus.UserInvalid, session.UserId, session.FamilyId);
        }

        if (session.PermissionVersion != session.User.PermissionVersion)
        {
            await RevokeFamilyCoreAsync(session.FamilyId, "permission-version-changed", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(RefreshSessionRotationStatus.PermissionVersionChanged, session.UserId, session.FamilyId);
        }

        var replacementToken = CreateRandomToken();
        var replacementExpiresAt = now.AddDays(Math.Max(1, _jwtOptions.RefreshTokenDays));
        _db.AuthRefreshSessions.Add(new AuthRefreshSession
        {
            FamilyId = session.FamilyId,
            UserId = session.UserId,
            PermissionVersion = session.PermissionVersion,
            TokenHash = HashToken(replacementToken),
            ExpiresAt = replacementExpiresAt,
            CreatedAt = now
        });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(RefreshSessionRotationStatus.Success, session.UserId, session.FamilyId, replacementToken, replacementExpiresAt);
    }

    public async Task RevokeByTokenAsync(string refreshToken, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return;

        var tokenHash = HashToken(refreshToken);
        var session = await _db.AuthRefreshSessions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.TokenHash == tokenHash, cancellationToken);
        if (session != null)
            await RevokeFamilyCoreAsync(session.FamilyId, reason, cancellationToken);
    }

    public Task RevokeUserSessionsAsync(int userId, string reason, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return _db.AuthRefreshSessions
            .Where(item => item.UserId == userId && item.Status == AuthRefreshSessionStatus.Active)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, AuthRefreshSessionStatus.Revoked)
                .SetProperty(item => item.RevokedAt, now)
                .SetProperty(item => item.RevocationReason, reason), cancellationToken);
    }

    private Task RevokeFamilyCoreAsync(string familyId, string reason, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return _db.AuthRefreshSessions
            .Where(item => item.FamilyId == familyId && item.Status == AuthRefreshSessionStatus.Active)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, AuthRefreshSessionStatus.Revoked)
                .SetProperty(item => item.RevokedAt, now)
                .SetProperty(item => item.RevocationReason, reason), cancellationToken);
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string CreateRandomToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
