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

    [Fact]
    public async Task GetPagedAsync_WhenPageSizeIsUnbounded_ShouldClampToRepositoryMaximum()
    {
        Context.AuditLogs.AddRange(Enumerable.Range(1, 201).Select(index => new AuditLog
        {
            Source = AuditLogSource.BackendRequest,
            Level = AuditLogLevel.Information,
            RequestPath = $"/api/test/{index}",
            CreatedAt = DateTime.UtcNow.AddSeconds(-index)
        }));
        await Context.SaveChangesAsync();

        var result = await _repository.GetPagedAsync(page: 1, pageSize: int.MaxValue);

        result.Total.Should().Be(201);
        result.Items.Should().HaveCount(AuditLogRepository.MaxPageSize);
    }

    [Fact]
    public async Task DeleteBeforeAsync_ShouldDeleteOnlyOneBoundedBatch()
    {
        var now = DateTime.UtcNow;
        Context.AuditLogs.AddRange(Enumerable.Range(1, 5).Select(index => new AuditLog
        {
            Source = AuditLogSource.BackendRequest,
            Level = AuditLogLevel.Information,
            RequestPath = $"/api/old/{index}",
            CreatedAt = now.AddDays(-10).AddSeconds(index)
        }));
        Context.AuditLogs.Add(new AuditLog
        {
            Source = AuditLogSource.BackendRequest,
            Level = AuditLogLevel.Information,
            RequestPath = "/api/new",
            CreatedAt = now
        });
        await Context.SaveChangesAsync();

        var deleted = await _repository.DeleteBeforeAsync(now.AddDays(-1), batchSize: 2);

        deleted.Should().Be(2);
        Context.AuditLogs.Count().Should().Be(4);
    }

    [Fact]
    public async Task DeleteOverflowAsync_ShouldKeepNewestRecordsAndDeleteBoundedBatch()
    {
        var now = DateTime.UtcNow;
        Context.AuditLogs.AddRange(Enumerable.Range(1, 5).Select(index => new AuditLog
        {
            Source = AuditLogSource.BackendRequest,
            Level = AuditLogLevel.Information,
            RequestPath = $"/api/{index}",
            CreatedAt = now.AddSeconds(index)
        }));
        await Context.SaveChangesAsync();

        var deleted = await _repository.DeleteOverflowAsync(maxRecordCount: 3, batchSize: 1);

        deleted.Should().Be(1);
        Context.AuditLogs.Should().HaveCount(4);
        Context.AuditLogs.Should().NotContain(item => item.RequestPath == "/api/1");
    }
}
