namespace AcceptanceSpecSystem.Api.Authorization;

/// <summary>
/// 权限匹配器（支持 * 通配）
/// </summary>
public static class PermissionMatcher
{
    public const string AllPermission = "*:*:*";

    public static bool HasPermission(IEnumerable<string> grantedPermissions, string requiredPermission)
    {
        if (string.IsNullOrWhiteSpace(requiredPermission))
            return false;

        var required = requiredPermission.Trim();
        var granted = grantedPermissions
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (granted.Length == 0)
            return false;

        if (granted.Any(p => p.Equals(AllPermission, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (granted.Any(p => p.Equals(required, StringComparison.OrdinalIgnoreCase)))
            return true;

        var requiredParts = required.Split(':', StringSplitOptions.TrimEntries);
        foreach (var permission in granted)
        {
            var grantedParts = permission.Split(':', StringSplitOptions.TrimEntries);
            if (grantedParts.Length != requiredParts.Length)
                continue;

            var matched = true;
            for (var i = 0; i < requiredParts.Length; i++)
            {
                if (grantedParts[i] == "*")
                    continue;

                if (!grantedParts[i].Equals(requiredParts[i], StringComparison.OrdinalIgnoreCase))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
                return true;
        }

        return false;
    }
}
