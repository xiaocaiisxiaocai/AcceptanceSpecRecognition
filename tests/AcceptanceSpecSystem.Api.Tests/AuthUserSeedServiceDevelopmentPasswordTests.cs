using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Data.Context;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AcceptanceSpecSystem.Api.Tests;

public class AuthUserSeedServiceDevelopmentPasswordTests
{
    [Fact]
    public async Task EnsureSeedUsersAsync_WhenDevelopmentAdminPasswordMissing_ShouldSeedAdminPasswordAsAdmin()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<IAuthPasswordService, AuthPasswordService>();
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment
        {
            EnvironmentName = Environments.Development
        });
        services.AddSingleton<IOptions<AuthSeedOptions>>(Microsoft.Extensions.Options.Options.Create(new AuthSeedOptions()));

        await using var serviceProvider = services.BuildServiceProvider();

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        }

        await AuthUserSeedService.EnsureSeedUsersAsync(serviceProvider, NullLogger.Instance);

        await using var verificationScope = serviceProvider.CreateAsyncScope();
        var verificationDbContext = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordService = verificationScope.ServiceProvider.GetRequiredService<IAuthPasswordService>();
        var admin = await verificationDbContext.SystemUsers
            .SingleAsync(user => user.Username == AuthUserSeedService.DefaultAdminUsername);

        passwordService.VerifyPassword(admin.PasswordHash, "admin").Should().BeTrue();
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "AcceptanceSpecSystem.Api.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
