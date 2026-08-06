using AcceptanceSpecSystem.Api.Services;
using AcceptanceSpecSystem.Application.Contracts;
using AcceptanceSpecSystem.Application.Options;
using AcceptanceSpecSystem.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using DataAiServicePurpose = AcceptanceSpecSystem.Data.Entities.AiServicePurpose;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class AiServiceStartupPreloadServiceTests
{
    [Fact]
    public async Task StartAsync_WhenEnabled_ShouldTriggerBothPurposesWithoutWaitingForInference()
    {
        var selection = new RecordingSelectionService(expectedCalls: 2);
        var services = new ServiceCollection();
        services.AddScoped<IAiServiceSelectionAppService>(_ => selection);
        await using var provider = services.BuildServiceProvider();
        using var preload = new AiServiceStartupPreloadService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new TestHostEnvironment("Production"),
            Microsoft.Extensions.Options.Options.Create(
                new AiServiceReadinessOptions { PreloadOnStartup = true }),
            NullLogger<AiServiceStartupPreloadService>.Instance);

        await preload.StartAsync(CancellationToken.None);
        await selection.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await preload.StopAsync(CancellationToken.None);

        selection.Purposes.Should().Equal(
            DataAiServicePurpose.Llm,
            DataAiServicePurpose.Embedding);
    }

    [Theory]
    [InlineData(false, "Production")]
    [InlineData(true, "Testing")]
    public async Task StartAsync_WhenDisabledOrTesting_ShouldNotTriggerExternalPreload(
        bool enabled,
        string environmentName)
    {
        var selection = new RecordingSelectionService(expectedCalls: 1);
        var services = new ServiceCollection();
        services.AddScoped<IAiServiceSelectionAppService>(_ => selection);
        await using var provider = services.BuildServiceProvider();
        using var preload = new AiServiceStartupPreloadService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new TestHostEnvironment(environmentName),
            Microsoft.Extensions.Options.Options.Create(
                new AiServiceReadinessOptions { PreloadOnStartup = enabled }),
            NullLogger<AiServiceStartupPreloadService>.Instance);

        await preload.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await preload.StopAsync(CancellationToken.None);

        selection.Purposes.Should().BeEmpty();
    }

    private sealed class RecordingSelectionService(int expectedCalls)
        : IAiServiceSelectionAppService
    {
        public List<DataAiServicePurpose> Purposes { get; } = [];
        public TaskCompletionSource Completed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AiServiceSelectionDto> GetSelectionAsync(
            DataAiServicePurpose purpose,
            CancellationToken cancellationToken = default) =>
            RecordAsync(purpose);

        public Task<AiServiceSelectionDto> PreloadPreferredAsync(
            DataAiServicePurpose purpose,
            CancellationToken cancellationToken = default) =>
            RecordAsync(purpose);

        private Task<AiServiceSelectionDto> RecordAsync(DataAiServicePurpose purpose)
        {
            Purposes.Add(purpose);
            if (Purposes.Count >= expectedCalls)
                Completed.TrySetResult();
            return Task.FromResult(new AiServiceSelectionDto
            {
                Status = "checking",
                ServiceId = Purposes.Count,
                Message = "正在检测"
            });
        }
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "AcceptanceSpecSystem.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
