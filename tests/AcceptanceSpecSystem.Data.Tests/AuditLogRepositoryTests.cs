using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

public class AuditLogRepositoryTests : TestBase
{
    private readonly AuditLogRepository _repository;

    public AuditLogRepositoryTests()
    {
        _repository = new AuditLogRepository(Context);
    }

    [Fact]
    public async Task GetPagedAsync_ShouldApplyCombinedFiltersAndReturnNewestFirst()
    {
        var now = DateTime.UtcNow;
        Context.AuditLogs.AddRange(
            new AuditLog
            {
                Source = AuditLogSource.BackendRequest,
                Level = AuditLogLevel.Warning,
                Username = "alice",
                RequestMethod = "POST",
                RequestPath = "/api/specs",
                StatusCode = 409,
                Details = "conflict target",
                CreatedAt = now.AddMinutes(-2)
            },
            new AuditLog
            {
                Source = AuditLogSource.BackendRequest,
                Level = AuditLogLevel.Warning,
                Username = "alice",
                RequestMethod = "POST",
                RequestPath = "/api/specs/2",
                StatusCode = 500,
                Details = "conflict target",
                CreatedAt = now.AddMinutes(-1)
            },
            new AuditLog
            {
                Source = AuditLogSource.FrontendEvent,
                Level = AuditLogLevel.Warning,
                Username = "alice",
                RequestMethod = "POST",
                RequestPath = "/api/specs",
                StatusCode = 409,
                Details = "conflict target",
                CreatedAt = now
            },
            new AuditLog
            {
                Source = AuditLogSource.BackendRequest,
                Level = AuditLogLevel.Information,
                Username = "bob",
                RequestMethod = "GET",
                RequestPath = "/api/specs",
                StatusCode = 200,
                Details = "other",
                CreatedAt = now
            });
        await Context.SaveChangesAsync();

        var result = await _repository.GetPagedAsync(
            page: 1,
            pageSize: 10,
            source: AuditLogSource.BackendRequest,
            level: AuditLogLevel.Warning,
            username: "alice",
            requestMethod: "POST",
            keyword: "conflict",
            from: now.AddMinutes(-3),
            to: now,
            minStatusCode: 400,
            maxStatusCode: 599);

        result.Total.Should().Be(2);
        result.Items.Select(item => item.StatusCode)
            .Should()
            .Equal(500, 409);
    }
}
