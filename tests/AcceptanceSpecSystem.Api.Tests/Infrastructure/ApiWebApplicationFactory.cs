using System.Data.Common;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Data.Context;
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
            services.AddScoped<ILlmReviewService, TestLlmReviewService>();
            services.AddScoped<ILlmSuggestionService, TestLlmSuggestionService>();

            // Ensure schema created
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
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
}

