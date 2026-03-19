using System.Data.Common;
using AcceptanceSpecSystem.Api.Services;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcceptanceSpecSystem.Api.Tests.Infrastructure;

public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    private DbConnection? _connection;
    private string? _tempRoot;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Replace AppDbContext (MySQL) with SQLite in-memory
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.RemoveAll(typeof(AppDbContext));

            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            // Replace file storage with an isolated temp directory
            services.RemoveAll(typeof(IFileStorageService));
            _tempRoot = Path.Combine(Path.GetTempPath(), "AcceptanceSpecSystem.Api.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            services.AddSingleton<IFileStorageService>(new TestFileStorageService(_tempRoot));

            // Replace LLM services with test doubles to avoid external calls
            services.RemoveAll(typeof(ILlmReviewService));
            services.RemoveAll(typeof(ILlmSuggestionService));
            services.RemoveAll(typeof(IEmbeddingService));
            services.RemoveAll(typeof(ITextSimilarityService));
            services.AddScoped<ILlmReviewService, TestLlmReviewService>();
            services.AddScoped<ILlmSuggestionService, TestLlmSuggestionService>();
            services.AddScoped<IEmbeddingService, TestEmbeddingService>();
            services.AddSingleton<ITextSimilarityService, TestTextSimilarityService>();

            // 使用测试鉴权（默认 admin），避免真实 JWT 依赖影响集成测试
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName,
                _ => { });

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
            CreatedAt = DateTime.Now
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
            CreatedAt = DateTime.Now
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
            CreatedAt = DateTime.Now
        };
        var roleCommon = new AuthRole
        {
            CompanyId = company.Id,
            Code = "common",
            Name = "普通用户",
            Description = "测试普通用户",
            IsBuiltIn = true,
            IsActive = true,
            CreatedAt = DateTime.Now
        };
        db.AuthRoles.AddRange(roleAdmin, roleCommon);
        db.SaveChanges();

        var passwordService = new AuthPasswordService();
        var admin = new SystemUser
        {
            CompanyId = company.Id,
            Username = AuthUserSeedService.DefaultAdminUsername,
            PasswordHash = passwordService.HashPassword(AuthUserSeedService.DefaultAdminPassword),
            Nickname = "管理员",
            Avatar = "https://avatars.githubusercontent.com/u/44761321",
            IsActive = true,
            PermissionVersion = 1,
            CreatedAt = DateTime.Now
        };
        var common = new SystemUser
        {
            CompanyId = company.Id,
            Username = AuthUserSeedService.DefaultCommonUsername,
            PasswordHash = passwordService.HashPassword(AuthUserSeedService.DefaultCommonPassword),
            Nickname = "普通用户",
            Avatar = "https://avatars.githubusercontent.com/u/52823142",
            IsActive = true,
            PermissionVersion = 1,
            CreatedAt = DateTime.Now
        };
        db.SystemUsers.AddRange(admin, common);
        db.SaveChanges();

        db.AuthUserRoles.AddRange(
            new AuthUserRole
            {
                UserId = admin.Id,
                RoleId = roleAdmin.Id,
                CreatedAt = DateTime.Now
            },
            new AuthUserRole
            {
                UserId = common.Id,
                RoleId = roleCommon.Id,
                CreatedAt = DateTime.Now
            });

        db.AuthUserOrgUnits.AddRange(
            new AuthUserOrgUnit
            {
                UserId = admin.Id,
                OrgUnitId = rootOrgUnit.Id,
                IsPrimary = true,
                CreatedAt = DateTime.Now
            },
            new AuthUserOrgUnit
            {
                UserId = common.Id,
                OrgUnitId = rootOrgUnit.Id,
                IsPrimary = true,
                CreatedAt = DateTime.Now
            });

        db.SaveChanges();
    }
}

