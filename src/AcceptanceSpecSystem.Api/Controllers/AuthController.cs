using System.Security.Claims;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 登录与令牌接口
/// </summary>
[ApiController]
[Route("")]
public class AuthController : ControllerBase
{
    private readonly IAuthTokenService _authTokenService;
    private readonly IAuthLoginAppService _loginAppService;
    private readonly IAuthAccessService _authAccessService;
    private readonly IAuthRefreshSessionService _refreshSessions;
    private readonly IBrowserAuthSecurityService _browserSecurity;
    private readonly BrowserAuthOptions _browserOptions;

    public AuthController(
        IAuthTokenService authTokenService,
        IAuthLoginAppService loginAppService,
        IAuthAccessService authAccessService,
        IAuthRefreshSessionService refreshSessions,
        IBrowserAuthSecurityService browserSecurity,
        IOptions<BrowserAuthOptions> browserOptions)
    {
        _authTokenService = authTokenService;
        _loginAppService = loginAppService;
        _authAccessService = authAccessService;
        _refreshSessions = refreshSessions;
        _browserSecurity = browserSecurity;
        _browserOptions = browserOptions.Value;
    }

    /// <summary>
    /// 登录
    /// </summary>
    [HttpPost("login")]
    [AuditOperation("login", "auth")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<FrontendAuthResponse<LoginSuccessData>>> Login(
        [FromBody] LoginRequest? request,
        CancellationToken cancellationToken = default)
    {
        var username = request?.Username?.Trim() ?? string.Empty;
        var password = request?.Password ?? string.Empty;
        HttpContext.Items["AuditUsername"] = username;

        if (!_browserSecurity.ValidateTrustedOrigin(Request, out var originError))
            return StatusCode(StatusCodes.Status403Forbidden, AuthFailure<LoginSuccessData>(originError));

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return Unauthorized(new FrontendAuthResponse<LoginSuccessData>
            {
                Success = false,
                Data = null,
                Message = "用户名或密码不能为空"
            });
        }

        var login = await _loginAppService.AuthenticateAsync(username, password, cancellationToken);
        if (login.Status == AuthLoginStatus.InvalidCredentials)
        {
            return Unauthorized(new FrontendAuthResponse<LoginSuccessData>
            {
                Success = false,
                Data = null,
                Message = "用户名或密码错误"
            });
        }

        var access = login.Access;
        if (login.Status != AuthLoginStatus.Success || access == null)
        {
            return Unauthorized(new FrontendAuthResponse<LoginSuccessData>
            {
                Success = false,
                Data = null,
                Message = "用户不存在或已停用"
            });
        }

        var permissions = access.Permissions.ToList();
        var tokenUser = new AuthTokenUser
        {
            UserId = access.UserId,
            CompanyId = access.CompanyId,
            Username = access.Username,
            PermissionVersion = access.PermissionVersion,
            RoleCode = access.RoleCode,
            Permissions = permissions
        };
        var pair = _authTokenService.CreateTokenPair(tokenUser);
        await _refreshSessions.CreateAsync(
            tokenUser.UserId,
            tokenUser.PermissionVersion,
            pair.RefreshToken,
            pair.RefreshTokenExpiresAt,
            cancellationToken);
        _browserSecurity.WriteSessionCookies(Response, pair.RefreshToken, pair.RefreshTokenExpiresAt);
        return Ok(new FrontendAuthResponse<LoginSuccessData>
        {
            Success = true,
            Data = new LoginSuccessData
            {
                Avatar = access.Avatar,
                Username = access.Username,
                Nickname = string.IsNullOrWhiteSpace(access.Nickname) ? access.Username : access.Nickname,
                RoleCode = access.RoleCode,
                Permissions = permissions,
                AccessToken = pair.AccessToken,
                Expires = pair.AccessTokenExpiresAt
            }
        });
    }

