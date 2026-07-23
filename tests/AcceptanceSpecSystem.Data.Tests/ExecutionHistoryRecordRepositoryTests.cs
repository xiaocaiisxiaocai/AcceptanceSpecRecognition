using AcceptanceSpecSystem.Data.Entities;
using AcceptanceSpecSystem.Data.Repositories;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

public class ExecutionHistoryRecordRepositoryTests : TestBase
{
    private readonly ExecutionHistoryRecordRepository _repository;

    public ExecutionHistoryRecordRepositoryTests()
    {
        _repository = new ExecutionHistoryRecordRepository(Context);
    }

    [Fact]
    public async Task GetPagedOwnedAsync_ShouldFilterByOwnerKeywordTaskTypeAndOrderByNewest()
    {
        var now = DateTime.UtcNow;
        Context.ExecutionHistoryRecords.AddRange(
            CreateRecord("task-old", "smart-fill", "older.xlsx", 1, 10, now.AddMinutes(-2)),
            CreateRecord("task-new", "smart-fill", "target.xlsx", 1, 10, now.AddMinutes(-1)),
            CreateRecord("task-other-type", "batch-reply", "target.xlsx", 1, 10, now),
            CreateRecord("task-other-user", "smart-fill", "target.xlsx", 1, 11, now),
            CreateRecord("task-other-company", "smart-fill", "target.xlsx", 2, 10, now));
        await Context.SaveChangesAsync();

        var result = await _repository.GetPagedOwnedAsync(
            companyId: 1,
            userId: 10,
            page: 1,
            pageSize: 10,
            keyword: "target",
            taskType: "smart-fill");

        result.Total.Should().Be(1);
        result.Items.Should().ContainSingle();
        result.Items[0].TaskId.Should().Be("task-new");
    }

    [Fact]
    public async Task GetOwnedByTaskIdAsync_ShouldRespectCompanyAndUserOwnership()
    {
        Context.ExecutionHistoryRecords.AddRange(
            CreateRecord("task-001", "smart-fill", "a.xlsx", 1, 10, DateTime.UtcNow),
            CreateRecord("task-002", "smart-fill", "b.xlsx", 1, 11, DateTime.UtcNow));
        await Context.SaveChangesAsync();

        var owned = await _repository.GetOwnedByTaskIdAsync("task-001", companyId: 1, userId: 10);
        var otherUser = await _repository.GetOwnedByTaskIdAsync("task-001", companyId: 1, userId: 11);

        owned.Should().NotBeNull();
        owned!.TaskId.Should().Be("task-001");
        otherUser.Should().BeNull();
    }

    [Fact]
    public async Task GetPagedOwnedAsync_WhenPageSizeIsUnbounded_ShouldClampToRepositoryMaximum()
    {
        var now = DateTime.UtcNow;
        Context.ExecutionHistoryRecords.AddRange(Enumerable.Range(1, 201).Select(index =>
            CreateRecord($"task-{index}", "smart-fill", $"{index}.xlsx", 1, 10, now.AddSeconds(-index))));
        await Context.SaveChangesAsync();

        var result = await _repository.GetPagedOwnedAsync(1, 10, 1, int.MaxValue);

        result.Total.Should().Be(201);
        result.Items.Should().HaveCount(ExecutionHistoryRecordRepository.MaxPageSize);
    }

    [Fact]
    public async Task DeleteBeforeAsync_ShouldDeleteOnlyOneBoundedBatch()
    {
        var now = DateTime.UtcNow;
        Context.ExecutionHistoryRecords.AddRange(Enumerable.Range(1, 5).Select(index =>
            CreateRecord($"old-{index}", "smart-fill", $"{index}.xlsx", 1, 10, now.AddDays(-10).AddSeconds(index))));
        Context.ExecutionHistoryRecords.Add(CreateRecord("new", "smart-fill", "new.xlsx", 1, 10, now));
        await Context.SaveChangesAsync();

        var deleted = await _repository.DeleteBeforeAsync(now.AddDays(-1), batchSize: 2);

        deleted.Should().Be(2);
        Context.ExecutionHistoryRecords.Count().Should().Be(4);
    }

    [Fact]
    public async Task DeleteOverflowAsync_ShouldKeepNewestRecordsAndDeleteBoundedBatch()
    {
        var now = DateTime.UtcNow;
        Context.ExecutionHistoryRecords.AddRange(Enumerable.Range(1, 5).Select(index =>
            CreateRecord($"task-{index}", "smart-fill", $"{index}.xlsx", 1, 10, now.AddSeconds(index))));
        await Context.SaveChangesAsync();

        var deleted = await _repository.DeleteOverflowAsync(maxRecordCount: 3, batchSize: 1);

        deleted.Should().Be(1);
        Context.ExecutionHistoryRecords.Should().HaveCount(4);
        Context.ExecutionHistoryRecords.Should().NotContain(item => item.TaskId == "task-1");
    }

    private static ExecutionHistoryRecord CreateRecord(
        string taskId,
        string taskType,
        string sourceFileName,
        int companyId,
        int userId,
        DateTime createdAt)
    {
        return new ExecutionHistoryRecord
        {
            TaskId = taskId,
            TaskType = taskType,
            SourceFileName = sourceFileName,
            CompanyId = companyId,
            CreatedByUserId = userId,
            DetailJson = "{}",
            CreatedAt = createdAt
        };
    }
}
