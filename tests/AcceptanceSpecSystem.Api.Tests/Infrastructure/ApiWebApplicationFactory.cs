using System.Data.Common;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Application.Models;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Services;
using AcceptanceSpecSystem.Data.Context;
using AcceptanceSpecSystem.Data.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Tests.Infrastructure;

public class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestAdminPassword = "TestAdmin!20260326";
    public const string TestCommonPassword = "TestCommon!20260326";

    private DbConnection? _connection;
    private string? _tempRoot;

    protected virtual bool UseTestAuthentication => true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _tempRoot ??= Path.Combine(Path.GetTempPath(), "AcceptanceSpecSystem.Api.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AuthSeed:AdminPassword"] = TestAdminPassword,
                ["AuthSeed:CommonPassword"] = TestCommonPassword,
                ["ApiRateLimits:Login:PermitLimit"] = "10000",
                ["ApiRateLimits:RefreshToken:PermitLimit"] = "10000",
                ["ApiRateLimits:Upload:PermitLimit"] = "10000",
                ["ApiRateLimits:AiHeavy:PermitLimit"] = "10000",
                ["BrowserAuth:RefreshCookieName"] = "__Host-acceptance-refresh",
                ["BrowserAuth:CookieSecure"] = "true",
                ["BrowserAuth:AllowInsecureHttp"] = "false",
                ["BrowserAuth:AllowedOrigins:0"] = AuthCookieTestHelper.AllowedOrigin,
                ["FileCompareTemporaryStorage:Root"] = Path.Combine(_tempRoot, "file-compare")
            });
        });

        builder.ConfigureServices(services =>
        {
            // 集成测试逐请求共享同一条 SQLite 内存连接，后台周期任务若同时访问该连接会制造
            // 与业务行为无关的 database locked/连接释放竞态。后台服务各自有独立生命周期测试，
            // API 工厂只验证请求路径，因此在此统一移除宿主后台任务。
            services.RemoveAll<IHostedService>();

            // Replace AppDbContext (MySQL) with SQLite in-memory
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.RemoveAll(typeof(AppDbContext));

            // 使用唯一命名的共享内存库：锚连接负责数据库生命周期，每个 DbContext
            // 通过相同连接字符串创建独立连接，才能真实覆盖并发 HTTP scope。
            // 直接复用同一个 DbConnection 会让两个并发 refresh 在测试基础设施层偶发 500。
            var databaseName = $"AcceptanceSpecSystemTests-{Guid.NewGuid():N}";
            var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared;Default Timeout=30";
            _connection = new SqliteConnection(connectionString);
            _connection.Open();

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlite(connectionString);
            });

            // Replace file storage with an isolated temp directory
            services.RemoveAll(typeof(IFileStorageService));
            services.AddSingleton<IFileStorageService>(new TestFileStorageService(_tempRoot));

            // Replace LLM services with test doubles to avoid external calls
            services.RemoveAll(typeof(ILlmReviewService));
            services.RemoveAll(typeof(ILlmEquivalenceAdjudicationService));
            services.RemoveAll(typeof(ILlmCandidateRerankService));
            services.RemoveAll(typeof(ILlmDocumentStructureAdjudicationService));
            services.RemoveAll(typeof(ILlmColumnSemanticRecallService));
            services.RemoveAll(typeof(IEmbeddingService));
            services.AddScoped<ILlmReviewService, TestLlmReviewService>();
            services.AddScoped<ILlmEquivalenceAdjudicationService, TestLlmEquivalenceAdjudicationService>();
            services.AddScoped<ILlmCandidateRerankService, TestLlmCandidateRerankService>();
            services.AddScoped<ILlmDocumentStructureAdjudicationService, TestLlmDocumentStructureAdjudicationService>();
            services.AddScoped<ILlmColumnSemanticRecallService, TestLlmColumnSemanticRecallService>();
            services.AddScoped<IEmbeddingService, TestEmbeddingService>();

            // 本地 HTTPListener 测试使用随机端口；通过显式 loopback CIDR + 端口集合
            // 构造与生产相同的安全 transport，不把测试环境变成绕过 SSRF 的普通 HttpClient。
            services.RemoveAll<ISafeAiHttpClientFactory>();
            services.AddSingleton<ISafeAiHttpClientFactory>(_ =>
            {
                var policy = new AiEndpointAccessPolicy(
                    new AiDnsResolver(),
                    new StaticOptionsMonitor<AiEndpointSecurityOptions>(new AiEndpointSecurityOptions
                    {
                        PrivateNetworkAllowlist =
                        [
                            new AiEndpointPrivateNetworkRule
                            {
                                Cidr = "127.0.0.0/8",
                                Ports = Enumerable.Range(1, 65535).ToList()
                            }
                        ]
                    }));
                return new SafeAiHttpMessageHandlerFactory(
                    policy,
                    new AiSocketConnector(
                        new AiSocketFactory(),
                        new AiSocketConnectOperation()));
            });

            // 使用测试鉴权（默认 admin），避免真实 JWT 依赖影响集成测试
            if (UseTestAuthentication)
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName,
                    _ => { });
            }

            // DataProtection 测试隔离（Ephemeral 密钥不持久化）
            services.AddDataProtection().UseEphemeralDataProtectionProvider();

            // Ensure schema created
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            SeedSystemUsersIfNeeded(db);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        try { _connection?.Dispose(); } catch { /* ignore */ }
        try
        {
            if (!string.IsNullOrWhiteSpace(_tempRoot) && Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch { /* ignore */ }
    }

    private static void SeedSystemUsersIfNeeded(AppDbContext db)
    {
        if (db.SystemUsers.Any())
            return;

        var company = new OrgCompany
        {
            Code = AuthUserSeedService.DefaultCompanyCode,
            Name = AuthUserSeedService.DefaultCompanyName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.OrgCompanies.Add(company);
        db.SaveChanges();

        var rootOrgUnit = new OrgUnit
        {
            CompanyId = company.Id,
            ParentId = null,
            UnitType = OrgUnitType.Company,
            Code = AuthUserSeedService.DefaultRootOrgCode,
            Name = AuthUserSeedService.DefaultRootOrgName,
            Path = "/",
            Depth = 0,
            Sort = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.OrgUnits.Add(rootOrgUnit);
        db.SaveChanges();
        rootOrgUnit.Path = $"/{rootOrgUnit.Id}/";

        var roleAdmin = new AuthRole
        {
            CompanyId = company.Id,
            Code = "admin",
            Name = "管理员",
            Description = "测试管理员",
            IsBuiltIn = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var roleCommon = new AuthRole
        {
            CompanyId = company.Id,
            Code = "common",
            Name = "普通用户",
            Description = "测试普通用户",
            IsBuiltIn = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.AuthRoles.AddRange(roleAdmin, roleCommon);
        db.SaveChanges();

        var permissionReadSystemUser = new AuthPermission
        {
            Code = "api:system-user:read",
            Name = "接口-系统用户-读取",
            PermissionType = PermissionType.Api,
            Resource = "system-user",
            Action = "read",
            IsBuiltIn = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.AuthPermissions.Add(permissionReadSystemUser);
        db.SaveChanges();

        db.AuthRolePermissions.Add(new AuthRolePermission
        {
            RoleId = roleAdmin.Id,
            PermissionId = permissionReadSystemUser.Id
        });
        db.SaveChanges();

        var passwordService = new AuthPasswordService();
        var admin = new SystemUser
        {
            CompanyId = company.Id,
            Username = AuthUserSeedService.DefaultAdminUsername,
            PasswordHash = passwordService.HashPassword(TestAdminPassword),
            Nickname = "管理员",
            Avatar = "https://avatars.githubusercontent.com/u/44761321",
            IsActive = true,
            PermissionVersion = 1,
            CreatedAt = DateTime.UtcNow
        };
        var common = new SystemUser
        {
            CompanyId = company.Id,
            Username = AuthUserSeedService.DefaultCommonUsername,
            PasswordHash = passwordService.HashPassword(TestCommonPassword),
            Nickname = "普通用户",
            Avatar = "https://avatars.githubusercontent.com/u/52823142",
            IsActive = true,
            PermissionVersion = 1,
            CreatedAt = DateTime.UtcNow
        };
        db.SystemUsers.AddRange(admin, common);
        db.SaveChanges();

        db.AuthUserRoles.AddRange(
            new AuthUserRole
            {
                UserId = admin.Id,
                RoleId = roleAdmin.Id,
                CreatedAt = DateTime.UtcNow
            },
            new AuthUserRole
            {
                UserId = common.Id,
                RoleId = roleCommon.Id,
                CreatedAt = DateTime.UtcNow
            });

        db.AuthUserOrgUnits.AddRange(
            new AuthUserOrgUnit
            {
                UserId = admin.Id,
                OrgUnitId = rootOrgUnit.Id,
                IsPrimary = true,
                CreatedAt = DateTime.UtcNow
            },
            new AuthUserOrgUnit
            {
                UserId = common.Id,
                OrgUnitId = rootOrgUnit.Id,
                IsPrimary = true,
                CreatedAt = DateTime.UtcNow
            });

        db.SaveChanges();
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}

public sealed class RealJwtApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override bool UseTestAuthentication => false;
}

public sealed class InsecureHttpRealJwtApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override bool UseTestAuthentication => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["BrowserAuth:RefreshCookieName"] = AuthCookieTestHelper.InsecureRefreshCookieName,
                ["BrowserAuth:CsrfCookieName"] = AuthCookieTestHelper.CsrfCookieName,
                ["BrowserAuth:CookieSecure"] = "false",
                ["BrowserAuth:CookieSameSite"] = "Strict",
                ["BrowserAuth:CookiePath"] = "/",
                ["BrowserAuth:CookieDomain"] = null,
                ["BrowserAuth:AllowInsecureHttp"] = "true",
                ["BrowserAuth:AllowedOrigins:0"] = AuthCookieTestHelper.AllowedOrigin
            }));
    }
}

public sealed class AuditWriteFailureApiWebApplicationFactory : ApiWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAuditTrailAppService>();
            services.AddScoped<IAuditTrailAppService, ThrowingAuditTrailAppService>();
        });
    }

    private sealed class ThrowingAuditTrailAppService : IAuditTrailAppService
    {
        public Task WriteAsync(
            AuditTrailWriteCommand command,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException(new InvalidOperationException("模拟审计写入失败"));
        }

        public Task<PagedResult<AuditLogListItemDto>> GetPagedAsync(
            int page,
            int pageSize,
            AuditLogSource? source,
            AuditLogLevel? level,
            string? username,
            string? requestMethod,
            string? keyword,
            DateTime? from,
            DateTime? to,
            int? minStatusCode,
            int? maxStatusCode,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<AuditLogDetailDto?> GetByIdAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<int> DeleteByRangeAsync(
            DateTime? from,
            DateTime? to,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
