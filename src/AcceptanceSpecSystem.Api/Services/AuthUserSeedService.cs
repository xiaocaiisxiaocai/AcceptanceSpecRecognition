using AcceptanceSpecSystem.Api.Options;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// API 宿主的权限种子启动适配器；持久化编排由 Application 用例负责。
/// Application 用例读取共享 navigation-manifest.json，并同步公司、组织、角色、权限和默认账号。
/// </summary>
public static class AuthUserSeedService
{
    public const string DefaultCompanyCode = AuthUserSeedAppService.DefaultCompanyCode;
    public const string DefaultCompanyName = AuthUserSeedAppService.DefaultCompanyName;
    public const string DefaultRootOrgCode = AuthUserSeedAppService.DefaultRootOrgCode;
    public const string DefaultRootOrgName = AuthUserSeedAppService.DefaultRootOrgName;
    public const string DefaultOperationalDepartmentCode = AuthUserSeedAppService.DefaultOperationalDepartmentCode;
    public const string DefaultOperationalDepartmentName = AuthUserSeedAppService.DefaultOperationalDepartmentName;
    public const string DefaultAdminUsername = AuthUserSeedAppService.DefaultAdminUsername;
    public const string DefaultCommonUsername = AuthUserSeedAppService.DefaultCommonUsername;
    public const string DevelopmentDefaultAdminPassword = AuthUserSeedAppService.DevelopmentDefaultAdminPassword;

    public static Task EnsureSeedUsersAsync(IServiceProvider services, ILogger logger)
    {
        var environment = services.GetRequiredService<IHostEnvironment>();
        var options = services.GetRequiredService<IOptions<AuthSeedOptions>>().Value;
        return AuthUserSeedAppService.EnsureSeedUsersAsync(
            services,
            logger,
            environment,
            new AuthSeedConfiguration(options.AdminPassword, options.CommonPassword));
    }
}
