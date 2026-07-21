using System.Diagnostics;
using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application.Options;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class DatabaseBackupExecutorTests
{
    [Fact]
    public async Task BackupAsync_WhenProcessSucceeds_ShouldAtomicallyPublishFinalFile()
    {
        using var directory = new TemporaryDirectory();
        var runner = new StubProcessRunner(async (output, cancellationToken) =>
        {
            await output.WriteAsync("CREATE TABLE test(id int);"u8.ToArray(), cancellationToken);
            return new MySqlDumpProcessResult(0, string.Empty);
        });
        var executor = CreateExecutor(runner);

        var result = await executor.BackupAsync(CreateOptions(directory.Path), CancellationToken.None);

        File.Exists(Path.Combine(directory.Path, result.FileName)).Should().BeTrue();
        Directory.EnumerateFiles(directory.Path, "*.partial").Should().BeEmpty();
    }

    [Fact]
    public async Task BackupAsync_WhenProcessReturnsNonZero_ShouldDeletePartialFile()
    {
        using var directory = new TemporaryDirectory();
        var runner = new StubProcessRunner(async (output, cancellationToken) =>
        {
            await output.WriteAsync("partial"u8.ToArray(), cancellationToken);
            return new MySqlDumpProcessResult(2, "dump failed");
        });
        var executor = CreateExecutor(runner);

        var action = () => executor.BackupAsync(CreateOptions(directory.Path), CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*dump failed*");
        Directory.EnumerateFiles(directory.Path).Should().BeEmpty();
    }

    [Fact]
    public async Task BackupAsync_WhenCancelled_ShouldDeletePartialAndNotPublishFinalFile()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        var runner = new StubProcessRunner(async (output, cancellationToken) =>
        {
            await output.WriteAsync("partial"u8.ToArray(), cancellationToken);
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return new MySqlDumpProcessResult(0, string.Empty);
        });
        var executor = CreateExecutor(runner);

        var action = () => executor.BackupAsync(CreateOptions(directory.Path), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        Directory.EnumerateFiles(directory.Path).Should().BeEmpty();
    }

    private static MySqlDumpDatabaseBackupExecutor CreateExecutor(IMySqlDumpProcessRunner runner)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=mysql;Port=3306;Database=acceptance;User=acceptance;Password=secret;"
            })
            .Build();
        return new MySqlDumpDatabaseBackupExecutor(
            configuration,
            NullLogger<MySqlDumpDatabaseBackupExecutor>.Instance,
            runner);
    }

    private static DatabaseBackupOptions CreateOptions(string directory) => new()
    {
        BackupDirectory = directory,
        RetentionCount = 3
    };

    private sealed class StubProcessRunner : IMySqlDumpProcessRunner
    {
        private readonly Func<Stream, CancellationToken, Task<MySqlDumpProcessResult>> _run;

        public StubProcessRunner(Func<Stream, CancellationToken, Task<MySqlDumpProcessResult>> run)
        {
            _run = run;
        }

        public Task<MySqlDumpProcessResult> RunAsync(
            ProcessStartInfo startInfo,
            Stream standardOutputDestination,
            CancellationToken cancellationToken)
            => _run(standardOutputDestination, cancellationToken);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "AcceptanceSpecSystem.DatabaseBackupExecutorTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
