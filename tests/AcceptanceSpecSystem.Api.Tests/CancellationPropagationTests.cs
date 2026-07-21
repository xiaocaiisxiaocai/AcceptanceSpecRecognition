using AcceptanceSpecSystem.Api.Tests.Infrastructure;
using AcceptanceSpecSystem.Application.Services;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AcceptanceSpecSystem.Api.Tests;

public sealed class CancellationPropagationTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public CancellationPropagationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AcceptanceSpecQuery_ShouldPropagateCancellationToRepositoryQuery()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AcceptanceSpecQueryService>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => service.GetPagedAsync(
            new SpecAccessContext { UserId = 1, CompanyId = 1, IsAll = true },
            page: 1,
            pageSize: 20,
            cancellationToken: cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PromptTemplateProvider_ShouldPropagateCancellationToRepositoryQuery()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<IPromptTemplateProvider>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => provider.GetOrCreateSystemAsync(
            PromptTemplateScene.MatchingReview,
            $"cancel-{Guid.NewGuid():N}",
            "cancel test",
            "content",
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AuthDataScope_ShouldPropagateCancellationToDatabaseQueries()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAuthDataScopeService>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => service.GetScopeAsync(
            userId: 1,
            companyId: 1,
            resource: "spec",
            cancellationToken: cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
