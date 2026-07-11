using System.Net;
using System.Text.Json;
using AcceptanceSpecSystem.Application.Options;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcceptanceSpecSystem.Api.Tests;

public class DatabaseBackupApiTests : IClassFixture<DatabaseBackupApiTests.DatabaseBackupApiFactory>
{
    private readonly HttpClient _client;
    private readonly RecordingDatabaseBackupExecutor _executor;

    public DatabaseBackupApiTests(DatabaseBackupApiFactory factory)
    {
        _client = factory.CreateClient();
        _executor = factory.Executor;
    }

    [Fact]
    public async Task PutOptions_ShouldPersistOptionsForSubsequentReads()
    {
        var response = await _client.PutAsync(
            "/api/database-backup/options",
            ApiClientJson.ToJsonContent(new
            {
                enabled = true,
                runAtLocalTime = "03:15",
                backupDirectory = "/backup/mysql",
                retentionCount = 12
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await _client.GetAsync("/api/database-backup");
        var result = await getResponse.ReadAsAsync<ApiResponse<JsonElement>>();
        var options = result.Data.GetProperty("options");

        options.GetProperty("enabled").GetBoolean().Should().BeTrue();
        options.GetProperty("runAtLocalTime").GetString().Should().Be("03:15");
        options.GetProperty("backupDirectory").GetString().Should().Be("/backup/mysql");
        options.GetProperty("retentionCount").GetInt32().Should().Be(12);
    }

    [Fact]
    public async Task Run_ShouldExecuteBackupAndReturnLastResult()
    {
        _executor.Result = new DatabaseBackupExecutionResult("acceptance-20260522031500.sql.gz", 1024);

        await _client.PutAsync(
            "/api/database-backup/options",
            ApiClientJson.ToJsonContent(new
            {
                enabled = true,
                runAtLocalTime = "03:15",
                backupDirectory = "/backup/mysql",
                retentionCount = 12
            }));

        var response = await _client.PostAsync("/api/database-backup/run", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.ReadAsAsync<ApiResponse<JsonElement>>();
        result.Code.Should().Be(0);

        result.Data.GetProperty("status").GetProperty("lastSucceeded").GetBoolean().Should().BeTrue();
        result.Data.GetProperty("status").GetProperty("lastFileName").GetString().Should().Be("acceptance-20260522031500.sql.gz");
        _executor.Calls.Should().Be(1);
        _executor.Options.BackupDirectory.Should().Be("/backup/mysql");
    }

    public sealed class DatabaseBackupApiFactory : ApiWebApplicationFactory
    {
        public RecordingDatabaseBackupExecutor Executor { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDatabaseBackupExecutor>();
                services.AddSingleton<IDatabaseBackupExecutor>(Executor);
            });
        }
    }

    public sealed class RecordingDatabaseBackupExecutor : IDatabaseBackupExecutor
    {
        public int Calls { get; private set; }
        public DatabaseBackupOptions Options { get; private set; } = new();
        public DatabaseBackupExecutionResult Result { get; set; } = new("backup.sql.gz", 1);

        public Task<DatabaseBackupExecutionResult> BackupAsync(
            DatabaseBackupOptions options,
            CancellationToken cancellationToken)
        {
            Calls++;
            Options = options;
            return Task.FromResult(Result);
        }
    }
}
