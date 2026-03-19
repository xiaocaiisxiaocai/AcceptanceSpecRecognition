using System.Security.Claims;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// Token 发行与校验服务
/// </summary>
public interface IAuthTokenService
{
    AuthTokenPair CreateTokenPair(AuthTokenUser user);

    ClaimsPrincipal? ValidateRefreshToken(string refreshToken);
}

/// <summary>
/// Token 生成所需用户上下文
/// </summary>
public class AuthTokenUser
{
    public int UserId { get; set; }

    public int CompanyId { get; set; }

    public string Username { get; set; } = string.Empty;

    public int PermissionVersion { get; set; }

    public List<string> Roles { get; set; } = [];

    public List<string> Permissions { get; set; } = [];
}

/// <summary>
/// Access/Refresh Token 对
/// </summary>
public class AuthTokenPair
{
    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public DateTime AccessTokenExpiresAt { get; set; }
}
