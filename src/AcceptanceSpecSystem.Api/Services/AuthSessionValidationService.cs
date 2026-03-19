using System.Security.Claims;
using AcceptanceSpecSystem.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// 访问令牌会话校验结果
/// </summary>
public sealed class AuthSessionValidationResult
{
    public bool IsValid { get; init; }

    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// 访问令牌会话校验服务
/// </summary>
public interface IAuthSessionValidationService
{
    Task<AuthSessionValidationResult> ValidateAccessTokenAsync(ClaimsPrincipal? principal, CancellationToken cancellationToken = default);
}

/// <summary>
/// 访问令牌会话校验服务实现
/// </summary>
public sealed class AuthSessionValidationService : IAuthSessionValidationService
{
    private readonly AppDbContext _dbContext;

    public AuthSessionValidationService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AuthSessionValidationResult> ValidateAccessTokenAsync(
        ClaimsPrincipal? principal,
        CancellationToken cancellationToken = default)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return Invalid("访问令牌无效");
        }

        var tokenType = principal.FindFirstValue("token_type");
        if (!string.Equals(tokenType, "access", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("仅允许使用访问令牌访问接口");
        }

        if (!int.TryParse(principal.FindFirstValue("user_id"), out var userId) ||
            !int.TryParse(principal.FindFirstValue("company_id"), out var companyId) ||
            !int.TryParse(principal.FindFirstValue("permission_version"), out var permissionVersion))
        {
            return Invalid("访问令牌缺少必要的会话信息");
        }

        var user = await _dbContext.SystemUsers
            .AsNoTracking()
            .Where(x => x.Id == userId && x.CompanyId == companyId)
            .Select(x => new
            {
                x.Id,
                x.IsActive,
                x.PermissionVersion
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (user == null)
        {
            return Invalid("当前账号不存在");
        }

        if (!user.IsActive)
        {
            return Invalid("当前账号已停用");
        }

        if (user.PermissionVersion != permissionVersion)
        {
            return Invalid("当前登录状态已失效，请重新登录");
        }

        return new AuthSessionValidationResult
        {
            IsValid = true
        };
    }

    private static AuthSessionValidationResult Invalid(string message)
    {
        return new AuthSessionValidationResult
        {
            IsValid = false,
            Message = message
        };
    }
}
