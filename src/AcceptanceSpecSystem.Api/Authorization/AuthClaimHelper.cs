using System.Security.Claims;

namespace AcceptanceSpecSystem.Api.Authorization;

/// <summary>
/// 鉴权声明解析辅助
/// </summary>
public static class AuthClaimHelper
{
    public static int? GetUserId(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue("user_id");
        return int.TryParse(value, out var id) ? id : null;
    }

    public static int? GetCompanyId(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue("company_id");
        return int.TryParse(value, out var id) ? id : null;
    }
}
