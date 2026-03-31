using AcceptanceSpecSystem.Api.Options;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Data.Context;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

    [Fact]
    public async Task EnsureSeedUsersAsync_WhenUsingDevelopmentFallbackPasswords_ShouldNotLogPlaintextPasswords()
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

        var logger = new CollectingLogger();
        await AuthUserSeedService.EnsureSeedUsersAsync(serviceProvider, logger);

        logger.Messages.Should().Contain(message => message.Contains("临时开发口令", StringComparison.Ordinal));
        logger.Messages.Should().NotContain(message => message.Contains("admin=", StringComparison.OrdinalIgnoreCase),
            "日志不应输出管理员明文口令");
        logger.Messages.Should().NotContain(message => message.Contains("common=", StringComparison.OrdinalIgnoreCase),
            "日志不应输出普通用户明文口令");
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "AcceptanceSpecSystem.Api.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