    /// <summary>
    /// 刷新令牌
    /// </summary>
    [HttpPost("refresh-token")]
    [AuditOperation("refresh-token", "auth")]
    [AllowAnonymous]
    [EnableRateLimiting("refresh-token")]
    public async Task<ActionResult<FrontendAuthResponse<RefreshTokenSuccessData>>> RefreshToken(
        CancellationToken cancellationToken = default)
    {
        if (!_browserSecurity.ValidateStateChangingRequest(Request, out var csrfError))
            return StatusCode(StatusCodes.Status403Forbidden, AuthFailure<RefreshTokenSuccessData>(csrfError));

        var refreshToken = Request.Cookies[_browserOptions.RefreshCookieName]?.Trim() ?? string.Empty;

        var rotation = await _refreshSessions.RotateAsync(refreshToken, cancellationToken);
        if (rotation.Status != RefreshSessionRotationStatus.Success || rotation.UserId == null ||
            string.IsNullOrWhiteSpace(rotation.ReplacementToken) || rotation.ReplacementExpiresAt == null)
        {
            _browserSecurity.ClearSessionCookies(Response);
            var message = rotation.Status == RefreshSessionRotationStatus.ReplayDetected
                ? "检测到异常会话重放，请重新登录"
                : "RefreshToken 无效或已过期";
            return Unauthorized(AuthFailure<RefreshTokenSuccessData>(message));
        }

        var access = await _authAccessService.GetByUserIdAsync(rotation.UserId.Value, cancellationToken);
        HttpContext.Items["AuditUsername"] = access?.Username;

        if (access == null || !access.IsActive)
        {
            await _refreshSessions.RevokeByTokenAsync(rotation.ReplacementToken, "access-context-invalid", cancellationToken);
            _browserSecurity.ClearSessionCookies(Response);
            return Unauthorized(AuthFailure<RefreshTokenSuccessData>("用户不存在或已停用"));
        }

        var pair = _authTokenService.CreateTokenPair(new AuthTokenUser
        {
            UserId = access.UserId,
            CompanyId = access.CompanyId,
            Username = access.Username,
            PermissionVersion = access.PermissionVersion,
            RoleCode = access.RoleCode,
            Permissions = access.Permissions.ToList()
        });
        _browserSecurity.WriteSessionCookies(Response, rotation.ReplacementToken, rotation.ReplacementExpiresAt.Value);
        return Ok(new FrontendAuthResponse<RefreshTokenSuccessData>
        {
            Success = true,
            Data = new RefreshTokenSuccessData
            {
                Avatar = access.Avatar,
                Username = access.Username,
                Nickname = string.IsNullOrWhiteSpace(access.Nickname) ? access.Username : access.Nickname,
                RoleCode = access.RoleCode,
                Permissions = access.Permissions.ToList(),
                AccessToken = pair.AccessToken,
                Expires = pair.AccessTokenExpiresAt
            }
        });
    }

    /// <summary>
    /// 退出当前浏览器会话。
    /// </summary>
    [HttpPost("logout")]
    [AuditOperation("logout", "auth")]
    [AllowAnonymous]
    public async Task<ActionResult<FrontendAuthResponse<object>>> Logout(CancellationToken cancellationToken = default)
    {
        if (!_browserSecurity.ValidateStateChangingRequest(Request, out var csrfError))
            return StatusCode(StatusCodes.Status403Forbidden, AuthFailure<object>(csrfError));

        var refreshToken = Request.Cookies[_browserOptions.RefreshCookieName]?.Trim() ?? string.Empty;
        await _refreshSessions.RevokeByTokenAsync(refreshToken, "user-logout", cancellationToken);
        _browserSecurity.ClearSessionCookies(Response);
        return Ok(new FrontendAuthResponse<object> { Success = true, Data = new { } });
    }

    private static FrontendAuthResponse<T> AuthFailure<T>(string message) => new()
    {
        Success = false,
        Data = default,
        Message = message
    };
}
