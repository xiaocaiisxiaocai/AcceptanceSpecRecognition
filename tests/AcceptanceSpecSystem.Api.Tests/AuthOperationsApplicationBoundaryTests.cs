using FluentAssertions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class AuthOperationsApplicationBoundaryTests
{
    [Fact]
    public void AuthRbacAndOperationsUseCases_ShouldBeOwnedByApplication()
    {
        var applicationServices = new[]
        {
            "AuthLoginAppService.cs",
            "AuthAccessService.cs",
            "AuthDataScopeService.cs",
            "AuthPermissionQueryService.cs",
            "AuthRefreshSessionService.cs",
            "AuthRoleAppService.cs",
            "AuthSessionValidationService.cs",
            "AuthUserSeedAppService.cs",
            "OrgUnitAppService.cs",
            "SystemUserAppService.cs",
            "DashboardAppService.cs",
            "DatabaseBackupManager.cs"
        };

        foreach (var fileName in applicationServices)
        {
            File.Exists(Path.Combine(Root, "src", "AcceptanceSpecSystem.Application", "Services", fileName))
                .Should().BeTrue($"{fileName} 应由 Application 层拥有");

            File.Exists(Path.Combine(Root, "src", "AcceptanceSpecSystem.Api", "Services", fileName))
                .Should().BeFalse($"{fileName} 不应继续保留 Api 业务编排实现");
        }
    }

    [Fact]
    public void AuthAndOperationsProtocolAdapters_ShouldOnlyMapProtocolContext()
    {
        var authController = Read("src/AcceptanceSpecSystem.Api/Controllers/AuthController.cs");
        authController.Should().Contain("IAuthLoginAppService");
        authController.Should().NotContain("IUnitOfWork");
        authController.Should().NotContain("AppDbContext");
        authController.Should().NotMatchRegex(@"\bI[A-Za-z0-9]+Repository\b");

        var seedAdapter = Read("src/AcceptanceSpecSystem.Api/Services/AuthUserSeedService.cs");
        seedAdapter.Should().Contain("AuthUserSeedAppService.EnsureSeedUsersAsync");
        seedAdapter.Should().NotContain("AppDbContext");
        seedAdapter.Should().NotContain("IUnitOfWork");

        var backupHost = Read("src/AcceptanceSpecSystem.Api/Services/DatabaseBackupService.cs");
        backupHost.Should().Contain("DatabaseBackupManager");
        backupHost.Should().NotContain("AppDbContext");
        backupHost.Should().NotContain("IUnitOfWork");
    }

    [Fact]
    public void ApplicationUseCases_ShouldNotDependOnClaimsOrHttpTypes()
    {
        var files = new[]
        {
            "AuthLoginAppService.cs",
            "AuthSessionValidationService.cs",
            "SystemUserAppService.cs",
            "DashboardAppService.cs"
        };

        foreach (var fileName in files)
        {
            var content = Read($"src/AcceptanceSpecSystem.Application/Services/{fileName}");
            content.Should().NotContain("ClaimsPrincipal", $"{fileName} 应接收显式应用输入而不是 HTTP ClaimsPrincipal");
            content.Should().NotContain("HttpContext", $"{fileName} 不应感知 HTTP 上下文");
            content.Should().NotContain("IFormFile", $"{fileName} 不应感知 ASP.NET 上传类型");
        }
    }

    [Fact]
    public void ApplicationDependencyInjection_ShouldOwnAuthRbacAndOperationsRegistrations()
    {
        var application = Read("src/AcceptanceSpecSystem.Application/ServiceCollectionExtensions.cs");
        var api = Read("src/AcceptanceSpecSystem.Api/ServiceCollectionExtensions.cs");

        application.Should().Contain("IAuthLoginAppService, AuthLoginAppService");
        application.Should().Contain("IAuthRoleAppService, AuthRoleAppService");
        application.Should().Contain("ISystemUserAppService, SystemUserAppService");
        application.Should().Contain("IDashboardAppService, DashboardAppService");
        application.Should().Contain("DatabaseBackupManager");

        api.Should().NotContain("IAuthRoleAppService, AuthRoleAppService");
        api.Should().NotContain("ISystemUserAppService, SystemUserAppService");
        api.Should().NotContain("IDashboardAppService, DashboardAppService");
        api.Should().NotContain("AddSingleton<DatabaseBackupManager>");
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string Root
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "AcceptanceSpecSystem.sln")))
                    return current.FullName;
                current = current.Parent;
            }

            throw new InvalidOperationException("未找到仓库根目录");
        }
    }
}
