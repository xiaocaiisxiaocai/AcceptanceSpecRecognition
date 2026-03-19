using System.Security.Claims;
using AcceptanceSpecSystem.Api.DTOs;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Data.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcceptanceSpecSystem.Api.Controllers;

/// <summary>
/// 登录与路由初始化接口（兼容前端 pure-admin 默认协议）
/// </summary>
[ApiController]
[Route("")]
public class AuthController : ControllerBase
{
    private readonly IAuthTokenService _authTokenService;
    private readonly IAuthPasswordService _authPasswordService;
    private readonly IAuthAccessService _authAccessService;
    private readonly IUnitOfWork _unitOfWork;

    public AuthController(
        IAuthTokenService authTokenService,
        IAuthPasswordService authPasswordService,
        IAuthAccessService authAccessService,
        IUnitOfWork unitOfWork)
    {
        _authTokenService = authTokenService;
        _authPasswordService = authPasswordService;
        _authAccessService = authAccessService;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// 登录
    /// </summary>
    [HttpPost("login")]
    [AuditOperation("login", "auth")]
    [AllowAnonymous]
    public async Task<ActionResult<FrontendAuthResponse<LoginSuccessData>>> Login([FromBody] LoginRequest? request)
    {
        var username = request?.Username?.Trim() ?? string.Empty;
        var password = request?.Password ?? string.Empty;
        HttpContext.Items["AuditUsername"] = username;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return Unauthorized(new FrontendAuthResponse<LoginSuccessData>
            {
                Success = false,
                Data = null,
                Message = "用户名或密码不能为空"
            });
        }

        var user = await _unitOfWork.SystemUsers.GetByUsernameAsync(username);
        if (user == null || !user.IsActive || !_authPasswordService.VerifyPassword(user.PasswordHash, password))
        {
            return Unauthorized(new FrontendAuthResponse<LoginSuccessData>
            {
                Success = false,
                Data = null,
                Message = "用户名或密码错误"
            });
        }

        var access = await _authAccessService.GetByUsernameAsync(username);
        if (access == null || !access.IsActive)
        {
            return Unauthorized(new FrontendAuthResponse<LoginSuccessData>
            {
                Success = false,
                Data = null,
                Message = "用户不存在或已停用"
            });
        }

        var roles = access.Roles.ToList();
        var permissions = access.Permissions.ToList();
        var tokenUser = new AuthTokenUser
        {
            UserId = access.UserId,
            CompanyId = access.CompanyId,
            Username = user.Username,
            PermissionVersion = access.PermissionVersion,
            Roles = roles,
            Permissions = permissions
        };
        var pair = _authTokenService.CreateTokenPair(tokenUser);
        return Ok(new FrontendAuthResponse<LoginSuccessData>
        {
            Success = true,
            Data = new LoginSuccessData
            {
                Avatar = user.Avatar,
                Username = user.Username,
                Nickname = string.IsNullOrWhiteSpace(user.Nickname) ? user.Username : user.Nickname,
                Roles = roles,
                Permissions = permissions,
                AccessToken = pair.AccessToken,
                RefreshToken = pair.RefreshToken,
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
    public async Task<ActionResult<FrontendAuthResponse<RefreshTokenSuccessData>>> RefreshToken([FromBody] RefreshTokenRequest? request)
    {
        var refreshToken = request?.RefreshToken?.Trim() ?? string.Empty;
        var principal = _authTokenService.ValidateRefreshToken(refreshToken);
        if (principal == null)
        {
            return Unauthorized(new FrontendAuthResponse<RefreshTokenSuccessData>
            {
                Success = false,
                Data = null,
                Message = "RefreshToken 无效或已过期"
            });
        }

        var userIdClaim = principal.FindFirstValue("user_id");
        var username = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? principal.FindFirstValue(ClaimTypes.Name)
                       ?? principal.FindFirstValue("sub");
        HttpContext.Items["AuditUsername"] = username;

        AuthAccessContext? access = null;
        if (int.TryParse(userIdClaim, out var userId))
        {
            access = await _authAccessService.GetByUserIdAsync(userId);
        }

        if (access == null && !string.IsNullOrWhiteSpace(username))
        {
            access = await _authAccessService.GetByUsernameAsync(username);
        }

        if (access == null || !access.IsActive)
        {
            return Unauthorized(new FrontendAuthResponse<RefreshTokenSuccessData>
            {
                Success = false,
                Data = null,
                Message = "用户不存在或已停用"
            });
        }

        var pair = _authTokenService.CreateTokenPair(new AuthTokenUser
        {
            UserId = access.UserId,
            CompanyId = access.CompanyId,
            Username = access.Username,
            PermissionVersion = access.PermissionVersion,
            Roles = access.Roles.ToList(),
            Permissions = access.Permissions.ToList()
        });
        return Ok(new FrontendAuthResponse<RefreshTokenSuccessData>
        {
            Success = true,
            Data = new RefreshTokenSuccessData
            {
                AccessToken = pair.AccessToken,
                RefreshToken = pair.RefreshToken,
                Expires = pair.AccessTokenExpiresAt
            }
        });
    }

    /// <summary>
    /// 获取动态路由（当前返回空数组，沿用前端 pure-admin 初始化协议）
    /// </summary>
    [HttpGet("get-async-routes")]
    [AllowAnonymous]
    public IActionResult GetAsyncRoutes()
    {
        return Ok(new
        {
            success = true,
            data = Array.Empty<object>()
        });
    }
}
